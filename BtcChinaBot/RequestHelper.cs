using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using BtcChinaBot.Business;


namespace BtcChinaBot
{
    internal class RequestHelper
    {
        private const string BASE_URL = "https://api.btcchina.com/api_trade_v1.php";
        private const string TRADE_HISTORY_URL = "http://data.btcchina.com/data/historydata";
        private const byte RETRY_COUNT = 5;
        private const int RETRY_DELAY = 750;

        private readonly Logger _logger;
        private readonly WebProxy _webProxy;
        private ulong _lastId;
        


        internal RequestHelper(Logger logger)
        {
            _logger = logger;
            var proxyHost = Configuration.GetValue("proxyHost");
            var proxyPort = Configuration.GetValue("proxyPort");
            if (null != proxyHost && null != proxyPort)
            {
                _webProxy = new WebProxy(proxyHost, int.Parse(proxyPort));
                _webProxy.Credentials = CredentialCache.DefaultCredentials;
            }
        }


        private static string getTonce()
        {
            TimeSpan timeSpan = DateTime.UtcNow - new DateTime(1970, 1, 1);
            long milliSeconds = Convert.ToInt64(timeSpan.TotalMilliseconds * 1000);
            return Convert.ToString(milliSeconds);
        }

        private ulong ID
        {
            get { return ++_lastId; }
        }


        internal AccountInfoResponse GetAccountInfo()
        {
            var data = doRequest("getAccountInfo");
            return deserializeJSON<AccountInfoResponse>(data);
        }

        /// <summary>
        /// If <paramref name="since"/> is NULL, get last 100 executed trades. Otherwise gets trade history long enough to
        /// cover the since time.
        /// </summary>
        internal List<TradeResponse> GetTradeHistory(DateTime? since = null)
        {
            var client = new WebClient();

            if (null != _webProxy)
                client.Proxy = _webProxy;

            var data = client.DownloadString(TRADE_HISTORY_URL);
            var trades = deserializeJSON<List<TradeResponse>>(data);

            while (null != since && trades.First().DateTyped > since.Value)
            {
                int firstTradeId = int.Parse(trades.First().tid);
                data = client.DownloadString(TRADE_HISTORY_URL + "?since=" + (firstTradeId-100));
                var trades2 = deserializeJSON<List<TradeResponse>>(data);
                //Remove overlapping record
                trades2.RemoveAt(0);
                trades.InsertRange(0, trades2);
            }

            return trades;
        }

        internal MarketDepthResponse GetMarketDepth()
        {
            var data = doRequest("getMarketDepth2"/*TODO, "10,\"BTCCNY\""*/);     //Default is 10 orders, BTC/CNY market
            return deserializeJSON<MarketDepthResponse>(data);
        }

        internal OrderInfoResponse GetOrderInfo(int orderId)
        {
            var data = doRequest("getOrder", orderId.ToString());
            return deserializeJSON<OrderInfoResponse>(data);
        }

        /// <summary>Create a SELL order. Returns order ID.</summary>
        /// <param name="price">If NULL, execute a market order</param>
        /// <param name="amount">Amount of BTC to sell</param>
        internal int PlaceSellOrder(double? price, ref double amount)
        {
            string paramString = price == null
                ? String.Format("null,{0:0.####}", amount)
                : String.Format("{0:0.##},{1:0.####}", price, amount);
            var data = doRequest("sellOrder2", paramString);

            var error = deserializeJSON<ErrorResponse>(data);
            if (null != error.error && !String.IsNullOrEmpty(error.error.message))
            {
                if ("Insufficientbalance" == error.error.message)
                {
                    //BTC balance changed meanwhile, probably SELL order was (partially) filled
                    _logger.AppendMessage("WARN: Insufficient balance reported when creating SELL order with amount="+amount, true, ConsoleColor.Yellow);
                    var accountInfo = GetAccountInfo();
                    var newAmount = accountInfo.result.balance.btc.amount;
                    _logger.AppendMessage("Available account balance is " + newAmount + " BTC. Using this as amount for SELL order", true, ConsoleColor.Yellow);
                    amount = double.Parse(newAmount);
                    return PlaceSellOrder(price, ref amount);
                }

                throw new Exception(String.Format("Error creating SELL order (paramString={0}). Message={1}", paramString, error.error.message));
            }

            return deserializeJSON<SellOrderResponse>(data).result;
        }

        /// <summary>Update SELL order by re-creating it. Returns new order ID.</summary>
        internal int UpdateSellOrder(int orderId, double price, ref double amount)
        {
            //First try to cancel the old order. Recreate it then.
            if (CancelOrder(orderId))
                return PlaceSellOrder(price, ref amount);
            //It's been closed meanwhile. Leave it be, very next iteration will find and handle properly
            return orderId;
        }

        /// <summary>Create a BUY order. Returns order ID.</summary>
        /// <param name="price">Use NULL for market order</param>
        /// <param name="amount">Amount of BTC to buy</param>
        internal int PlaceBuyOrder(double? price, double amount)
        {
            string paramString;
            if (null == price)
                paramString = String.Format("null,{0:0.####}", amount);
            else
                paramString = String.Format("{0:0.##},{1:0.####}", price, amount);
            var data = doRequest("buyOrder2", paramString);

            var error = deserializeJSON<ErrorResponse>(data);
            if (null != error.error && !String.IsNullOrEmpty(error.error.message))
                throw new Exception(String.Format("Error creating BUY order (paramString={0}). Message={1}", paramString, error.error.message));

            return deserializeJSON<BuyOrderResponse>(data).result;
        }

