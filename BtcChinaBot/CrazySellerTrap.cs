using System;
using System.Linq;
using System.Threading;
using BtcChinaBot.Business;


namespace BtcChinaBot
{
    /// <summary>
    /// Bullish strategy. Keeps a BUY order some fixed volume below highest bid price. When we buy from a panic
    /// whale, try to sell it higher ASAP.
    /// </summary>
    internal class CrazySellerTrap : ITrader
    {
        private bool _killSignal;
        private bool _verbose = true;
        private readonly Logger _logger;
        private readonly RequestHelper _requestor;
        private int _intervalMs;

        //BTC amount to trade
        private const double OPERATIVE_AMOUNT = 0.6;
        private readonly double _minWallVolume = 2.0;//TODO:1.0;
        private readonly double maxWallVolume = 8.0;
        //Volumen of BTC necessary to accept our offer
        private double _volumeWall;
        //Minimum difference between BUY price and subsequent SELL price (so we have at least some profit)
        private const double MIN_DIFFERENCE = 0.8;
        //Tolerance of BUY price (factor). Usefull if possible price change is minor, to avoid frequent order updates.
        private const double PRICE_DELTA = 0.06;    //6%

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


        public CrazySellerTrap(Logger logger)
        {
            _logger = logger;
            _minWallVolume = double.Parse(Configuration.GetValue("min_volume"));
            maxWallVolume = double.Parse(Configuration.GetValue("max_volume"));
            _logger.AppendMessage(String.Format("Crazy seller trap trader initialized with operative={0}; MinWall={1}; MaxWal={2}", OPERATIVE_AMOUNT, _minWallVolume, maxWallVolume));
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
            log("Crazy Seller Trap trader received kill signal. Good bye.");
        }


        /// <summary>The core method to do one iteration of orders' check and updates</summary>
        private void check()
        {
            var market = _requestor.GetMarketDepth().result.market_depth;
            var tradeHistory = _requestor.GetTradeHistory();

            var now = new DateTime(1970, 1, 1).AddSeconds(market.date).AddHours(2);
            var coef = Helpers.GetMadness(tradeHistory, now);
            _volumeWall = Helpers.SuggestWallVolume(coef, _minWallVolume, maxWallVolume);
            _intervalMs = Helpers.SuggestInterval(coef);
            log("Volume={0} BTC; Interval={1} ms;", _volumeWall, _intervalMs);

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
                            var newAmount = OPERATIVE_AMOUNT - _sellOrderAmount;

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
            else if (OPERATIVE_AMOUNT - _sellOrderAmount > 0.00001)    //No BUY order (and there are some money available). So create one
            {
                _buyOrderPrice = suggestBuyPrice(market);
                _buyOrderAmount = OPERATIVE_AMOUNT - _sellOrderAmount;
                _buyOrderId = _requestor.PlaceBuyOrder(_buyOrderPrice, _buyOrderAmount);
                log("Successfully created BUY order with ID={0}; amount={1} BTC; price={2} CNY", ConsoleColor.Cyan, _buyOrderId, _buyOrderAmount, _buyOrderPrice);
            }

            //Handle SELL order
            if (OPERATIVE_AMOUNT - _buyOrderAmount > 0.00001)
            {
                //SELL order already existed
                if (-1 != _sellOrderId)
                {
                    var sellOrder = _requestor.GetOrderInfo(_sellOrderId).result.order;

                    switch (sellOrder.status)
                    {
                        case Status.OPEN:
                        {
                            log("SELL order ID={0} open (amount={1} BTC, price={2} CNY)", _sellOrderId, sellOrder.amount, _sellOrderPrice);

                            double price = suggestSellPrice(market);

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
                            else if (OPERATIVE_AMOUNT - _buyOrderAmount > _sellOrderAmount)
                            {
                                var newAmount = OPERATIVE_AMOUNT - _buyOrderAmount;
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
                else    //No SELL order, create one
                {
                    _sellOrderPrice = suggestSellPrice(market);
                    var amount = OPERATIVE_AMOUNT - _buyOrderAmount;
                    _sellOrderId = _requestor.PlaceSellOrder(_sellOrderPrice, ref amount);
                    _sellOrderAmount = amount;
                    log("Successfully created SELL order with ID={0}; amount={1} BTC; price={2} CNY", ConsoleColor.Cyan, _sellOrderId, _sellOrderAmount, _sellOrderPrice);
                }
            }

            log(new string('=', 80));
        }


        private double suggestBuyPrice(MarketDepth market)
        {
            double sum = 0;
            var minDiff = _volumeWall * PRICE_DELTA;
            var lowestAsk = market.ask.First().price;

            foreach (var bid in market.bid)
            {
                if (sum + OPERATIVE_AMOUNT > _volumeWall && bid.price + 2.0*MIN_DIFFERENCE < lowestAsk)
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

        private double suggestSellPrice(MarketDepth market)
        {
/*            //If best BUY order fits our profit greed, don't hesitate and try to use it
            if (market.bid[0].price > _executedBuyPrice + MIN_DIFFERENCE)
                return market.bid[0].price;     //TODO: review this rule. Maybe if we wait a bit longer, we can have bigger profit
*/
            foreach (var ask in market.ask)
            {
                //Don't count self
                if (ask.price.eq(_sellOrderPrice) && ask.amount.eq(_sellOrderAmount))
                    continue;

                if (ask.price > _executedBuyPrice + MIN_DIFFERENCE)
                {
                    return ask.price.eq(_sellOrderPrice)
                        ? _sellOrderPrice
                        : ask.price - 0.01;
                }
            }

            //All SELL orders are too low (probably some terrible fall). Suggest SELL order with minimum profit and hope :-( TODO: maybe some stop-loss strategy
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
