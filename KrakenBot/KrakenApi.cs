using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Common;
using KrakenBot.Business;


namespace KrakenBot
{
    internal class KrakenApi
    {
        private const string BASE_URL = "https://api.kraken.com";
        private const byte RETRY_COUNT = 10;
        private const int RETRY_DELAY = 2000;

        private readonly Logger _logger;
        private readonly long _nonceOffset;
        private readonly WebProxy _webProxy;


        public KrakenApi(Logger logger)
        {
            _logger = logger;
            var proxyHost = Configuration.GetValue("proxyHost");
            var proxyPort = Configuration.GetValue("proxyPort");
            if (null != proxyHost && null != proxyPort)
            {
                _webProxy = new WebProxy(proxyHost, int.Parse(proxyPort));
                _webProxy.Credentials = CredentialCache.DefaultCredentials;
            }

            var nonceOffset = Configuration.GetValue("nonce_offset");
            if (!String.IsNullOrEmpty(nonceOffset))
                _nonceOffset = long.Parse(nonceOffset);
        }


        internal DateTime GetServerTime()
        {
            var data = sendGetRequest("https://api.kraken.com/0/public/Time");
            return Helpers.DeserializeJSON<TimeResponse>(data).result.TimeTyped;
        }

        internal MarketDepthResponse GetMarketDepth(byte maxItems = 15)
        {
            var data = sendGetRequest("https://api.kraken.com/0/public/Depth?pair=XXBTZEUR&count=" + maxItems);
            return Helpers.DeserializeJSON<MarketDepthResponse>(data);
        }

        internal TradeHistoryResponse GetTradeHistory()
        {
            var data = sendGetRequest("https://api.kraken.com/0/public/Trades?pair=XXBTZEUR");
            return Helpers.DeserializeJSON<TradeHistoryResponse>(data);
        }

        internal BalanceResponse GetAccountBalance()
        {
            var data = sendPostRequest("Balance");
            return Helpers.DeserializeJSON<BalanceResponse>(data);
        }

        internal OrderInfoResponse GetOrderInfo(string orderId)
        {
            var data = sendPostRequest("QueryOrders", String.Format("&txid={0}&trades=false", orderId));

            //NOTE: there's variable with dynamic name of order ID. It's replaced with constant string to avoid tricky JSON parsing
            data = data.Replace(orderId, "orderData");
            return Helpers.DeserializeJSON<OrderInfoResponse>(data);
        }

        internal string PlaceBuyOrder(double? price, double amount)
        {
            string postData = String.Format("&pair=XXBTZEUR&type=buy&ordertype={0}&volume={1}", null == price ? "market" : "limit", amount);
            if (null != price)
                postData += "&price=" + price;

            var data = sendPostRequest("AddOrder", postData);

            var error = Helpers.DeserializeJSON<ErrorResponse>(data);
            if (null != error.error && error.error.Any())
                throw new Exception(String.Format("Error creating BUY order (postData={0}). Messages={1}", postData, String.Join("; ", error.error)));

            var response = Helpers.DeserializeJSON<AddOrderResponse>(data);
            return response.result.txid.First();
        }

        internal string UpdateBuyOrder(string orderId, double price, double amount)
        {
            //Cancel the old order, recreate
            if (CancelOrder(orderId))
                return PlaceBuyOrder(price, amount);
            //It's been closed meanwhile. Leave it be, very next iteration will find and handle properly
            return orderId;
        }

        internal string PlaceSellOrder(double? price, ref double amount)
        {
            string postData = String.Format("&pair=XXBTZEUR&type=sell&ordertype={0}&volume={1}", null == price ? "market" : "limit", amount);
            if (null != price)
                postData += "&price=" + price;

            var data = sendPostRequest("AddOrder", postData);

            var error = Helpers.DeserializeJSON<ErrorResponse>(data);
            if (null != error.error && error.error.Any())
            {
                if (error.error.Contains("EOrder:Insufficient funds"))
                {
                    //BTC balance changed meanwhile, probably SELL order was (partially) filled
                    _logger.AppendMessage("WARN: Insufficient balance reported when creating SELL order with amount=" + amount, true, ConsoleColor.Yellow);
                    var accountInfo = GetAccountBalance();
                    amount = accountInfo.result.BalanceBtc;     //WARN: Kraken doesn't tell "frozen" funds and we're lazy to deduce from orders.
                    _logger.AppendMessage("Available account balance is " + amount + " BTC. Using this as amount for SELL order", true, ConsoleColor.Yellow);
                    return PlaceSellOrder(price, ref amount);
                }

                throw new Exception(String.Format("Error creating SELL order (price={0}; amount={1}). Message={2}", price, amount, String.Join(", ", error.error)));
            }

            var response = Helpers.DeserializeJSON<AddOrderResponse>(data);
            return response.result.txid.First();
        }