        /// <summary>Update BUY order by re-creating it. Returns new order ID.</summary>
        internal int UpdateBuyOrder(int orderId, double price, double amount)
        {
            //Cancel the old order, recreate
            if (CancelOrder(orderId))
                return PlaceBuyOrder(price, amount);
            //It's been closed meanwhile. Leave it be, very next iteration will find and handle properly
            return orderId;
        }


        internal bool CancelOrder(int orderId)
        {
            //Try more times because sometimes we get status "Order processing"
            string data = null;
            for (byte i = 0; i < RETRY_COUNT; i++)
            {
                data = doRequest("cancelOrder", orderId.ToString());
                var error1 = deserializeJSON<ErrorResponse>(data);
                if (null == error1 || null == error1.error || String.IsNullOrEmpty(error1.error.message))
                    break;      //Success

                if ("Order already completed" == error1.error.message)
                {
                    _logger.AppendMessage("Can't cancel order ID=" + orderId + " because was closed", true, ConsoleColor.Yellow);
                    return false;     
                }
                if ("Order already cancelled" == error1.error.message)
                {
                    _logger.AppendMessage("WARNING: Service reports order ID=" + orderId + " is already cancelled while trying to cancel", true, ConsoleColor.Yellow);
                    return true;
                }

                _logger.AppendMessage(String.Format("Attempt: {0}:: cancelOrder ID={1} failed with error={2}", i, orderId, error1.error.message), true, ConsoleColor.Yellow);
            }

            var error2 = deserializeJSON<ErrorResponse>(data);
            if (null != error2 && null != error2.error && !String.IsNullOrEmpty(error2.error.message))
                throw new Exception(String.Format("Error canceling order ID={0}. Message={1}", orderId, error2.error.message));

            bool success = deserializeJSON<CancelOrderResponse>(data).result;
            if (!success)
                throw new Exception(String.Format("Error canceling order ID={0}. Response status is FALSE.", orderId));

            return true;
        }

        #region private helpers

        private string doRequest(string methodName, string paramString = "")
        {
            ServicePointManager.ServerCertificateValidationCallback = delegate { return true; };

            var tonce = getTonce();
            var id = ID;
            NameValueCollection parameters = new NameValueCollection
            {
                {"tonce", tonce},
                {"accesskey", Configuration.AccessKey},
                {"requestmethod", "post"},
                {"id", id.ToString()},
                {"method", methodName},
                {"params", paramString}
            };
            string paramsHash = getHMACSHA1Hash(Configuration.SecretKey, buildQueryString(parameters));
            string base64String = Convert.ToBase64String(Encoding.ASCII.GetBytes(Configuration.AccessKey + ':' + paramsHash));

            string postData = "{\"method\":\"" + methodName + "\",\"params\":[" + paramString + "],\"id\":" + id + "}";

            WebException error = null;
            for (int i = 0; i < RETRY_COUNT; i++)
            {
                try
                {
                    return sendPostRequest(BASE_URL, base64String, tonce, postData);
                }
                catch (WebException we)
                {
                    var text = String.Format("(ATTEMPT {0}/3) Web request failed with exception={1}; status={2}", i, we.Message, we.Status);
                    _logger.AppendMessage(text, true, ConsoleColor.Yellow);
                    error = we;
                    Thread.Sleep(RETRY_DELAY);
                }
            }

            throw new Exception(String.Format("Web request failed {0} times in a row with error '{1}'. Giving up.", RETRY_COUNT, error.Message));
        }


        private string sendPostRequest(string url, string base64, string tonce, string postData)
        {
            WebRequest webRequest = WebRequest.Create(url);
            byte[] bytes = Encoding.ASCII.GetBytes(postData);

            if (null != _webProxy)
                webRequest.Proxy = _webProxy;

            webRequest.Method = "POST";
            webRequest.ContentType = "application/json-rpc";
            webRequest.ContentLength = bytes.Length;
            webRequest.Headers["Authorization"] = "Basic " + base64;
            webRequest.Headers["Json-Rpc-Tonce"] = tonce;

            // Send the json authentication post request
            using (Stream dataStream = webRequest.GetRequestStream())
            {
                dataStream.Write(bytes, 0, bytes.Length);
                dataStream.Close();
            }
            // Get authentication response
            using (WebResponse response = webRequest.GetResponse())
            {
                using (var stream = response.GetResponseStream())
                {
                    using (var reader = new StreamReader(stream))
                    {
                        var text = reader.ReadToEnd();
                        return text;
                    }
                }
            }
        }

        private static string buildQueryString(NameValueCollection parameters)
        {
            return String.Join("&", (from string key in parameters select key + "=" + parameters[key]).ToArray());
        }

        private static string getHMACSHA1Hash(string secret_key, string input)
        {
            HMACSHA1 hmacsha1 = new HMACSHA1(Encoding.ASCII.GetBytes(secret_key));
            MemoryStream stream = new MemoryStream(Encoding.ASCII.GetBytes(input));
            byte[] hashData = hmacsha1.ComputeHash(stream);

            // Format as hexadecimal string.
            StringBuilder hashBuilder = new StringBuilder();
            foreach (byte data in hashData)
                hashBuilder.Append(data.ToString("x2"));

            return hashBuilder.ToString();
        }

        private static T deserializeJSON<T>(string json)
        {
            using (var ms = new MemoryStream(Encoding.Unicode.GetBytes(json)))
            {
                var deserializer = new DataContractJsonSerializer(typeof(T));
                return (T)deserializer.ReadObject(ms);
            }
        }
        #endregion
    }
}
