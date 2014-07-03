using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Common;
using KrakenBot.Business;


namespace KrakenBot
{
    internal class KrakenRequestHelper
    {
        private const string BASE_URL = "https://api.kraken.com";
        private const byte RETRY_COUNT = 5;
        private const int RETRY_DELAY = 750;

        private readonly Logger _logger;
        private readonly WebProxy _webProxy;


        public KrakenRequestHelper(Logger logger)
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


        internal BalanceResponse GetAccountBalance()
        {
            var data = sendPostRequest("Balance");
            return Helpers.DeserializeJSON<BalanceResponse>(data);
        }

        internal DateTime GetServerTime()
        {
            var client = new WebClient();

            if (null != _webProxy)
                client.Proxy = _webProxy;

            var data = client.DownloadString("https://api.kraken.com/0/public/Time");
            return Helpers.DeserializeJSON<TimeResponse>(data).result.TimeTyped;
        }

        internal MarketDepthResponse GetMarketDepth(byte maxItems = 15)
        {
            var client = new WebClient();

            if (null != _webProxy)
                client.Proxy = _webProxy;

            var data = client.DownloadString("https://api.kraken.com/0/public/Depth?pair=XXBTZEUR&count=" + maxItems);
            var market = Helpers.DeserializeJSON<MarketDepthResponse>(data);

            return market;
        }

        internal OrderInfoResponse GetOrderInfo(string orderId)
        {
            var data = sendPostRequest("QueryOrders", String.Format("&txid={0}&trades=false", orderId));

            //NOTE: there's variable with dynamic name of order ID. It's replaced with constant string to avoid tricky JSON parsing
            data = data.Replace(orderId, "orderData");

            var order = Helpers.DeserializeJSON<OrderInfoResponse>(data);
            return order;
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

        internal TradeHistoryResponse GetTradeHistory()
        {
            var client = new WebClient();

            if (null != _webProxy)
                client.Proxy = _webProxy;

            var data = client.DownloadString("https://api.kraken.com/0/public/Trades?pair=XXBTZEUR");
            var trades = Helpers.DeserializeJSON<TradeHistoryResponse>(data);

            return trades;
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


        private string sendPostRequest(string method, string postData = null)
        {
            // generate a 64 bit nonce using a timestamp at tick resolution
            Int64 nonce = DateTime.Now.Ticks;
            postData = "nonce=" + nonce + postData;

            string path = "/0/private/" + method;
            string address = BASE_URL + path;
            HttpWebRequest webRequest = (HttpWebRequest)WebRequest.Create(address);
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


        private byte[] sha256_hash(String value)
        {
            using (SHA256 hash = SHA256.Create())
            {
                Byte[] result = hash.ComputeHash(Encoding.UTF8.GetBytes(value));
                return result;
            }
        }

        private byte[] getHash(byte[] keyByte, byte[] messageBytes)
        {
            using (var hmacsha512 = new HMACSHA512(keyByte))
            {
                Byte[] result = hmacsha512.ComputeHash(messageBytes);
                return result;
            }
        }
    }
}
