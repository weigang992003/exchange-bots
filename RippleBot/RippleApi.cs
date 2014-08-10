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
    internal class RippleApi : IDisposable
    {
        private const string TRADE_BASE_URL = "wss://s-west.ripple.com:443";
        private const string CHARTS_BASE_URL = "http://api.ripplecharts.com/api/";
        private const byte RETRY_COUNT = 6;
        private const int RETRY_DELAY = 1000;
        private const string USD_ISSUER_ADDRESS = "rvYAfWj5gh67oV6fW32ZzP3Aw4Eubs59B";      //BitStamp

        private readonly string _walletAddress;

        private readonly Logger _logger;
        private bool _open;
        private bool _closedByUser;

        private readonly WebSocket _webSocket;
        private readonly WebProxy _webProxy;

        private string _lastResponse;
        private readonly Regex _offerPattern = new Regex("\"taker_(?<verb>get|pay)s\":\"(?<value>\\d{1,20})\"");


        internal RippleApi(Logger logger)
        {
            _logger = logger;

//            _webProxy = new WebProxy("wsproxybra.ext.crifnet.com", 8080);      //TODO
//            _webProxy.Credentials = CredentialCache.DefaultCredentials;

            _webSocket = new WebSocket(TRADE_BASE_URL);
            _webSocket.Opened += websocket_Opened;
            _webSocket.Error += websocket_Error;
            _webSocket.Closed += websocket_Closed;
            _webSocket.MessageReceived += websocket_MessageReceived;

            _walletAddress = Configuration.AccessKey;
        }

        internal void Init()
        {
            _webSocket.Open();

            while (!_open)
                Thread.Sleep(250);
        }

        internal double GetXrpBalance()
        {
            var command = new AccountInfoRequest { account = _walletAddress };

            var data = sendToRippleNet(Helpers.SerializeJson(command));
            var account = Helpers.DeserializeJSON<AccountInfoResponse>(data);
            return account.result.account_data.BalanceXrp;
        }

        internal Offer GetOrderInfo(int orderId)
        {
            var command = new OrderInfoRequest { id = 1, account = _walletAddress };

            var data = sendToRippleNet(Helpers.SerializeJson(command));

            var error = Helpers.DeserializeJSON<ErrorResponse>(data);
            if (!String.IsNullOrEmpty(error.error))
            {
                if (!error.IsCritical)
                {
                    _logger.AppendMessage("GetOrderInfo: non-critical error " + error.error_message, true, ConsoleColor.Yellow);
                    return null;
                }
                else
                    throw new Exception(error.error + " " + error.error_message);
            }

            var dataFix = _offerPattern.Replace(data, "'taker_${verb}s': {'currency': 'XRP', 'issuer':'ripple labs', 'value': '${value}'}".Replace("'", "\""));

            var offerList = Helpers.DeserializeJSON<OffersResponse>(dataFix);
            var order = offerList.result.offers.FirstOrDefault(o => o.seq == orderId);

            //NULL means it was already filled
            if (null == order)
                return new Offer(true);
            return order;
        }

        internal Market GetMarketDepth()
        {
            //BIDs
            var command = new MarketDepthRequest
            {
                id = 2,
                taker_pays = new Take { currency = "XRP" },
                taker_gets = new Take { currency = "USD", issuer = USD_ISSUER_ADDRESS }
            };

            var bidData = sendToRippleNet(Helpers.SerializeJson(command));

            var error = Helpers.DeserializeJSON<ErrorResponse>(bidData);
            if (!String.IsNullOrEmpty(error.error))
            {
                if (!error.IsCritical)
                {
                    _logger.AppendMessage("GetMarketDepth: non-critical error " + error.error_message, true, ConsoleColor.Yellow);
                    return null;
                }
                else
                    throw new Exception(error.error + " " + error.error_message);
            }

            var bids = Helpers.DeserializeJSON<MarketDepthBidsResponse>(bidData);

            //ASKs
            command = new MarketDepthRequest
            {
                id = 3,
                taker_pays = new Take { currency = "USD", issuer = USD_ISSUER_ADDRESS },
                taker_gets = new Take { currency = "XRP" }
            };

            var askData = sendToRippleNet(Helpers.SerializeJson(command));

            error = Helpers.DeserializeJSON<ErrorResponse>(askData);
            if (!String.IsNullOrEmpty(error.error))
            {
                if (!error.IsCritical)
                {
                    _logger.AppendMessage("GetMarketDepth: non-critical error " + error.error_message, true, ConsoleColor.Yellow);
                    return null;
                }
                else
                throw new Exception(error.error + " " + error.error_message);
            }

            var asks = Helpers.DeserializeJSON<MarketDepthAsksResponse>(askData);

            var market = new Market
            {
                Bids  = bids.result.offers,
                Asks = asks.result.offers
            };

            return market;
        }

        internal int PlaceBuyOrder(double price, double amount)
        {
            long amountXrpDrops = (long)Math.Round(amount*1000000);
            double amountUsd = price * amount;

            var command = new CreateOrderRequest
            {
                tx_json = new CrOR_TxJson
                {
                    Account = _walletAddress,
                    TakerGets = new Take
                    {
                        currency = "USD",
                        value = amountUsd.ToString("0.00000"),
                        issuer = USD_ISSUER_ADDRESS
                    },
                    TakerPays = amountXrpDrops.ToString()
                },
                secret = Configuration.SecretKey
            };

            var data = sendToRippleNet(Helpers.SerializeJson(command));
            var response = Helpers.DeserializeJSON<NewOrderResponse>(data);

            return response.result.tx_json.Sequence;
        }

        /// <summary>Update BUY order by re-creating it. Returns new order ID.</summary>
        internal int UpdateBuyOrder(int orderId, double price, double amount)
        {
            //Cancel the old order, recreate
            if (CancelOrder(orderId))
                return PlaceBuyOrder(price, amount);

            return orderId;
        }

        internal int PlaceSellOrder(double price, ref double amount)
        {
            long amountXrpDrops = (long)Math.Round(amount * 1000000);
            double amountUsd = price * amount;

            var command = new CreateSellOrderRequest
            {
                tx_json = new CSOR_TxJson
                {
                    Account = _walletAddress,
                    TakerPays = new Take
                    {
                        currency = "USD",
                        value = amountUsd.ToString("0.00000"),
                        issuer = USD_ISSUER_ADDRESS
                    },
                    TakerGets = amountXrpDrops.ToString()
                },
                secret = Configuration.SecretKey
            };

            var data = sendToRippleNet(Helpers.SerializeJson(command));

            var error = Helpers.DeserializeJSON<ErrorResponse>(data);
            if (!String.IsNullOrEmpty(error.error))
            {
                _logger.AppendMessage("Error creating SELL order. Mesage=" + error.error_message, true, ConsoleColor.Magenta);
                throw new Exception(error.error + " " + error.error_message);
            }

            var response = Helpers.DeserializeJSON<NewOrderResponse>(data);

            return response.result.tx_json.Sequence;
        }

        /// <summary>Update SELL order by re-creating it. Returns new order ID.</summary>
        internal int UpdateSellOrder(int orderId, double price, ref double amount)
        {
            //First try to cancel the old order. Recreate it then.
            if (CancelOrder(orderId))
                return PlaceSellOrder(price, ref amount);

            return orderId;
        }

        internal bool CancelOrder(int orderId)
        {
            var command = new CancelOrderRequest
            {                
                tx_json = new CaOR_TxJson
                {
                    Account = _walletAddress,
                    OfferSequence = orderId.ToString()
                },
                secret = Configuration.SecretKey
            };

            var data = sendToRippleNet(Helpers.SerializeJson(command));

            //Check for error
            var error = Helpers.DeserializeJSON<ErrorResponse>(data);
            if (!String.IsNullOrEmpty(error.error))
            {
                _logger.AppendMessage(String.Format("Error cancelling order ID={0}. Reason={1} : {2}", orderId, error.error, error.error_message),
                                      true, ConsoleColor.Magenta);
                return false;
            }

            var cancel = Helpers.DeserializeJSON<CancelOrderResponse>(data);

            //Some asserts for meaningfull reply
            if (null == cancel)
            {
                _logger.AppendMessage("cancel == NULL", true, ConsoleColor.Magenta);
                return false;
            }
            if (null == cancel.result)
            {
                _logger.AppendMessage("cancel.result == NULL", true, ConsoleColor.Magenta);
                return false;
            }

            if ("tesSUCCESS" != cancel.result.engine_result)
            {
                throw new Exception(String.Format("Unexpected response when canceling order {0}. _result={1}; _result_message={2}",
                                                  orderId, cancel.result.engine_result, cancel.result.engine_result_message));
            }

            return true;
        }

        internal void Close()
        {
            _closedByUser = true;
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
                counter = new Counter { currency = "USD", issuer = USD_ISSUER_ADDRESS },
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
            _open = false;
            _logger.AppendMessage("WebSocket connection was closed.", true, ConsoleColor.Yellow);

            if (!_closedByUser)
            {
                _logger.AppendMessage("Trying to reopen...", true, ConsoleColor.Yellow);
                Init();
            }
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
