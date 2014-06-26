using System;
using System.Linq;
using System.Threading;
using BtcChinaBot.Business;
using Common;


namespace BtcChinaBot
{
    internal class NaiveBear : ITrader
    {
        private bool _killSignal;
        private bool _verbose = true;
        private readonly Logger _logger;
        private readonly BtcChinaRequestHelper _requestor;
        private readonly MarketTrend _trend;
        private int _intervalMs;

        //Available BTC to trade
        private const double OPERATIVE_AMOUNT = 0.3;    //TODO: whatever
        //Total volume of BUY orders above the ours, so that we don't buy expensively
        private const double VOLUME_WALL = 7.0;
        //Minimum difference between SELL price and subsequent BUY price (so we have at least some profit)
        private const double MIN_DIFFERENCE = 0.5;
        //Tolerance of BUY price (factor). Usefull if possible price change is minor, to avoid frequent order updates.
        private const double PRICE_DELTA = 0.15;

        //Active BUY order ID
        private int _buyOrderId = -1;
        //Active BUY order amount
        private double _buyOrderAmount;
        //Active BUY order price
        private double _buyOrderPrice;
        //The price at which we sold
        private double _executedSellPrice = -1.0;



        public NaiveBear(Logger logger)
        {
            _logger = logger;
            _logger.AppendMessage("Naive Bear trader initialized with operative share " + OPERATIVE_AMOUNT + " BTC");
            _requestor = new BtcChinaRequestHelper(logger);
            _trend = new MarketTrend();
        }

        public void StartTrading()
        {
            do
            {
                try
                {
                    check();
                    Thread.Sleep(_intervalMs);
                }
                catch (Exception ex)
                {
                    log("ERROR: " + ex.Message + Environment.NewLine + ex.StackTrace);
                    throw;
                }
            } while (!_killSignal);
        }

        public void Kill()
        {
            _killSignal = true;
            log("Naive Bear trader received kill signal. Good bye.");
        }