        internal string UpdateSellOrder(string orderId, double price, ref double amount)
        {
            //Cancel the old order, recreate
            if (CancelOrder(orderId))
                return PlaceSellOrder(price, ref amount);
            //It's been closed meanwhile. Leave it be, very next iteration will find and handle properly
            return orderId;
        }

        internal bool CancelOrder(string orderId)
        {
            var data = sendPostRequest("CancelOrder", "&txid=" + orderId);

            var error = Helpers.DeserializeJSON<ErrorResponse>(data);
            if (null != error.error && error.error.Any())
            {
                if (error.error.Contains("EOrder:Unknown order"))
                {
                    _logger.AppendMessage("Order ID=" + orderId + " reported unknown during cancelling. Probably closed/cancelled", true, ConsoleColor.Yellow);
                    return false;
                }
            }

            var cancel = Helpers.DeserializeJSON<CancelOrderResponse>(data).result;
            if (cancel.count != 1)
                _logger.AppendMessage(String.Format("Unexpected response for CancelOrder. Count={0}", cancel.count));
            return true;
        }


        #region private helpers

        private string sendGetRequest(string url)
        {
            var client = new WebClient();

            if (null != _webProxy)
                client.Proxy = _webProxy;

            WebException exc = null;
            for (int i = 1; i <= RETRY_COUNT; i++)
            {
                try
                {
                    return client.DownloadString(url);
                }
                catch (WebException we)
                {
                    var text = String.Format("(ATTEMPT {0}/{1}) Web request failed with exception={2}; status={3}", i, RETRY_COUNT, we.Message, we.Status);
                    _logger.AppendMessage(text, true, ConsoleColor.Yellow);
                    exc = we;
                    Thread.Sleep(RETRY_DELAY);
                }
            }

            throw new Exception(String.Format("Web request failed {0} times in a row with error '{1}'. Giving up.", RETRY_COUNT, exc.Message));
        }

        private string sendPostRequest(string method, string postData = null)
        {
            // generate a 64 bit nonce using a timestamp at tick resolution
            Int64 nonce = DateTime.Now.Ticks;
            nonce += _nonceOffset;
            postData = "nonce=" + nonce + postData;

            string path = "/0/private/" + method;
            string address = BASE_URL + path;
            var webRequest = (HttpWebRequest)WebRequest.Create(address);
            webRequest.KeepAlive = false;
            webRequest.ContentType = "application/x-www-form-urlencoded";
            webRequest.Method = "POST";
            webRequest.Headers.Add("API-Key", Configuration.AccessKey);

            if (null != _webProxy)
                webRequest.Proxy = _webProxy;

            byte[] base64DecodedSecred = Convert.FromBase64String(Configuration.SecretKey);

            var np = nonce + Convert.ToChar(0) + postData;

            var pathBytes = Encoding.UTF8.GetBytes(path);
            var hash256Bytes = sha256_hash(np);
            var z = new byte[pathBytes.Count() + hash256Bytes.Count()];
            pathBytes.CopyTo(z, 0);
            hash256Bytes.CopyTo(z, pathBytes.Count());

            var signature = getHash(base64DecodedSecred, z);
            webRequest.Headers.Add("API-Sign", Convert.ToBase64String(signature));

            using (var writer = new StreamWriter(webRequest.GetRequestStream()))
            {
                writer.Write(postData);
            }
            
            WebException exc = null;
            for (int i = 1; i <= RETRY_COUNT; i++)
            {
                try
                {
                    using (WebResponse webResponse = webRequest.GetResponse())
                    {
                        using (Stream stream = webResponse.GetResponseStream())
                        {
                            using (StreamReader reader = new StreamReader(stream))
                            {
                                var text = reader.ReadToEnd();
                                return text;
                            }
                        }
                    }
                }
                catch (WebException we)
                {
                    var text = String.Format("(ATTEMPT {0}/{1}) Web request failed with exception={2}; status={3}", i, RETRY_COUNT, we.Message, we.Status);
                    _logger.AppendMessage(text, true, ConsoleColor.Yellow);
                    exc = we;

                    //DEBUG, TODO: delete
                    if (null != we.Response)
                    {
                        var resp = new StreamReader(we.Response.GetResponseStream()).ReadToEnd();
                        _logger.AppendMessage("DEBUG: " + resp, true, ConsoleColor.Magenta);
                    }


                    Thread.Sleep(RETRY_DELAY);
                }
            }

            throw new Exception(String.Format("Web request failed {0} times in a row with error '{1}'. Giving up.", RETRY_COUNT, exc.Message));
        }

        private static byte[] sha256_hash(String value)
        {
            using (SHA256 hash = SHA256.Create())
            {
                return hash.ComputeHash(Encoding.UTF8.GetBytes(value));
            }
        }

        private static byte[] getHash(byte[] keyByte, byte[] messageBytes)
        {
            using (var hmacsha512 = new HMACSHA512(keyByte))
            {
                return hmacsha512.ComputeHash(messageBytes);
            }
        }
        #endregion
    }
}
