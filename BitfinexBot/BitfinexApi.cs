using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using BitfinexBot.Business;
using Common;


namespace BitfinexBot
{
    internal class BitfinexApi
    {
        private const string BASE_URL = "https://api.bitfinex.com/v1/";
        private const byte RETRY_COUNT = 6;
        private const int RETRY_DELAY = 1000;

        private readonly Logger _logger;
        private readonly long _nonceOffset;
        private readonly WebProxy _webProxy;

        public BitfinexApi(Logger logger)
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
            var data = sendGetRequest(BASE_URL + "pubticker/ltcusd");
            return Helpers.DeserializeJSON<TickerResponse>(data).ServerTime;
        }

        internal MarketDepthResponse GetMarketDepth(byte maxItems = 15)
        {
            var data = sendGetRequest(String.Format("{0}book/ltcusd?limit_bids={1}&limit_asks={1}", BASE_URL, maxItems));
            return Helpers.DeserializeJSON<MarketDepthResponse>(data);
        }

        internal List<Trade> GetTradeHistory(DateTime? since = null)
        {
            var path = BASE_URL + "trades/ltcusd";
            if (null != since)
            {
                var serverDiff = new TimeSpan(-2, 0, 0);
                var seconds = (since.Value - new DateTime(1970, 1, 1) + serverDiff).TotalSeconds;
                path += "?timestamp=" + (int)seconds;
            }

            var data = sendGetRequest(path);
            var trades = Helpers.DeserializeJSON<List<Trade>>(data);
            return trades;
        }

        /// <summary>Get LTC exchange balance</summary>
        internal Balance GetAccountBalance()
        {
            var data = sendPostRequest("balances");

            var balances = Helpers.DeserializeJSON<List<Balance>>(data);
            return balances.First(b => b.type == "exchange" && b.currency == "ltc");
        }

        internal OrderInforResponse GetOrderInfo(int orderId)
        {
            var paramz = new List<Tuple<string, string>>
            {
                new Tuple<string, string>("\"order_id\"", orderId.ToString())
            };
            var data = sendPostRequest("order/status", paramz);

            return Helpers.DeserializeJSON<OrderInforResponse>(data);
        }

        internal int PlaceBuyOrder(double price, double amount)
        {
            var paramz = new List<Tuple<string, string>>
            {
                new Tuple<string, string>("\"symbol\"", "\"ltcusd\""),
                new Tuple<string, string>("\"amount\"", "\"" + amount.ToString("0.000") + "\""),
                new Tuple<string, string>("\"price\"", "\"" + price.ToString("0.000") + "\""),
                new Tuple<string, string>("\"exchange\"", "\"bitfinex\""),
                new Tuple<string, string>("\"side\"", "\"buy\""),
                new Tuple<string, string>("\"type\"", "\"exchange limit\"")
            };

            var data = sendPostRequest("order/new", paramz);

            var error = Helpers.DeserializeJSON<ErrorResponse>(data);
            if (!String.IsNullOrEmpty(error.message))
                throw new Exception(String.Format("Error creating BUY order (price={0}, amount={1}). Messages={2}", price, amount, error.message));

            var response = Helpers.DeserializeJSON<OrderInforResponse>(data);
            return response.id;
        }

        internal int UpdateBuyOrder(int orderId, double price, double amount)
        {
            //Cancel the old order, recreate
            if (CancelOrder(orderId))
                return PlaceBuyOrder(price, amount);
            //It's been closed meanwhile. Leave it be, very next iteration will find and handle properly
            return orderId;
        }

        internal int PlaceSellOrder(double price, ref double amount)
        {
            var paramz = new List<Tuple<string, string>>
            {
                new Tuple<string, string>("\"symbol\"", "\"ltcusd\""),
                new Tuple<string, string>("\"amount\"", "\"" + amount.ToString("0.000") + "\""),
                new Tuple<string, string>("\"price\"", "\"" + price.ToString("0.000") + "\""),
                new Tuple<string, string>("\"exchange\"", "\"bitfinex\""),
                new Tuple<string, string>("\"side\"", "\"sell\""),
                new Tuple<string, string>("\"type\"", "\"exchange limit\"")
            };

            var data = sendPostRequest("order/new", paramz);

            var error = Helpers.DeserializeJSON<ErrorResponse>(data);
            if (!String.IsNullOrEmpty(error.message))
            {
                if ("Invalid order: not enough balance" == error.message)
                {
                    //LTC balance changed meanwhile, probably SELL order was (partially) filled
                    _logger.AppendMessage("WARN: Insufficient balance reported when creating SELL order with amount=" + amount, true, ConsoleColor.Yellow);
                    var accountInfo = GetAccountBalance();
                    amount = accountInfo.AvailableLtc;
                    _logger.AppendMessage("Available account balance is " + amount + " LTC. Using this as amount for SELL order", true, ConsoleColor.Yellow);
                    return PlaceSellOrder(price, ref amount);
                }

                throw new Exception(String.Format("Error creating SELL order (price={0}; amount={1}). Message={2}", price, amount, error.message));
            }

            var response = Helpers.DeserializeJSON<OrderInforResponse>(data);
            return response.id;
        }

