using System.IO;
using System.Net;
using System.Text;
using Common;
using RippleBot.Business;
using System;
using System.Linq;
using System.Threading;
using RippleBot.Business.DataApi;
using WebSocket4Net;
using System.Text.RegularExpressions;


namespace RippleBot
{
    //TODO: not the best name, we're calling data (charts) API too, and it's simple REST
    internal class RippleWebSocketApi : IDisposable
    {
        private const string TRADE_BASE_URL = "wss://s-west.ripple.com:443";
        private const string CHARTS_BASE_URL = "http://api.ripplecharts.com/api/";
        private const byte RETRY_COUNT = 6;
        private const int RETRY_DELAY = 1000;
        private const string USD_ISSUER_ADDRESS = "rvYAfWj5gh67oV6fW32ZzP3Aw4Eubs59B";      //BitStamp

        private readonly string _walletAddress = "rpMV1zYgR5P6YWA2JSXDPcbsbqivkooKVY";      //TODO: Config.AccessKey

        private readonly Logger _logger;
        private bool _open;

        private readonly WebSocket _webSocket;
        private readonly WebProxy _webProxy;

        private string _lastResponse;
        private readonly Regex _offerPattern = new Regex("\"taker_(?<verb>get|pay)s\":\"(?<value>\\d{1,20})\"");


        internal RippleWebSocketApi(Logger logger)
        {
            _logger = logger;

//            _webProxy = new WebProxy("wsproxybra.ext.crifnet.com", 8080);      //TODO
//            _webProxy.Credentials = CredentialCache.DefaultCredentials;

            _webSocket = new WebSocket(TRADE_BASE_URL);
            _webSocket.Opened += websocket_Opened;
            _webSocket.Error += websocket_Error;
            _webSocket.Closed += websocket_Closed;
            _webSocket.MessageReceived += websocket_MessageReceived;
        }

        internal void Init()
        {
            _webSocket.Open();

            while (!_open)
                Thread.Sleep(50);
        }

        internal Offer GetOrderInfo(int orderId)
        {
            var command = "{" + String.Format("\"command\":\"account_offers\",\"id\":1,\"account\":\"{0}\"", _walletAddress) + "}";       //TODO: serialize from class

            var data = sendToRippleNet(command);
            var dataFix = _offerPattern.Replace(data, "'taker_${verb}s': {'currency': 'XRP', 'issuer':'ripple labs', 'value': '${value}'}".Replace("'", "\""));

            var offerList = Helpers.DeserializeJSON<OffersResponse>(dataFix);
            return offerList.result.offers.FirstOrDefault(o => o.seq == orderId);
        }

        internal Market GetMarketDepth()
        {
            //BIDs
            var command = new MarketDepthRequest
            {
                id = 2,
                command = "book_offers",
                taker_pays = new Take { currency = "XRP" },
                taker_gets = new Take { currency = "USD", issuer = USD_ISSUER_ADDRESS },
                limit = 15
            };

            var bidData = sendToRippleNet(Helpers.SerializeJson(command));

            /*TODO: should not be needed here
            var error = Helpers.DeserializeJSON<ErrorResponse>(bidData);
            if (!String.IsNullOrEmpty(error.error))
                return error.error + " " + error.error_message;*/

            var bids = Helpers.DeserializeJSON<MarketDepthBidsResponse>(bidData);

            //ASKs
            command = new MarketDepthRequest
            {
                id = 3,
                command = "book_offers",
                taker_pays = new Take { currency = "USD", issuer = USD_ISSUER_ADDRESS },
                taker_gets = new Take { currency = "XRP" },
                limit = 15
            };

            var askData = sendToRippleNet(Helpers.SerializeJson(command));

            /*TODO: neither here
            error = Helpers.DeserializeJSON<ErrorResponse>(askData);
            if (!String.IsNullOrEmpty(error.error))
                return error.error + " " + error.error_message;*/

            var asks = Helpers.DeserializeJSON<MarketDepthAsksResponse>(askData);

            var market = new Market
            {
                Bids  = bids.result.offers,
                Asks = asks.result.offers
            };

            return market;
        }

        internal void Close()
        {
            if (_open)
                _webSocket.Close();
            _open = false;
        }

        public void Dispose()
        {
            Close();
        }


        /// <summary>Get trade statistics</summary>
        /// <param name="age">First candle will start on now-age</param>
        internal CandlesResponse GetTradeStatistics(TimeSpan age)
        {
            var input = new CandlesRequest
            {
                @base = new Base {currency = "XRP"},
                counter = new Counter { currency = "USD", issuer = "rvYAfWj5gh67oV6fW32ZzP3Aw4Eubs59B"/*BitStamp ripple address*/ },
                startTime = DateTime.UtcNow.Subtract(age).ToString("s"),    //"2014-07-22T10:00:00"
                endTime = DateTime.UtcNow.ToString("s"),
                timeIncrement = "minute",
                timeMultiple = 5,
                format = "json"
            };
            var jsonParams = Helpers.SerializeJson(input);

            var data = sendPostRequest("offers_exercised", jsonParams);
            return Helpers.DeserializeJSON<CandlesResponse>(data);
        }

        #region private helpers

        private string sendToRippleNet(string commandData)
        {
            _webSocket.Send(commandData);

            if (!_open)
                throw new InvalidOperationException("WebSocket not open");

            while (null == _lastResponse)
                Thread.Sleep(50);

            var ret = _lastResponse;
            _lastResponse = null;
            return ret;
        }

        private void websocket_MessageReceived(object sender, MessageReceivedEventArgs mrea)
        {
            _lastResponse = mrea.Message;
        }

        private void websocket_Opened(object sender, EventArgs e)
        {
            _logger.AppendMessage("WebSocket connection established", true, ConsoleColor.Yellow);
            _open = true;
        }

        private void websocket_Error(object sender, SuperSocket.ClientEngine.ErrorEventArgs eea)
        {
            _logger.AppendMessage("WebSocket error: " + eea.Exception.Message, true, ConsoleColor.Yellow);
        }        

        private void websocket_Closed(object sender, EventArgs e)
        {
            _logger.AppendMessage("WebSocket connection was closed", true, ConsoleColor.Yellow);
            _open = false;
        }

        private string sendPostRequest(string method, string postData)
        {
            string address = CHARTS_BASE_URL + method;
            var webRequest = (HttpWebRequest)WebRequest.Create(address);
            webRequest.ContentType = "application/json";
            webRequest.Method = "POST";

            if (null != _webProxy)
                webRequest.Proxy = _webProxy;

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
                    Thread.Sleep(RETRY_DELAY);
                }
            }

            throw new Exception(String.Format("Web request failed {0} times in a row with error '{1}'. Giving up.", RETRY_COUNT, exc.Message));
        }
        #endregion
    }
}
