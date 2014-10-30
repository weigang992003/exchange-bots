using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading;
using Common;
//using RippleBot.ApiHelpers;
using RippleBot.Business;
using RippleBot.Business.DataApi;
using WebSocket4Net;


namespace RippleBot
{
    internal class RippleApi : IDisposable
    {
        private const int SOCKET_TIMEOUT = 12000;
        private const string CHARTS_BASE_URL = "http://api.ripplecharts.com/api/";
        private const byte RETRY_COUNT = 10;
        private const int RETRY_DELAY = 2000;
        private readonly string _issuerAddress;      //BitStamp, SnapSwap, RippleCN or so
        private readonly string _fiatCurreny;

        private readonly string _rippleSocketUri;
        private readonly string _walletAddress;

        private readonly Logger _logger;
        private bool _open;
        private bool _closedByUser;

        private readonly WebSocket _webSocket;
        private readonly WebProxy _webProxy;

        private string _lastResponse;
        private readonly Regex _offerPattern = new Regex("\"taker_(?<verb>get|pay)s\":\"(?<value>\\d{1,20})\"");


        internal RippleApi(Logger logger, string exchIssuerAddress, string fiatCurrencyCode)
        {
            _logger = logger;
            _issuerAddress = exchIssuerAddress;
            _fiatCurreny = fiatCurrencyCode;

            var proxyHost = Configuration.GetValue("proxyHost");
            var proxyPort = Configuration.GetValue("proxyPort");
            if (null != proxyHost && null != proxyPort)
            {
                _webProxy = new WebProxy(proxyHost, int.Parse(proxyPort));
                _webProxy.Credentials = CredentialCache.DefaultCredentials;
            }

            _webSocket = new WebSocket(_rippleSocketUri = Configuration.GetValue("server"));
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

        internal ServerStateResponse GetServerState()
        {
            var command = new ServerStateRequest();

            var data = sendToRippleNet(Helpers.SerializeJson(command));
            return Helpers.DeserializeJSON<ServerStateResponse>(data);
        }

        internal double GetXrpBalance()
        {
            var command = new AccountInfoRequest { account = _walletAddress };

            var data = sendToRippleNet(Helpers.SerializeJson(command));

            if (null == data)
                return -1.0;

            if (!checkError("GetXrpBalance", data))
                return -1.0;

            var account = Helpers.DeserializeJSON<AccountInfoResponse>(data);
            return account.result.account_data.BalanceXrp;
        }

        internal Offer GetOrderInfo(int orderId)
        {
            var offerList = getActiveOrders();
            var order = offerList.result.offers.FirstOrDefault(o => o.seq == orderId);

            //NULL means it was already filled BUG: OR CANCELLED!!! TODO: some better way of getting order status
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
                taker_gets = new Take { currency = _fiatCurreny, issuer = _issuerAddress }
            };

            var bidData = sendToRippleNet(Helpers.SerializeJson(command));
            if (null == bidData)
                return null;

            if (!checkError("GetMarketDepth", bidData))
                return null;

            var bids = Helpers.DeserializeJSON<MarketDepthBidsResponse>(bidData);
            if (null == bids.result)
            {
                _logger.AppendMessage("bidData JSON is " + Environment.NewLine + bidData, true, ConsoleColor.Magenta);
                return null;
            }

            //ASKs
            command = new MarketDepthRequest
            {
                id = 3,
                taker_pays = new Take { currency = _fiatCurreny, issuer = _issuerAddress },
                taker_gets = new Take { currency = "XRP" }
            };

            var askData = sendToRippleNet(Helpers.SerializeJson(command));
            if (null == askData)
                return null;

            if (!checkError("GetMarketDepth", askData))
                return null;

            var asks = Helpers.DeserializeJSON<MarketDepthAsksResponse>(askData);
            if (null == asks.result)
            {
                _logger.AppendMessage("askData JSON is " + Environment.NewLine + askData, true, ConsoleColor.Magenta);
                return null;
            }

            var market = new Market
            {
                Bids  = bids.result.offers,
                Asks = asks.result.offers
            };

            return market;
        }

