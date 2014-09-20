using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Common;
using HuobiBot.Business;


namespace HuobiBot
{
    internal class HuobiApi
    {
        private const string TICKER_URL = "http://market.huobi.com/staticmarket/ticker_btc_json.js";
        private const string MARKET_URL = "http://market.huobi.com/staticmarket/depth_btc_json.js";
        private const string TRADE_STATS_URL = "http://market.huobi.com/staticmarket/detail_btc_json.js";
        private const string TRADING_API_URL = "https://api.huobi.com/api.php";
        private const byte RETRY_COUNT = 10;
        private const int RETRY_DELAY = 1000;
        private const int DATA_TIMEOUT = 10*60*1000;

        private readonly Logger _logger;
        private readonly long _timeOffset;
        private readonly WebProxy _webProxy;


        internal HuobiApi(Logger logger)
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
                _timeOffset = long.Parse(nonceOffset) / 10000000;
        }


        internal DateTime GetServerTime()
        {
            var client = new WebClient2(_logger, DATA_TIMEOUT);

            if (null != _webProxy)
                client.Proxy = _webProxy;

            var ticker = client.DownloadObject<TickerResponse>(TICKER_URL);
            return null==ticker ? DateTime.MinValue : ticker.ServerTime;
        }

        internal MarketDepthResponse GetMarketDepth()
        {
            var client = new WebClient2(_logger, DATA_TIMEOUT);

            if (null != _webProxy)
                client.Proxy = _webProxy;

            var depth = client.DownloadObject<MarketDepthResponse>(MARKET_URL);
            return depth;
        }

        internal TradeStatisticsResponse GetTradeStatistics()
        {
            var client = new WebClient2(_logger, DATA_TIMEOUT);

            if (null != _webProxy)
                client.Proxy = _webProxy;

            var trades = client.DownloadObject<TradeStatisticsResponse>(TRADE_STATS_URL);
            return trades;
        }

        internal List<Candle> GetCandles()
        {
            var client = new WebClient2(_logger, DATA_TIMEOUT);

            if (null != _webProxy)
                client.Proxy = _webProxy;

            var data = client.DownloadStringSafe("http://market.huobi.com/staticmarket/btc_kline_001_json.js");
            if (null == data)
                return null;

            //NOTE: add auxiliary root variable to avoid tricky JSON parsing
            data = "{\"candleData\":" + data + "}";
            var candles = Helpers.DeserializeJSON<CandlesResponse>(data);
            return candles.MergeAsCandles(5);
        }

        internal AccountInfoResponse GetAccountInfo()
        {
            var data = doRequest("get_account_info");
            return Helpers.DeserializeJSON<AccountInfoResponse>(data);
        }

        private int _buyRetryCounter;

        internal OrderInfoResponse GetOrderInfo(int orderId)
        {
            var data = doRequest("order_info", new List<Tuple<string, string>>{new Tuple<string, string>("id", orderId.ToString())});
            var debug = Helpers.DeserializeJSON<OrderInfoResponse>(data);

            var error = Helpers.DeserializeJSON<ErrorResponse>(data);
            if (!String.IsNullOrEmpty(error.Description))
            {
                //TODO: this code smells because repeats. And probably should repeat in all private calls. Refactor it.
                if (70 == error.code && ++_buyRetryCounter <= RETRY_COUNT)
                {
                    //Simetimes it takes so long to server to respond, that it returns "Invalid submitting time"
                    _logger.AppendMessage("The server returned " + error.Description + " when creating a BUY order. Trying again...", true, ConsoleColor.Yellow);
                    return GetOrderInfo(orderId);
                }
                throw new Exception(String.Format("Error getting order ID={0} info. Message={1}", orderId, error.Description));
            }
            _buyRetryCounter = 0;

            return debug;
        }

        internal int PlaceBuyOrder(double price, double amount)
        {
            var paramz = new List<Tuple<string, string>>
            {
                new Tuple<string, string>("price", price.ToString("0.00")),
                new Tuple<string, string>("amount", amount.ToString("0.0000")),
            };
            var data = doRequest("buy", paramz);

            var error = Helpers.DeserializeJSON<ErrorResponse>(data);
            if (!String.IsNullOrEmpty(error.Description))
            {
                if (70 == error.code && ++_buyRetryCounter <= RETRY_COUNT)
                {
                    //Simetimes it takes so long to server to respond, that it returns "Invalid submitting time"
                    _logger.AppendMessage("The server returned " + error.Description + " when creating a BUY order. Trying again...", true, ConsoleColor.Yellow);
                    return PlaceBuyOrder(price, amount);
                }
                throw new Exception(String.Format("Error creating BUY order (price={0}; amount={1}). Message={2}", price, amount, error.Description));
            }
            _buyRetryCounter = 0;

            var debug = Helpers.DeserializeJSON<BasicResponse>(data);
            return debug.id;
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
                new Tuple<string, string>("price", price.ToString("0.00")),
                new Tuple<string, string>("amount", amount.ToString("0.0000")),
            };
            var data = doRequest("sell", paramz);

