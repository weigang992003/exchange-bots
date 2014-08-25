using System;
using System.Linq;
using System.Threading;
using Common;
using HuobiBot.Business;


namespace HuobiBot
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
        private readonly HuobiApi _requestor;
        private int _intervalMs;

        //BTC amount to trade
        private readonly double _operativeAmount;
        private readonly double _minWallVolume;
        private readonly double _maxWallVolume;
        //Volumen of BTC necessary to accept our offer
        private double _volumeWall;
        //Minimum difference between BUY price and subsequent SELL price (so we have at least some profit)
        private const double MIN_DIFFERENCE = 1.0;
        //Tolerance of BUY price (factor). Usefull if possible price change is minor, to avoid frequent order updates.
        private const double MIN_PRICE_DELTA = 0.8;    //0.8 CNY

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
            _operativeAmount = double.Parse(Configuration.GetValue("operative_amount"));
            _minWallVolume = double.Parse(Configuration.GetValue("min_volume"));
            _maxWallVolume = double.Parse(Configuration.GetValue("max_volume"));
            _logger.AppendMessage(String.Format("Crazy seller trap trader initialized with operative={0}; MinWall={1}; MaxWall={2}", _operativeAmount, _minWallVolume, _maxWallVolume));
            _requestor = new HuobiApi(logger);
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
            var market = _requestor.GetMarketDepth();
            if (null == market || !market.IsValid)
            {
                log("market==NULL or doesn't bear enough data; jump", ConsoleColor.Yellow);
                return;
            }
            var tradeStats = _requestor.GetTradeStatistics();
            var serverTime = _requestor.GetServerTime();

            var coef = TradeHelper.GetMadness(tradeStats, serverTime);
            _volumeWall = Helpers.SuggestWallVolume(coef, _minWallVolume, _maxWallVolume);
            _intervalMs = Helpers.SuggestInterval(coef);
            log("Madness={0}; Volume={1} BTC; Interval={2} ms;", coef, _volumeWall, _intervalMs);

            //We have active BUY order
            if (-1 != _buyOrderId)
            {
                var buyOrder = _requestor.GetOrderInfo(_buyOrderId);
                switch (buyOrder.Status)
                {
                    case OrderStatus.Unfilled:
                    case OrderStatus.PartiallyFilled:
                    {
                        //Untouched
                        if (buyOrder.Amount.eq(_buyOrderAmount))
                        {
                            log("BUY order ID={0} untouched (amount={1} BTC, price={2} CNY)", _buyOrderId, _buyOrderAmount, _buyOrderPrice);

                            double price = suggestBuyPrice(market);
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
                            _executedBuyPrice = buyOrder.Price;
                            _buyOrderAmount = buyOrder.Amount;
                            log("BUY order ID={0} partially filled at price={1} CNY. Remaining amount={2} BTC;", ConsoleColor.Green, _buyOrderId, _executedBuyPrice, buyOrder.Amount);
                            var price = suggestBuyPrice(market);
                            //The same price is totally unlikely, so we don't check it here
                            _buyOrderId = _requestor.UpdateBuyOrder(_buyOrderId, price, buyOrder.Amount);
                            _buyOrderPrice = price;
                            log("Updated BUY order ID={0}; amount={1} BTC; price={2} CNY", _buyOrderId, _buyOrderAmount, _buyOrderPrice);
                        }
                        break;
                    }
                    case OrderStatus.Finished:
                    {
                        _executedBuyPrice = buyOrder.Price;
                        _buyOrderId = -1;
                        log("BUY order ID={0} (amount={1} BTC) was closed at price={2} CNY", ConsoleColor.Green, buyOrder.id, _buyOrderAmount, _executedBuyPrice);
                        _buyOrderAmount = 0;
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
                _buyOrderPrice = suggestBuyPrice(market);
                _buyOrderAmount = _operativeAmount - _sellOrderAmount;
                _buyOrderId = _requestor.PlaceBuyOrder(_buyOrderPrice, _buyOrderAmount);
                log("Successfully created BUY order with ID={0}; amount={1} BTC; price={2} CNY", ConsoleColor.Cyan, _buyOrderId, _buyOrderAmount, _buyOrderPrice);
            }

            //Handle SELL order
            if (_operativeAmount - _buyOrderAmount > 0.00001)
            {
                //SELL order already existed
                if (-1 != _sellOrderId)
                {
                    var sellOrder = _requestor.GetOrderInfo(_sellOrderId);

                    switch (sellOrder.Status)
                    {
                        case OrderStatus.Unfilled:
                        case OrderStatus.PartiallyFilled:
                        {
                            log("SELL order ID={0} open (amount={1} BTC, price={2} CNY)", _sellOrderId, sellOrder.Amount, _sellOrderPrice);

                            double price = suggestSellPrice(market);

                            //Partially filled
                            if (!sellOrder.Amount.eq(_sellOrderAmount))
                            {
                                log("SELL order ID={0} partially filled at price={1} CNY. Remaining amount={2} BTC;", ConsoleColor.Green, _sellOrderId, sellOrder.Price, sellOrder.Amount);
                                var amount = sellOrder.Amount;
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
                        case OrderStatus.Finished:
                        {
                            log("SELL order ID={0} (amount={1} BTC) was closed at price={2} CNY", ConsoleColor.Green, _sellOrderId, _sellOrderAmount, sellOrder.Price);
                            _sellOrderAmount = 0;
                            _sellOrderId = -1;
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
                    var amount = _operativeAmount - _buyOrderAmount;
                    _sellOrderId = _requestor.PlaceSellOrder(_sellOrderPrice, ref amount);
                    _sellOrderAmount = amount;
                    log("Successfully created SELL order with ID={0}; amount={1} BTC; price={2} CNY", ConsoleColor.Cyan, _sellOrderId, _sellOrderAmount, _sellOrderPrice);
                }
            }

            log(new string('=', 80));
        }


        private double suggestBuyPrice(MarketDepthResponse market)
        {
            double sum = 0;
            var lowestAsk = market.Asks.First().Price;

            foreach (var bid in market.Bids)
            {
                if (sum + _operativeAmount > _volumeWall && bid.Price + 2.0*MIN_DIFFERENCE < lowestAsk)
                {
                    double buyPrice = bid.Price + 0.01;

                    //The difference is too small and we'd be not first in BUY orders. Leave previous price to avoid server call
                    if (-1 != _buyOrderId && buyPrice < market.Bids[0].Price && Math.Abs(buyPrice - _buyOrderPrice) < MIN_PRICE_DELTA)
                    {
                        log("DEBUG: BUY price {0} too similar, using previous", buyPrice);
                        return _buyOrderPrice;
                    }

                    return buyPrice;
                }
                sum += bid.Amount;

                //Don't consider volume of own order
                if (bid.Price.eq(_buyOrderPrice))
                    sum -= _buyOrderAmount;
            }

            //Market too dry, use BUY order before last, so we see it in chart
            var price = market.Bids.Last().Price + 0.01;
            if (-1 != _buyOrderId && Math.Abs(price - _buyOrderPrice) < MIN_PRICE_DELTA)
                return _buyOrderPrice;
            return price;
        }

        private double suggestSellPrice(MarketDepthResponse market)
        {
            const double MIN_WALL_VOLUME = 0.1;

            double sumVolume = 0.0;
            foreach (var ask in market.Asks)
            {
                //Don't count self
                if (ask.Price.eq(_sellOrderPrice) && ask.Amount.eq(_sellOrderAmount))
                    continue;
                //Skip SELL orders with tiny amount
                sumVolume += ask.Amount;
                if (sumVolume < MIN_WALL_VOLUME)
                    continue;

                if (ask.Price > _executedBuyPrice + MIN_DIFFERENCE)
                {
                    return ask.Price.eq(_sellOrderPrice)
                        ? _sellOrderPrice
                        : ask.Price - 0.01;
                }
            }

            //All SELL orders are too low (probably some terrible fall). Suggest SELL order with minimum profit and hope :-( TODO: maybe some stop-loss strategy
            return _executedBuyPrice + MIN_DIFFERENCE;
        }

        private void log(string message, ConsoleColor color, params object[] args)
        {
            if (_verbose) //TODO: select verbose and non-verbose messages
            {
                try
                {
                    _logger.AppendMessage(String.Format(message, args), true, color);
                }
                catch (FormatException)
                {
                    var argz = null == args || 0 == args.Length
                        ? "NULL"
                        : String.Join(",", args);
                    _logger.AppendMessage("Couldn't log message '" + message + "',  args=" + argz, true, ConsoleColor.Red);
                }
            }
        }

        private void log(string message, params object[] args)
        {
            if (_verbose) //TODO: select verbose and non-verbose messages
            {
                try
                {
                    _logger.AppendMessage(String.Format(message, args));
                }
                catch (FormatException)
                {
                    var argz = null == args || 0 == args.Length
                        ? "NULL"
                        : String.Join(",", args);
                    _logger.AppendMessage("Couldn't log message '" + message + "',  args=" + argz, true, ConsoleColor.Red);
                }
            }
        }
    }
}
