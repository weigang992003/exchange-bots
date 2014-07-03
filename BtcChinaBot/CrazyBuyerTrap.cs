using System;
using System.Linq;
using System.Threading;
using BtcChinaBot.Business;
using Common;


namespace BtcChinaBot
{
    /// <summary>
    /// Sort of bearish strategy. The logic: keep a sell order some fixed volume above the lowest demand prize. When a crazy
    /// whale buys from us, try to buy it back cheaper.
    /// </summary>
    internal class CrazyBuyerTrap : ITrader
    {
        private bool _killSignal;
        private bool _verbose = true;
        private readonly Logger _logger;
        private readonly BtcChinaRequestHelper _requestor;
        private int _intervalMs;

        //Available BTC to trade
        private readonly double _operativeAmount;
        private readonly double _minWallVolume;
        private readonly double _maxWallVolume;
        //Volumen of BTC necessary to buy our offer
        private double _volume;
        //Minimum difference between SELL price and subsequent BUY price (so we have at least some profit)
        private const double MIN_DIFFERENCE = 0.8;
        //Tolerance of SELL price (factor). Usefull if possible price change is minor, to avoid frequent order updates.
        private const double PRICE_DELTA = 0.075;

        //Active SELL order ID
        private int _sellOrderId = -1;
        //Active SELL order amount
        private double _sellOrderAmount;
        //Active SELL order price
        private double _sellOrderPrice;

        //Active BUY order ID
        private int _buyOrderId = -1;
        //Active BUY order amount
        private double _buyOrderAmount;
        //Active BUY order price
        private double _buyOrderPrice;
        //The price at which we sold to crazy buyer
        private double _executedSellPrice = -1.0;


        public CrazyBuyerTrap(Logger logger)
        {
            _logger = logger;
            _operativeAmount = double.Parse(Configuration.GetValue("operative_amount"));
            _minWallVolume = double.Parse(Configuration.GetValue("min_volume"));
            _maxWallVolume = double.Parse(Configuration.GetValue("max_volume"));
            _logger.AppendMessage(String.Format("Crazy buyer trap trader initialized with operative={0}; MinWall={1}; MaxWall={2}", _operativeAmount, _minWallVolume, _maxWallVolume));
            _requestor = new BtcChinaRequestHelper(logger);
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
            log("Crazy Buyer Trap trader received kill signal. Good bye.");
        }