        internal int PlaceBuyOrder(double price, double amount)
        {
            long amountXrpDrops = (long) Math.Round(amount*1000000);
            double amountUsd = price * amount;

            var command = new CreateBuyOrderRequest
            {
                tx_json = new CrOR_TxJson
                {
                    Account = _walletAddress,
                    TakerGets = new Take
                    {
                        currency = _fiatCurreny,
                        value = amountUsd.ToString("0.00000"),
                        issuer = _issuerAddress
                    },
                    TakerPays = amountXrpDrops.ToString()
                },
                secret = Configuration.SecretKey
            };

            ErrorResponse error = null;
            var delay = RETRY_DELAY;
            for (int i = 1; i <= RETRY_COUNT; i++)
            {
                var data = sendToRippleNet(Helpers.SerializeJson(command));

                if (null == data)
                    return -1;

                error = Helpers.DeserializeJSON<ErrorResponse>(data);
                if (!String.IsNullOrEmpty(error.error))
                {
                    _logger.AppendMessage("Error creating BUY order. Mesage=" + error.error_message, true, ConsoleColor.Magenta);
                    if (!error.IsCritical)
                    {
                        //The request might have been successfull even if server says there were problems
                        _logger.AppendMessage("Retry in " + delay + " ms...", true, ConsoleColor.Yellow);
                        delay *= 2;
                        Thread.Sleep(delay);
                        continue;
                    }
                    throw new Exception(error.error + " " + error.error_message);
                }

                var response = Helpers.DeserializeJSON<NewOrderResponse>(data);

                if (ResponseKind.FatalError == response.result.ResponseKind)
                {
                    var message = String.Format("Error creating BUY order. Response={0} {1}", response.result.engine_result, response.result.engine_result_message);
                    _logger.AppendMessage(message, true, ConsoleColor.Yellow);
                    throw new Exception(message);
                }
                if (ResponseKind.NonCriticalError == response.result.ResponseKind)
                {
                    _logger.AppendMessage("Non-fatal error creating BUY order. Message=" + response.result.engine_result_message, true, ConsoleColor.Yellow);
                    _logger.AppendMessage("Retry in " + delay + " ms...", true, ConsoleColor.Yellow);
                    delay *= 2;
                    Thread.Sleep(delay);
                    continue;
                }

                return response.result.tx_json.Sequence;
            }

            throw new Exception(String.Format("Socket request failed {0} times in a row with error '{1}'. Giving up.", RETRY_COUNT, error.error_message));
        }

        /// <summary>Update BUY order by re-creating it. Returns new order ID.</summary>
        internal int UpdateBuyOrder(int orderId, double price, double amount)
        {
            //Cancel the old order, recreate
            if (CancelOrder(orderId))
            {
                var id = PlaceBuyOrder(price, amount);
                if (-1 == id)
                    return orderId;
                return id;
            }

            return orderId;
        }

        internal int PlaceSellOrder(double price, ref double amount)
        {
            long amountXrpDrops = (long) Math.Round(amount*1000000);
            double amountUsd = price * amount;

            var command = new CreateSellOrderRequest
            {
                tx_json = new CSOR_TxJson
                {
                    Account = _walletAddress,
                    TakerPays = new Take
                    {
                        currency = _fiatCurreny,
                        value = amountUsd.ToString("0.00000"),
                        issuer = _issuerAddress
                    },
                    TakerGets = amountXrpDrops.ToString()
                },
                secret = Configuration.SecretKey
            };

            ErrorResponse error = null;
            var delay = RETRY_DELAY;
            for (int i = 1; i <= RETRY_COUNT; i++)
            {
                var data = sendToRippleNet(Helpers.SerializeJson(command));

                if (null == data)
                    return -1;

                error = Helpers.DeserializeJSON<ErrorResponse>(data);
                if (!String.IsNullOrEmpty(error.error))
                {
                    _logger.AppendMessage("Error creating SELL order. Mesage=" + error.error_message, true, ConsoleColor.Magenta);
                    if (!error.IsCritical)
                    {
                        _logger.AppendMessage("Retry in " + delay + " ms...", true, ConsoleColor.Yellow);
                        delay *= 2;
                        Thread.Sleep(delay);
                        continue;
                    }
                    throw new Exception(error.error + " " + error.error_message);
                }

                var response = Helpers.DeserializeJSON<NewOrderResponse>(data);

                if (ResponseKind.FatalError == response.result.ResponseKind)
                {
                    var message = String.Format("Error creating SELL order. Response={0} {1}", response.result.engine_result, response.result.engine_result_message);
                    _logger.AppendMessage(message, true, ConsoleColor.Yellow);
                    throw new Exception(message);
                }
                if (ResponseKind.NonCriticalError == response.result.ResponseKind)
                {
                    _logger.AppendMessage("Non-fatal error creating SELL order. Message=" + response.result.engine_result_message, true, ConsoleColor.Yellow);
                    _logger.AppendMessage("Retry in " + delay + " ms...", true, ConsoleColor.Yellow);
                    delay *= 2;
                    Thread.Sleep(delay);
                    continue;
                }

                return response.result.tx_json.Sequence;
            }

            throw new Exception(String.Format("Socket request failed {0} times in a row with error '{1}'. Giving up.", RETRY_COUNT, error.error_message));
        }

        /// <summary>Update SELL order by re-creating it. Returns new order ID.</summary>
        internal int UpdateSellOrder(int orderId, double price, ref double amount)
        {
            //First try to cancel the old order. Recreate it then.
            if (CancelOrder(orderId))
            {
                var id = PlaceSellOrder(price, ref amount);
                if (-1 == id) //Socket problem
                    return orderId;
                return id;
            }

            return orderId;
        }

        /// <summary>Cancel existing offer</summary>
        /// <param name="orderId">Sequence number of order to cancel</param>
        /// <param name="verify">If true, get order data after cancellation claimed OK, to verify it indeed was cancelled successfully</param>
        /// <returns>True on success, false on fail</returns>
        internal bool CancelOrder(int orderId, bool verify = true)
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
            if (null == data) //Socket problem
                return false;

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