            var error = Helpers.DeserializeJSON<ErrorResponse>(data);
            if (!String.IsNullOrEmpty(error.Description))
            {
                if (70 == error.code && ++_buyRetryCounter <= RETRY_COUNT)
                {
                    //TODO: refactor repeating code
                    _logger.AppendMessage("The server returned " + error.Description + " when creating a SELL order. Trying again...", true, ConsoleColor.Yellow);
                    return PlaceSellOrder(price, ref amount);
                }
                if (10 == error.code)
                {
                    //BTC balance changed meanwhile, probably SELL order was (partially) filled
                    _logger.AppendMessage("WARN: Insufficient balance reported when creating SELL order with amount=" + amount, true, ConsoleColor.Yellow);
                    var accountInfo = GetAccountInfo();
                    amount = accountInfo.AvailableBtc;
                    _logger.AppendMessage("Available account balance is " + amount + " BTC. Using this as amount for SELL order", true, ConsoleColor.Yellow);
                    return PlaceSellOrder(price, ref amount);
                }
                throw new Exception(String.Format("Error creating SELL order (price={0}; amount={1}). Message={2}", price, amount, error.Description));
            }

            var debug = Helpers.DeserializeJSON<BasicResponse>(data);
            return debug.id;
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
            var data = doRequest("cancel_order", new List<Tuple<string, string>> { new Tuple<string, string>("id", orderId.ToString()) });

            var error = Helpers.DeserializeJSON<ErrorResponse>(data);
            if (!String.IsNullOrEmpty(error.Description))
            {
                //Already filled
                if (41 == error.code)
                {
                    _logger.AppendMessage("Can't cancel order ID=" + orderId + " because was closed", true, ConsoleColor.Yellow);
                    return false;
                }
                if (42 == error.code)
                {
                    _logger.AppendMessage("WARNING: Service reports order ID=" + orderId + " is already cancelled while trying to cancel", true, ConsoleColor.Yellow);
                    return true;
                }

                _logger.AppendMessage(String.Format("cancelOrder ID={0} failed with error={1}", orderId, error.Description), true, ConsoleColor.Yellow);
            }

            return Helpers.DeserializeJSON<BasicResponse>(data).result == "success";
        }



        #region private helpers
        //TODO: lot of copy-pasta here. Refactor!

        private string doRequest(string methodName, List<Tuple<string, string>> paramz = null)
        {
            ServicePointManager.ServerCertificateValidationCallback = delegate { return true; };

            WebException exc = null;
            var delay = 0;
            for (int i = 1; i <= RETRY_COUNT; i++)
            {
                var serverTimeDiff = new TimeSpan(-2, 0, 0);
                var totalSeconds = (long)Math.Round((DateTime.Now - new DateTime(1970, 1, 1) + serverTimeDiff).TotalSeconds);   //TODO: using DateTime.Now is not ideal, should use server time
                totalSeconds += _timeOffset;

                var parameters = new List<Tuple<string, string>>
                {
                    //Must be sorted by key
                    new Tuple<string, string>("access_key", Configuration.AccessKey),
                    new Tuple<string, string>("created", totalSeconds.ToString()),
                    new Tuple<string, string>("method", methodName),
                    new Tuple<string, string>("secret_key", Configuration.SecretKey)
                };
                if (null != paramz && paramz.Any())
                {
                    parameters.AddRange(paramz);
                    parameters = parameters.OrderBy(tuple => tuple.Item1).ToList();
                }

                //Finally add MD5 hash sign. It's out of sorting.
                var sign = getMD5Hash(buildQueryString(parameters));
                parameters.Add(new Tuple<string, string>("sign", sign));

                var postData = buildQueryString(parameters);

                delay += RETRY_DELAY;
                try
                {
                    var text = sendPostRequest(TRADING_API_URL, postData);
                    _logger.LastResponse = text;
                    return text;
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

        private string sendPostRequest(string url, string postData)
        {
            var webRequest = WebRequest.CreateHttp(url);
            webRequest.KeepAlive = false;
            webRequest.Timeout = 150000;        //150 seconds
            byte[] bytes = Encoding.ASCII.GetBytes(postData);

            if (null != _webProxy)
                webRequest.Proxy = _webProxy;

            webRequest.Method = "POST";
            webRequest.ContentType = "application/x-www-form-urlencoded";
            webRequest.ContentLength = bytes.Length;

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
                        _logger.LastResponse = text;
                        return text;
                    }
                }
            }
        }

        private static string buildQueryString(IEnumerable<Tuple<string, string>> parameters)
        {
            var query = parameters.Aggregate("", (current, tuple) => current + (tuple.Item1 + "=" + tuple.Item2 + "&"));
            return query.TrimEnd(new []{'&'});
        }

        private static string getMD5Hash(string text)
        {
            var md5 = MD5.Create();
            byte[] bytes = Encoding.ASCII.GetBytes(text);
            byte[] hashData = md5.ComputeHash(bytes);

            // Format as hexadecimal string.
            StringBuilder hashBuilder = new StringBuilder();
            foreach (byte data in hashData)
                hashBuilder.Append(data.ToString("x2"));

            return hashBuilder.ToString().ToLower();
        }

        
        #endregion
    }
}