        /// <summary>The core method to do one iteration of orders' check and updates</summary>
        private void check()
        {
            var market = _requestor.GetMarketDepth().result.market_depth;
            var tradeHistory = _requestor.GetTradeHistory();

            var coef = TradeHelpers.GetMadness(tradeHistory, market.ServerTime);
            _volume = Helpers.SuggestWallVolume(coef, _minWallVolume, _maxWallVolume);
            _intervalMs = Helpers.SuggestInterval(coef);
            log("Volume={0} BTC; Interval={1} ms; ", _volume, _intervalMs);

            //We have active SELL order
            if (-1 != _sellOrderId)
            {
                var sellOrder = _requestor.GetOrderInfo(_sellOrderId).result.order;
                switch (sellOrder.status)
                {
                    case Status.OPEN:
                    {
                        //Untouched
                        if (sellOrder.amount.eq(_sellOrderAmount))
                        {
                            log("SELL order ID={0} untouched (amount={1} BTC, price={2} CNY)", _sellOrderId, _sellOrderAmount, _sellOrderPrice);

                            double price = suggestSellPrice(market);
                            var newAmount = _operativeAmount - _buyOrderAmount;

                            //Evaluate and update if needed
                            if (newAmount > _sellOrderAmount || !_sellOrderPrice.eq(price))
                            {
                                _sellOrderId = _requestor.UpdateSellOrder(_sellOrderId, price, ref newAmount);
                                _sellOrderAmount = newAmount;
                                _sellOrderPrice = price;
                                log("Updated SELL order ID={0}; amount={1} BTC; price={2} CNY", _sellOrderId, _sellOrderAmount, price);
                            }
                        }
                        else    //Partially filled
                        {
                            _executedSellPrice = sellOrder.price;
                            _sellOrderAmount = sellOrder.amount;
                            log("SELL order ID={0} partially filled at price={1} CNY. Remaining amount={2} BTC;", ConsoleColor.Green, _sellOrderId, _executedSellPrice, sellOrder.amount);
                            var price = suggestSellPrice(market);
                            //The same price is totally unlikely, so we don't check it here
                            var amount = sellOrder.amount;
                            _sellOrderId = _requestor.UpdateSellOrder(_sellOrderId, price, ref amount);
                            _sellOrderAmount = amount;
                            _sellOrderPrice = price;
                            log("Updated SELL order ID={0}; amount={1} BTC; price={2} CNY", _sellOrderId, _sellOrderAmount, _sellOrderPrice);
                        }
                        break;
                    }
                    case Status.CLOSED:
                    {
                        _executedSellPrice = sellOrder.price;
                        _sellOrderId = -1;
                        log("SELL order ID={0} (amount={1} BTC) was closed at price={2} CNY", ConsoleColor.Green, sellOrder.id, _sellOrderAmount, _executedSellPrice);
                        _sellOrderAmount = 0;
                        break;
                    }
                    case Status.PENDING:
                    {
                        log("SELL order ID={0} is in status Pending", _sellOrderId);
                        break;
                    }
                    default:
                        var message = String.Format("SELL order ID={0} has unexpected status '{1}'", _sellOrderId, sellOrder.status);
                        log(message, ConsoleColor.Red);
                        throw new Exception(message);
                }
            }
            else if (_operativeAmount - _buyOrderAmount > 0.00001)    //No SELL order (and there are some BTC available). So create one
            {
                _sellOrderPrice = suggestSellPrice(market);
                var amount = _operativeAmount - _buyOrderAmount;
                _sellOrderId = _requestor.PlaceSellOrder(_sellOrderPrice, ref amount);
                _sellOrderAmount = amount;
                log("Successfully created SELL order with ID={0}; amount={1} BTC; price={2} CNY", ConsoleColor.Cyan, _sellOrderId, _sellOrderAmount, _sellOrderPrice);
            }

            //Handle BUY order
            if (_operativeAmount - _sellOrderAmount > 0.00001)
            {
                //BUY order already existed
                if (-1 != _buyOrderId)
                {
                    var buyOrder = _requestor.GetOrderInfo(_buyOrderId).result.order;

                    switch (buyOrder.status)
                    {
                        case Status.OPEN:
                        {
                            log("BUY order ID={0} open (amount={1} BTC, price={2} CNY)", _buyOrderId, buyOrder.amount, _buyOrderPrice);

                            double price = suggestBuyPrice(market);

                            //Partially filled
                            if (!buyOrder.amount.eq(_buyOrderAmount))
                            {
                                log("BUY order ID={0} partially filled at price={1} CNY. Remaining amount={2} BTC;", ConsoleColor.Green, _buyOrderId, buyOrder.price, buyOrder.amount);
                                _buyOrderId = _requestor.UpdateBuyOrder(_buyOrderId, price, buyOrder.amount);
                                _buyOrderAmount = buyOrder.amount;
                                _buyOrderPrice = price;
                                log("Updated BUY order ID={0}; amount={1} BTC; price={2} CNY", _buyOrderId, _buyOrderAmount, price);
                            }
                            //If there were some money released by filling a BUY order, increase this SELL order
                            else if (_operativeAmount - _sellOrderAmount > _buyOrderAmount)
                            {
                                var newAmount = _operativeAmount - _sellOrderAmount;
                                log("SELL dumped some BTC. Increasing BUY amount to {0} BTC", ConsoleColor.Cyan, newAmount);
                                _buyOrderId = _requestor.UpdateBuyOrder(_buyOrderId, price, newAmount);
                                _buyOrderAmount = newAmount;
                                _buyOrderPrice = price;
                                log("Updated BUY order ID={0}; amount={1} BTC; price={2} CNY", _buyOrderId, _buyOrderAmount, price);
                            }
                            //Or if we simply need to change price.
                            else if (!_buyOrderPrice.eq(price))
                            {
                                _buyOrderId = _requestor.UpdateBuyOrder(_buyOrderId, price, _buyOrderAmount);
                                _buyOrderPrice = price;
                                log("Updated BUY order ID={0}; amount={1} BTC; price={2} CNY", _buyOrderId, _buyOrderAmount, price);
                            }
                            break;
                        }
                        case Status.CLOSED:
                        {
                            log("BUY order ID={0} (amount={1} BTC) was closed at price={2} CNY", ConsoleColor.Green, _buyOrderId, _buyOrderAmount, buyOrder.price);
                            _buyOrderAmount = 0;
                            _buyOrderId = -1;
                            break;
                        }
                        case Status.PENDING:
                        {
                            log("BUY order ID={0} is in status Pending", _buyOrderId);
                            break;
                        }
                        default:
                            var message = String.Format("BUY order ID={0} has unexpected status '{1}", _buyOrderId, buyOrder.status);
                            log(message, ConsoleColor.Red);
                            throw new Exception(message);
                    }
                }
                else    //No BUY order, create one
                {
                    _buyOrderPrice = suggestBuyPrice(market);
                    _buyOrderAmount = _operativeAmount - _sellOrderAmount;
                    _buyOrderId = _requestor.PlaceBuyOrder(_buyOrderPrice, _buyOrderAmount);
                    log("Successfully created BUY order with ID={0}; amount={1} BTC; price={2} CNY", ConsoleColor.Cyan, _buyOrderId, _buyOrderAmount, _buyOrderPrice);
                }
            }

            log(new string('=', 80));
        }

