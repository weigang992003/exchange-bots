using System;
using System.Linq;
using System.Threading;
using BtcChinaBot.Business;


namespace BtcChinaBot
{
    /// <summary>
    /// When the market is peacefull (low volumes, scarce trades) and spreads large enough, does tiny
    /// trades to make some minor profits
    /// </summary>
    internal class NightlyFrogBoiling : ITrader
    {
        private bool _killSignal;
        private bool _verbose = true;
        private readonly Logger _logger;
        private readonly RequestHelper _requestor;
        private int _intervalMs;

        //BTC amount to trade
        private readonly double _operativeAmount;
        //Minimum difference between BUY price and subsequent SELL price (so we have at least some profit)
        private const double MIN_DIFFERENCE = 0.2;
        //Tolerance of BUY price (factor). Usefull if possible price change is minor, to avoid frequent order updates.
        private const double PRICE_DELTA = 0.05;    //5%
        private const double MIN_SPREAD = 1.6;

        //Active BUY order ID
        private int _buyOrderId = -1;
        //Active BUY order amount
        private double _buyOrderAmount;
        //Active BUY order price
        private double _buyOrderPrice;

        //Active SELL order ID
        private int _sellOrderId = -1;
        //Active SELL order amount
        private double _sellOrderAmount;
        //Active SELL order price
        private double _sellOrderPrice;
        //The price at which we bought from crazy buyer
        private double _executedBuyPrice = -1.0;


        public NightlyFrogBoiling(Logger logger)
        {
            _logger = logger;
            _operativeAmount = double.Parse(Configuration.GetValue("operative_amount"));
            _logger.AppendMessage("Nightly Frog Boiling trader initialized with operative amount " + _operativeAmount + " BTC");
            _requestor = new RequestHelper(logger);
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
            log("Nightly Frog Boling trader received kill signal. Good bye.");
        }

        private void check()
        {
            var market = _requestor.GetMarketDepth().result.market_depth;
            var tradeHistory = _requestor.GetTradeHistory();

            var now = new DateTime(1970, 1, 1).AddSeconds(market.date).AddHours(2);
            var coef = Helpers.GetMadness(tradeHistory, now);
            _intervalMs = Helpers.SuggestInterval(coef);

            var spread = getSpread(market);
            log("Spread={0:0.00} BTC; Madness={1:0.00}; Interval={2} ms;", spread, coef, _intervalMs);

            //Handle BUY order
            if (spread > MIN_SPREAD && coef < 0.15)     //TODO: tune up coef threshold, create const ACTIVITY_THRESHOLD
            {
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

                                var price = suggestBuyPrice(market, spread);
                                var newAmount = _operativeAmount - _sellOrderAmount;

                                //Evaluate and update if needed
                                if (newAmount > _buyOrderAmount || !_buyOrderPrice.eq(price))
                                {
                                    _buyOrderAmount = newAmount;
                                    _buyOrderId = _requestor.UpdateBuyOrder(_buyOrderId, price, newAmount);
                                    _buyOrderPrice = price;
                                    log("Updated BUY order ID={0}; amount={1} BTC; price={2} CNY", _buyOrderId, _buyOrderAmount, price);
                                }
                            }
                            else    //Partially filled
                            {
                                _executedBuyPrice = buyOrder.price;
                                _buyOrderAmount = buyOrder.amount;
                                log("BUY order ID={0} partially filled at price={1} CNY. Remaining amount={2} BTC;", ConsoleColor.Green, _buyOrderId, _executedBuyPrice, buyOrder.amount);
                                var price = suggestBuyPrice(market, spread);
                                //The same price is totally unlikely, so we don't check it here
                                _buyOrderId = _requestor.UpdateBuyOrder(_buyOrderId, price, buyOrder.amount);
                                _buyOrderPrice = price;
                                log("Updated BUY order ID={0}; amount={1} BTC; price={2} CNY", _buyOrderId, _buyOrderAmount, _buyOrderPrice);
                            }
                            break;
                        }
                        case Status.CLOSED:
                        {
                            _executedBuyPrice = buyOrder.price;
                            _buyOrderId = -1;
                            log("BUY order ID={0} (amount={1} BTC) was closed at price={2} CNY", ConsoleColor.Green, buyOrder.id, _buyOrderAmount, _executedBuyPrice);
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
                else if (_operativeAmount - _sellOrderAmount > 0.00001)    //No BUY order (and there are some money available). So create one
                {
                    _buyOrderPrice = suggestBuyPrice(market, spread);
                    _buyOrderAmount = _operativeAmount - _sellOrderAmount;
                    _buyOrderId = _requestor.PlaceBuyOrder(_buyOrderPrice, _buyOrderAmount);
                    log("Successfully created BUY order with ID={0}; amount={1} BTC; price={2} CNY", ConsoleColor.Cyan, _buyOrderId, _buyOrderAmount, _buyOrderPrice);
                }
            }
            else if (-1 != _buyOrderId)
            {
                //Check if it was (partially) filled meanwhile
                var buyOrder = _requestor.GetOrderInfo(_buyOrderId).result.order;
                if (buyOrder.status != Status.OPEN || !buyOrder.amount.eq(_buyOrderAmount))
                {
                    log("BUY order ID={0} changed before cancelled. Status={1}; amount={2};", ConsoleColor.Yellow, _buyOrderId, buyOrder.status, buyOrder.amount);
                    _buyOrderAmount = buyOrder.amount;
                    _executedBuyPrice = _buyOrderPrice;
                }

                log("Spread too low or market too wild. Cancelling BUY order ID={0}", ConsoleColor.Cyan, _buyOrderId);
                _requestor.CancelOrder(_buyOrderId);
                _buyOrderId = -1;
            }

