using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using BtceBot.Business;
using Common;


namespace BtceBot
{
    internal class BtceApi
    {
        private const string DATA_BASE_URL = "https://btc-e.com/api/2/ltc_usd/";
        private const string TRADE_BASE_URL = "https://btc-e.com/tapi";
        private const byte RETRY_COUNT = 6;
        private const int RETRY_DELAY = 1000;
        private const int TRADE_TIMEOUT = 60000;    //60s

        private readonly Logger _logger;
//        private readonly long _nonceOffset;
        private readonly WebProxy _webProxy;

        private readonly HMACSHA512 _hashMaker;
        private long _lastServerTicks;
        private readonly Regex _orderIdPattern = new Regex("\"(?<id>\\d{2,20})\":{", RegexOptions.Compiled);


        public BtceApi(Logger logger)
        {
            _logger = logger;
            var proxyHost = Configuration.GetValue("proxyHost");
            var proxyPort = Configuration.GetValue("proxyPort");
            if (null != proxyHost && null != proxyPort)
            {
                _webProxy = new WebProxy(proxyHost, int.Parse(proxyPort));
                _webProxy.Credentials = CredentialCache.DefaultCredentials;
            }

/*            var nonceOffset = Configuration.GetValue("nonce_offset");
            if (!String.IsNullOrEmpty(nonceOffset))
                _nonceOffset = long.Parse(nonceOffset);*/

            _hashMaker = new HMACSHA512(Encoding.ASCII.GetBytes(Configuration.SecretKey));
        }


        internal DateTime GetServerTime()
        {
            var data = sendGetRequest(DATA_BASE_URL + "ticker");
            var time = Helpers.DeserializeJSON<TickerResponse>(data).ticker.ServerTime;
            _lastServerTicks = (long) (time - new DateTime(2014, 8, 1)).TotalMilliseconds;
            return time;
        }

        internal MarketDepthResponse GetMarketDepth()
        {
            var data = sendGetRequest(DATA_BASE_URL + "depth");
            return Helpers.DeserializeJSON<MarketDepthResponse>(data);
        }

        internal TradeHistoryResponse GetTradeHistory(/*TODO: do I need this?   DateTime? since = null*/)
        {
            var data = sendGetRequest(DATA_BASE_URL + "trades");
            data = "{ \"trades\" : " + data + " }";
            var trades = Helpers.DeserializeJSON<TradeHistoryResponse>(data);
            return trades;
        }

        internal double GetAccountBalance()
        {
            var data = sendPostRequest("getInfo");
            return Helpers.DeserializeJSON<AccountInfoResponse>(data).@return.funds.usd;        //TODO: .ltc of course
        }



        internal Order GetOrderInfo(int orderId)
        {
            var data = sendPostRequest("OrderList", new Dictionary<string, string> {{"active", "1"}});

            //Small adjustement is needed to have smooth deserialization. See OrderList.json for original sample
            data = adjustOrderData(data);

            var orderList = Helpers.DeserializeJSON<OrderResponse>(data);
            var order = orderList.@return.FirstOrDefault(o => o.id == orderId);

            if (null != order)
                return order;

            //Couldn't find the order between active, maybe was closed => was closed
            return new Order(true);
        }



        private string sendGetRequest(string url)
        {
            WebException exc = null;
            var delay = 0;
            for (int i = 1; i <= RETRY_COUNT; i++)
            {
                var client = new WebClient2(_logger, 20000);

                if (null != _webProxy)
                    client.Proxy = _webProxy;

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

        private string sendPostRequest(string method, Dictionary<string, string> paramz = null)
        {
            var args = new Dictionary<string, string>
            {
                {"method", method},
                {"nonce", "-1"}
            };
            if (null != paramz)
            {
                foreach (var p in paramz)
                    args.Add(p.Key, p.Value);
            }

            WebException exc = null;
            var delay = RETRY_DELAY;

            for (int i = 1; i <= RETRY_COUNT; i++)
            {
                try
                {
                    args["nonce"] = (_lastServerTicks++).ToString();

                    var dataStr = buildPostData(args);
                    var postData = Encoding.ASCII.GetBytes(dataStr);

                    var webRequest = (HttpWebRequest)WebRequest.Create(new Uri(TRADE_BASE_URL));

                    webRequest.Method = "POST";
                    webRequest.Timeout = TRADE_TIMEOUT;
                    webRequest.ContentType = "application/x-www-form-urlencoded";
                    webRequest.ContentLength = postData.Length;

                    webRequest.Headers.Add("Key", Configuration.AccessKey);
                    var hash = _hashMaker.ComputeHash(postData);
                    webRequest.Headers.Add("Sign", BitConverter.ToString(hash).Replace("-", "").ToLower());

                    using (var reqStream = webRequest.GetRequestStream())
                    {
                        reqStream.Write(postData, 0, postData.Length);
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
                catch (WebException we)
                {
                    var text = String.Format("(ATTEMPT {0}/{1}) Web request failed with exception={2}; status={3}. Retry in {4}ms",
                                             i, RETRY_COUNT, we.Message, we.Status, delay);
                    _logger.AppendMessage(text, true, ConsoleColor.Yellow);
                    exc = we;

                    //DEBUG, TODO: delete
                    if (null != we.Response)
                    {
                        var resp = new StreamReader(we.Response.GetResponseStream()).ReadToEnd();
                        _logger.AppendMessage("DEBUG: " + resp, true, ConsoleColor.Magenta);
                    }

                    Thread.Sleep(delay);
                }
                delay *= 2;
            }

            throw new Exception(String.Format("Web request failed {0} times in a row with error '{1}'. Giving up.", RETRY_COUNT, exc.Message));
        }





        static string buildPostData(Dictionary<string, string> d)
        {
            var s = new StringBuilder();
            foreach (var item in d)
            {
                s.AppendFormat("{0}={1}", item.Key, item.Value);
                s.Append("&");
            }

            return s.ToString().TrimEnd(new []{'&'});
        }


        private string adjustOrderData(string data)
        {
            //First ensure there are no whitespaces
            data = data.Replace(" ", "");

            //Turn 'return' object into array
            data = data.Replace("\"return\":{", "\"return\": [");
            data = data.Replace("}}}", "}]}");

            //Change order ID numbers into values and create new variable for them
            data = _orderIdPattern.Replace(data, "{\"id\": ${id}, ");

            return data;
        }
    }
}