        internal int UpdateSellOrder(int orderId, double price, ref double amount)
        {
            //Cancel the old order, recreate
            if (CancelOrder(orderId))
                return PlaceSellOrder(price, ref amount);
            //It's been closed meanwhile. Leave it be, very next iteration will find and handle properly
            return orderId;
        }

        internal bool CancelOrder(int orderId)
        {
            var paramz = new List<Tuple<string, string>>
            {
                new Tuple<string, string>("\"order_id\"", orderId.ToString())
            };

            var data = sendPostRequest("order/cancel", paramz);

            var error = Helpers.DeserializeJSON<ErrorResponse>(data);
            if (!String.IsNullOrEmpty(error.message))
            {
                if ("Order could not be cancelled." == error.message)
                {
                    _logger.AppendMessage("Order ID=" + orderId + " couldn't be cancelled. Probably was closed.", true, ConsoleColor.Yellow);
                    return false;
                }

                throw new Exception(String.Format("Error cancelling order ID={0}. Message={1}", orderId, error.message));
            }

            var response = Helpers.DeserializeJSON<OrderInforResponse>(data);
            return response.is_cancelled;
        }

        #region private helpers

        private string sendGetRequest(string url)
        {
            var client = new WebClient();

            if (null != _webProxy)
                client.Proxy = _webProxy;

            WebException exc = null;
            var delay = 0;
            for (int i = 1; i <= RETRY_COUNT; i++)
            {
                delay += RETRY_DELAY;
                try
                {
                    return client.DownloadString(url);
                }
                catch (WebException we)
                {
                    var text = String.Format("(ATTEMPT {0}/{1}) Web request failed with exception={2}; status={3}", i, RETRY_COUNT, we.Message, we.Status);
                    _logger.AppendMessage(text, true, ConsoleColor.Yellow);
                    exc = we;
                    Thread.Sleep(delay);
                }
            }

            throw new Exception(String.Format("Web request failed {0} times in a row with error '{1}'. Giving up.", RETRY_COUNT, exc.Message));
        }

        private string sendPostRequest(string method, List<Tuple<string, string>> paramz = null)
        {
            long nonce = DateTime.Now.Ticks;
            nonce += _nonceOffset;

            string path = BASE_URL + method;
            string paramDict = "{" + String.Format("\"request\":\"/v1/{0}\",\"nonce\":\"{1}\"", method, nonce);
            if (null != paramz && paramz.Any())
            {
                foreach (var param in paramz)
                    paramDict += "," + param.Item1 + ":" + param.Item2;
            }
            paramDict += "}";
            string payload = Convert.ToBase64String(Encoding.UTF8.GetBytes(paramDict));

            var hmac = new HMACSHA384(Encoding.UTF8.GetBytes(Configuration.SecretKey));
            byte[] hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
            string hexHash = BitConverter.ToString(hash).Replace("-", "").ToLower();

            var headers = new NameValueCollection
            {
                {"X-BFX-APIKEY", Configuration.AccessKey},
                {"X-BFX-PAYLOAD", payload},
                {"X-BFX-SIGNATURE", hexHash}
            };

            var request = (HttpWebRequest)WebRequest.Create(path);
            request.KeepAlive = true;
            request.Method = "POST";

            if (null != _webProxy)
                request.Proxy = _webProxy;

            request.Headers.Add(headers);

            byte[] byteArray = Encoding.UTF8.GetBytes(paramDict);
            request.ContentLength = byteArray.Length;

            using (var writer = request.GetRequestStream())
            {
                writer.Write(byteArray, 0, byteArray.Length);
            }

            WebException exc = null;
            for (int i = 1; i <= RETRY_COUNT; i++)
            {
                try
                {
                    using (WebResponse response = request.GetResponse())
                    {
                        using (Stream stream = response.GetResponseStream())
                        {
                            using (var reader = new StreamReader(stream))
                            {
                                var text = reader.ReadToEnd();
                                return text;
                            }
                        }
                    }
                }
                catch (WebException we)
                {
                    //Business errors act as 400-ProtocolError, so must be sorted out
                    if (we.Response != null)
                    {
                        using (var errorResponse = (HttpWebResponse)we.Response)
                        {
                            using (var reader = new StreamReader(errorResponse.GetResponseStream()))
                            {
                                string json = reader.ReadToEnd();
                                var error = Helpers.DeserializeJSON<ErrorResponse>(json);
                                if (!String.IsNullOrEmpty(error.message))
                                    return json;
                            }
                        }
                    }

                    //Else real HTTP problem
                    var text = String.Format("(ATTEMPT {0}/{1}) Web request failed with exception={2}; status={3}", i, RETRY_COUNT, we.Message, we.Status);

                    _logger.AppendMessage(text, true, ConsoleColor.Yellow);
                    exc = we;
                    Thread.Sleep(RETRY_DELAY);
                }
            }

            throw new Exception(String.Format("Web request failed {0} times in a row with error '{1}'. Giving up.", RETRY_COUNT, exc.Message));
        }
        #endregion
    }
}