            //Handle SELL order
            if (_operativeAmount - _buyOrderAmount > 0.00001 && _executedBuyPrice > -1.0)
            {
                //SELL order already existed
                if (-1 != _sellOrderId)
                {
                    var sellOrder = _requestor.GetOrderInfo(_sellOrderId).result.order;

                    switch (sellOrder.status)
                    {
                        case Status.OPEN:
                            {
                                log("SELL order ID={0} open (amount={1} BTC, price={2} CNY)", _sellOrderId,
                                    sellOrder.amount,
                                    _sellOrderPrice);

                                double price = suggestSellPrice(market, spread);

                                //Partially filled
                                if (!sellOrder.amount.eq(_sellOrderAmount))
                                {
                                    log("SELL order ID={0} partially filled at price={1} CNY. Remaining amount={2} BTC;", ConsoleColor.Green, _sellOrderId, sellOrder.price, sellOrder.amount);
                                    var amount = sellOrder.amount;
                                    _sellOrderId = _requestor.UpdateSellOrder(_sellOrderId, price, ref amount);
                                    _sellOrderAmount = amount;
                                    _sellOrderPrice = price;
                                    log("Updated SELL order ID={0}; amount={1} BTC; price={2} CNY", _sellOrderId, _sellOrderAmount, price);
                                }
                                //If there were some money released by filling a BUY order, increase this SELL order
                                else if (_operativeAmount - _buyOrderAmount > _sellOrderAmount)
                                {
                                    var newAmount = _operativeAmount - _buyOrderAmount;
                                    _sellOrderId = _requestor.UpdateSellOrder(_sellOrderId, price, ref newAmount);
                                    _sellOrderAmount = newAmount;
                                    _sellOrderPrice = price;
                                    log("Updated SELL order ID={0}; amount={1} BTC; price={2} CNY", _sellOrderId, _sellOrderAmount, price);
                                }
                                //Or if we simply need to change price.
                                else if (!_sellOrderPrice.eq(price))
                                {
                                    _sellOrderId = _requestor.UpdateSellOrder(_sellOrderId, price, ref _sellOrderAmount);
                                    _sellOrderPrice = price;
                                    log("Updated SELL order ID={0}; amount={1} BTC; price={2} CNY", _sellOrderId, _sellOrderAmount, price);
                                }
                                break;
                            }
                        case Status.CLOSED:
                            {
                                log("SELL order ID={0} (amount={1} BTC) was closed at price={2} CNY", ConsoleColor.Green, _sellOrderId, _sellOrderAmount, sellOrder.price);
                                _sellOrderAmount = 0;
                                _sellOrderId = -1;
                                break;
                            }
                        case Status.PENDING:
                            {
                                log("SELL order ID={0} is in status Pending", null, _sellOrderId);
                                break;
                            }
                        default:
                            var message = String.Format("SELL order ID={0} has unexpected status '{1}", _sellOrderId, sellOrder.status);
                            log(message, ConsoleColor.Red);
                            throw new Exception(message);
                    }
                }
                else    //No SELL order and we have bought some BTC. Create new.
                {
                    var availableBtc = double.Parse(_requestor.GetAccountInfo().result.balance.btc.amount);
                    if (availableBtc > 0.0)
                    {
                        _sellOrderPrice = suggestSellPrice(market, spread);
                        var amount = _operativeAmount - _buyOrderAmount;
                        if (availableBtc > amount)
                        {
                            log("Available BTC={0}; OPERATIVE-_buyOrderAmount={1}; Using the first for new SELL order.", ConsoleColor.Yellow, availableBtc, amount);
                            amount = availableBtc;
                        }

                        _sellOrderId = _requestor.PlaceSellOrder(_sellOrderPrice, ref amount);
                        _sellOrderAmount = amount;
                        log("Successfully created SELL order with ID={0}; amount={1} BTC; price={2} CNY",
                            ConsoleColor.Cyan, _sellOrderId, _sellOrderAmount, _sellOrderPrice);
                    }
                }
            }