        private void check()
        {
            var market = _requestor.GetMarketDepth().result.market_depth;
            var tradeHistory = _requestor.GetTradeHistory(DateTime.Now.AddMinutes(-30));

            var coef = TradeHelpers.GetMadness(tradeHistory, market.ServerTime);
            _intervalMs = Helpers.SuggestInterval(coef);
            log("Interval={0} ms; ", _intervalMs);

            //TODO: log(some description of market condition based on recent trades)
            var candles = MarketTrend.getCandleStickData(tradeHistory, new TimeSpan(0, 3, 0));
            candles = candles.TakeLast(8).ToList();
            foreach (var candle in candles)
            {
                var color = ConsoleColor.Gray;
                if (candle.ClosingPrice > candle.OpeningPrice)
                    color = ConsoleColor.Green;
                if (candle.ClosingPrice < candle.OpeningPrice)
                    color = ConsoleColor.Red;
                Console.ForegroundColor = color;
                Console.WriteLine(candle);
                Console.ResetColor();
            }



            //We have BTC to SELL
            if (OPERATIVE_AMOUNT - _buyOrderAmount > 0.00001)
            {
                var reason = _trend.ReasonToSell(tradeHistory);
                if (null != reason)
                {
                    var amount = OPERATIVE_AMOUNT - _buyOrderAmount;
                    log("SELLing {0} BTC at market price. Reason={1}", ConsoleColor.Cyan, amount, reason);
/*TODO
                    int orderId = _requestor.PlaceSellOrder(null, ref amount);
                    var orderInfo = _requestor.GetOrderInfo(orderId);
                    var orderPrice = orderInfo.result.order.price;
                    log("Market SELL (order ID={0}, amount={1} BTC) was executed at price {2}", ConsoleColor.Cyan, orderId, amount, orderPrice);*/

                    //TODO: Create or update BUY order
                }
                else log("No reason to SELL...");

                var buyBackReason = _trend.ReasonToBuyBack(tradeHistory);
                if (null != buyBackReason)
                {
                    log("DEBUG: Reason to BUY back=" + buyBackReason, ConsoleColor.Cyan);
                }
                else log("No reason to BUY...");
            }

            //TODO: before executing a market BUY, ensure that market (ASK orders) can satisfy our profit needs

            //We have active BUY order
            if (-1 != _buyOrderId)
            {
                var buyOrder = _requestor.GetOrderInfo(_buyOrderId).result.order;
                switch (buyOrder.status)
                {
                    case Status.OPEN:
                        {
                            //Untouched
                            if (buyOrder.amount.eq(_buyOrderAmount))
                            {
                                log("BUY order ID={0} untouched (amount={1} BTC, price={2} CNY)", _buyOrderId, _buyOrderAmount, _buyOrderPrice);

                                double price = suggestBuyPrice(market);

                                //Evaluate and update if needed
                                if (!_buyOrderPrice.eq(price))
                                {
                                    _buyOrderId = _requestor.UpdateBuyOrder(_buyOrderId, price, buyOrder.amount);
                                    _buyOrderPrice = price;
                                    log("Updated BUY order ID={0}; amount={1} BTC; price={2} CNY", _buyOrderId, _buyOrderAmount, price);
                                }
                            }
                            else    //Partially filled
                            {
                                _buyOrderAmount = buyOrder.amount;
                                log("BUY order ID={0} partially filled at price={1} CNY. Remaining amount={2} BTC;", ConsoleColor.Green, _buyOrderId, buyOrder.price, buyOrder.amount);
                                var price = suggestBuyPrice(market);
                                //The same price is totally unlikely, so we don't check it here
                                _buyOrderId = _requestor.UpdateBuyOrder(_buyOrderId, price, buyOrder.amount);
                                _buyOrderPrice = price;
                                log("Updated BUY order ID={0}; amount={1} BTC; price={2} CNY", _buyOrderId, _buyOrderAmount, _buyOrderPrice);
                            }
                            break;
                        }
                    case Status.CLOSED:
                        {
                            _buyOrderId = -1;
                            log("BUY order ID={0} (amount={1} BTC) was closed at price={2} CNY", ConsoleColor.Green, buyOrder.id, _buyOrderAmount, buyOrder.price);
                            _buyOrderAmount = 0;
                            break;
                        }
                    case Status.PENDING:
                        {
                            log("BUY order ID={0} is in status Pending", _buyOrderId);
                            break;
                        }
                    default:
                        var message = String.Format("BUY order ID={0} has unexpected status '{1}'", _buyOrderId, buyOrder.status);
                        log(message, ConsoleColor.Red);
                        throw new Exception(message);
                }
            }





            log(new string('=', 80));
        }


        private double suggestBuyPrice(MarketDepth market)
        {
            double sum = 0;
            var minDiff = VOLUME_WALL * PRICE_DELTA;
            var lowestAsk = market.ask.First().price;

            foreach (var bid in market.bid)
            {
                if (sum + OPERATIVE_AMOUNT > VOLUME_WALL && bid.price + MIN_DIFFERENCE < lowestAsk)
                {
                    double buyPrice = bid.price + 0.01;

                    //The difference is too small and we'd be not first in BUY orders. Leave previous price to avoid server call
                    if (-1 != _buyOrderId && buyPrice < market.bid[0].price && Math.Abs(buyPrice - _buyOrderPrice) < minDiff)
                    {
                        log("DEBUG: BUY price {0} too similar, using previous", buyPrice);
                        return _buyOrderPrice;
                    }

                    return buyPrice;
                }
                sum += bid.amount;

                //Don't consider volume of own order
                if (bid.price.eq(_buyOrderPrice))
                    sum -= _buyOrderAmount;
            }

            //Market too dry, use BUY order before last, so we see it in chart
            var price = market.bid.Last().price + 0.01;
            if (-1 != _buyOrderId && Math.Abs(price - _buyOrderPrice) < minDiff)
                return _buyOrderPrice;
            return price;
        }


        private void log(string message, ConsoleColor color, params object[] args)
        {
            if (_verbose) //TODO: select verbose and non-verbose messages
                _logger.AppendMessage(String.Format(message, args), true, color);
        }

        private void log(string message, params object[] args)
        {
            if (_verbose) //TODO: select verbose and non-verbose messages
                _logger.AppendMessage(String.Format(message, args));
        }
    }
}