        private double suggestSellPrice(MarketDepth market)
        {
            double sum = 0;
            var minDiff = _volume * PRICE_DELTA;
            var highestBid = market.bid.First().price;

            foreach (var ask in market.ask)
            {
                if (sum + _operativeAmount > _volume && ask.price-MIN_DIFFERENCE > highestBid)
                {
                    double sellPrice = ask.price - 0.01;

                    //The difference is too small and we'd be not the first SELL order. Leave previous price to avoid server call
                    if (-1 != _sellOrderId && sellPrice > market.ask[0].price && Math.Abs(sellPrice - _sellOrderPrice) < minDiff)
                    {
                        log("DEBUG: SELL price {0} too similar, using previous", sellPrice);
                        return _sellOrderPrice;
                    }

                    return sellPrice;
                }
                sum += ask.amount;

                //Don't consider volume of own order
                if (ask.price.eq(_sellOrderPrice))
                    sum -= _sellOrderAmount;
            }

            //Market too dry, use SELL order before last, so we see it in chart
            var price = market.ask.Last().price - 0.01;
            if (-1 != _sellOrderId && Math.Abs(price - _sellOrderPrice) < minDiff)
                return _sellOrderPrice;
            return price;
        }

        private double suggestBuyPrice(MarketDepth market)
        {
            //If best SELL order fits our profit greed, don't hesitate and try to use it
/*            if (market.ask[0].price < _executedSellPrice - MIN_DIFFERENCE)
                return market.ask[0].price;     //TODO: review this rule. Maybe if we wait a bit longer, we can have bigger profit
*/
            foreach (var bid in market.bid)
            {
                if (bid.price < _executedSellPrice - MIN_DIFFERENCE)
                {
                    return bid.price.eq(_buyOrderPrice)
                        ? _buyOrderPrice
                        : bid.price + 0.01;
                }
            }

            //All BUY orders are too high (probably some wild race). Suggest BUY order with minimum profit and hope
            return _executedSellPrice - MIN_DIFFERENCE;
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