            log(new string('=', 80));
        }


        private double getSpread(MarketDepth market)
        {
            //Find highest BUY order that's not ours
            var firstBid = market.bid.First();
            double highestBidPrice;
            if (-1 == _buyOrderId)
                highestBidPrice = firstBid.price;
            else
            {
                if (_buyOrderPrice.eq(firstBid.price))
                {
                    //It's only us
                    if (firstBid.amount.eq(_buyOrderAmount))
                        highestBidPrice = market.bid[1].price;
                    else highestBidPrice = firstBid.price;
                }
                else highestBidPrice = firstBid.price;
            }

            //Find lowest SELL order not from us
            var firstAsk = market.ask.First();
            double lowestAskPrice;
            if (-1 == _sellOrderId)
                lowestAskPrice = firstAsk.price;
            else
            {
                if (_sellOrderPrice.eq(firstAsk.price))
                {
                    //It's only us
                    if (_sellOrderAmount.eq(firstAsk.amount))
                        lowestAskPrice = market.ask[1].price;
                    else lowestAskPrice = firstAsk.price;
                }
                else lowestAskPrice = firstAsk.price;
            }

            log("DEBUG: lowestAsk={0}; highestBid={1};", lowestAskPrice, highestBidPrice);
            return lowestAskPrice - highestBidPrice;
        }

        private double suggestBuyPrice(MarketDepth market, double spread)
        {
            var buyPrice = Math.Round(market.bid.First().price + spread / 3.0, 2);
            if (-1 == _buyOrderId)
                return buyPrice;

            var minDiff = spread * PRICE_DELTA;
            if (Math.Abs(buyPrice - _buyOrderPrice) < minDiff)
            {
                log("DEBUG: BUY price {0} too similar, using previous", buyPrice);
                return _buyOrderPrice;
            }
            return buyPrice;
        }

        private double suggestSellPrice(MarketDepth market, double spread)
        {
            var sellPrice = Math.Round(market.ask.First().price - spread/3.0, 2);
            if (sellPrice > _executedBuyPrice + MIN_DIFFERENCE && -1 == _sellOrderId)
                return sellPrice;

            if (-1 != _sellOrderId)
            {
                var minDiff = spread * PRICE_DELTA;
                if (Math.Abs(sellPrice - _buyOrderPrice) < minDiff)
                {
                    log("DEBUG: SELL price {0} too similar, using previous", sellPrice);
                    return _buyOrderPrice;
                }
            }

            return _executedBuyPrice + MIN_DIFFERENCE;
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