            if (!cancel.result.ResultOK)
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
                counter = new Counter { currency = _fiatCurreny, issuer = _issuerAddress },
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

        /// <summary>
        /// Cancel all orders that are not maintained by this bot and not placed manually
        /// </summary>
        internal void CleanupZombies(int buyOrderId, int sellOrderId)
        {
            var offerList = getActiveOrders();
            if (null == offerList)
                return;

            foreach (var offer in offerList.result.offers)
            {
                if (offer.Price.ToString().Contains("12345"))       //TODO: This is really stupid!! Find some way how to safely flag manual/bot orders
                    _logger.AppendMessage("Cleanup: Order ID=" + offer.seq + " not a zombie, possibly manual", true, ConsoleColor.Cyan);
                else if (-1 != buyOrderId && buyOrderId == offer.seq)
                    _logger.AppendMessage("Cleanup: Order ID=" + offer.seq + " not a zombie, our BUY order", false);
                else if (-1 != sellOrderId && sellOrderId == offer.seq)
                    _logger.AppendMessage("Cleanup: Order ID=" + offer.seq + " not a zombie, our SELL order", false);
                else
                {
                    _logger.AppendMessage(String.Format("Identified {0} zombie order with ID={1} ({2} XRP for {3} {4}). Trying to cancel...",
                                                        offer.Type, offer.seq, offer.AmountXrp, offer.Price, offer.Currency), true, ConsoleColor.Yellow);
                    //Found offer abandoned by this bot, try to cancel it
                    if (CancelOrder(offer.seq))
                        _logger.AppendMessage("... success", true, ConsoleColor.Cyan);
                    else
                        _logger.AppendMessage("... failed. Maybe next time", true, ConsoleColor.Yellow);
                }
            }
        }


        #region private helpers

        private OffersResponse getActiveOrders()
        {
            var command = new OrderInfoRequest { id = 1, account = _walletAddress };

            var data = sendToRippleNet(Helpers.SerializeJson(command));
            if (null == data)
                return null;

            if (!checkError("GetOrderInfo", data))
                return null;

            var dataFix = _offerPattern.Replace(data, "'taker_${verb}s': {'currency': 'XRP', 'issuer':'ripple labs', 'value': '${value}'}".Replace("'", "\""));

            return Helpers.DeserializeJSON<OffersResponse>(dataFix);
        }

        private string sendToRippleNet(string commandData)
        {
            _webSocket.Send(commandData);

            if (!_open)
                throw new InvalidOperationException("WebSocket not open");

            var duration = 0;
            while (null == _lastResponse)
            {
                const int wait = 50;
                Thread.Sleep(wait);
                duration += wait;
                if (duration > SOCKET_TIMEOUT)
                {
                    _logger.AppendMessage("Didn't recieve response from socket in " + duration + " ms. Returning NULL.");
                    return _lastResponse = null;
                }
            }

            var ret = _lastResponse;
            _logger.LastResponse = ret;
            _lastResponse = null;
            return ret;
        }

        private bool checkError(string action, string data)
        {
            var error = Helpers.DeserializeJSON<ErrorResponse>(data);
            if (!String.IsNullOrEmpty(error.error))
            {
                if (!error.IsCritical)
                {
                    _logger.AppendMessage(action + ": non-critical error " + error.error_message, true, ConsoleColor.Yellow);
                    return false;
                }
                throw new Exception(error.error + " " + error.error_message);
            }

            return true;
        }

        private void websocket_MessageReceived(object sender, MessageReceivedEventArgs mrea)
        {
            _lastResponse = mrea.Message;
        }

        private void websocket_Opened(object sender, EventArgs e)
        {
            _logger.AppendMessage("Established WebSocket connection to " + _rippleSocketUri, true, ConsoleColor.Yellow);
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

            WebException exc = null;
            var delay = RETRY_DELAY;
            for (int i = 1; i <= RETRY_COUNT; i++)
            {
                var webRequest = (HttpWebRequest)WebRequest.Create(address);
                webRequest.ContentType = "application/json";
                webRequest.Method = "POST";

                if (null != _webProxy)
                    webRequest.Proxy = _webProxy;

                using (var writer = new StreamWriter(webRequest.GetRequestStream()))
                {
                    writer.Write(postData);
                }

                try
                {
                    using (WebResponse webResponse = webRequest.GetResponse())
                    {
                        using (Stream stream = webResponse.GetResponseStream())
                        {
                            using (StreamReader reader = new StreamReader(stream))
                            {
                                var text = reader.ReadToEnd();
                                _logger.LastResponse = text;
                                return text;
                            }
                        }
                    }
                }
                catch (WebException we)
                {
                    var text = String.Format("(ATTEMPT {0}/{1}) Web request failed with exception={2}; status={3}. Retry in {4} ms",
                                             i, RETRY_COUNT, we.Message, we.Status, delay);
                    _logger.AppendMessage(text, true, ConsoleColor.Yellow);

                    exc = we;
                    Thread.Sleep(delay);
                }
                delay *= 2;
            }

            throw new Exception(String.Format("Web request failed {0} times in a row with error '{1}'. Giving up.", RETRY_COUNT, exc.Message));
        }
        #endregion
    }
}
