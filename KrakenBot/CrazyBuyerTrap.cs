using System;
using System.Linq;
using System.Threading;
using Common;
using KrakenBot.Business;


namespace KrakenBot
{
    internal class CrazyBuyerTrap : ITrader
    {
        private bool _killSignal;
        private bool _verbose = true;
        private readonly Logger _logger;
        private readonly KrakenRequestHelper _requestor;
        private int _intervalMs;

        //BTC amount to trade
        private readonly double _operativeAmount;
        private readonly double _minWallVolume;
        private readonly double _maxWallVolume;
        //Volumen of BTC necessary to accept our offer
        private double _volumeWall;
        //Minimum difference between SELL price and subsequent BUY price (so we have at least some profit)
        private const double MIN_DIFFERENCE = 1.7;      //TODO: dynamic by current price, volume and fee
        //Tolerance of SELL price (factor). Usefull if possible price change is minor, to avoid frequent order updates.
        private const double PRICE_DELTA = 0.05;    //5%

        //Active SELL order ID
        private string _sellOrderId;
        //Active SELL order amount
        private double _sellOrderAmount;
        //Active SELL order price
        private double _sellOrderPrice;

        //Active BUY order ID
        private string _buyOrderId;
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
            _requestor = new KrakenRequestHelper(logger);
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
            var market = _requestor.GetMarketDepth().result;
            var tradeHistory = _requestor.GetTradeHistory().result;
            var serverTime = _requestor.GetServerTime();

            var coef = TradeHelpers.GetMadness(tradeHistory, serverTime);
            _volumeWall = Helpers.SuggestWallVolume(coef, _minWallVolume, _maxWallVolume);
            _intervalMs = Helpers.SuggestInterval(coef);
            log("Volume={0} BTC; Interval={1} ms; ", _volumeWall, _intervalMs);

            //We have active SELL order
            if (null != _sellOrderId)
            {
                var sellOrder = _requestor.GetOrderInfo(_sellOrderId).result.orderData;
                switch (sellOrder.Status)
                {
                    case OrderStatus.Open:
                        {
                            //Untouched
                            if (sellOrder.Amount.eq(_sellOrderAmount))
                            {
                                log("SELL order ID={0} untouched (amount={1} BTC, price={2} EUR)", _sellOrderId, _sellOrderAmount, _sellOrderPrice);

                                double price = suggestSellPrice(market);
                                var newAmount = _operativeAmount - _buyOrderAmount;

                                //Evaluate and update if needed
                                if (newAmount > _sellOrderAmount || !_sellOrderPrice.eq(price))
                                {
                                    _sellOrderId = _requestor.UpdateSellOrder(_sellOrderId, price, ref newAmount);
                                    _sellOrderAmount = newAmount;
                                    _sellOrderPrice = price;
                                    log("Updated SELL order ID={0}; amount={1} BTC; price={2} EUR", _sellOrderId, _sellOrderAmount, price);
                                }
                            }
                            else    //Partially filled
                            {
                                _executedSellPrice = sellOrder.Price;
                                _sellOrderAmount = sellOrder.Amount;
                                log("SELL order ID={0} partially filled at price={1} EUR. Remaining amount={2} BTC;", ConsoleColor.Green, _sellOrderId, _executedSellPrice, sellOrder.Amount);
                                var price = suggestSellPrice(market);
                                //The same price is totally unlikely, so we don't check it here
                                var amount = sellOrder.Amount;
                                _sellOrderId = _requestor.UpdateSellOrder(_sellOrderId, price, ref amount);
                                _sellOrderAmount = amount;
                                _sellOrderPrice = price;
                                log("Updated SELL order ID={0}; amount={1} BTC; price={2} EUR", _sellOrderId, _sellOrderAmount, _sellOrderPrice);
                            }
                            break;
                        }
                    case OrderStatus.Closed:
                        {
                            _executedSellPrice = sellOrder.Price;
                            log("SELL order ID={0} (amount={1} BTC) was closed at price={2} EUR", ConsoleColor.Green, _sellOrderId, _sellOrderAmount, _executedSellPrice);
                            _sellOrderId = null;
                            _sellOrderAmount = 0;
                            break;
                        }
                    case OrderStatus.Pending:
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
                log("Successfully created SELL order with ID={0}; amount={1} BTC; price={2} EUR", ConsoleColor.Cyan, _sellOrderId, _sellOrderAmount, _sellOrderPrice);
            }

            //Handle BUY order
            if (_operativeAmount - _sellOrderAmount > 0.00001)
            {
                //BUY order already existed
                if (null != _buyOrderId)
                {
                    var buyOrder = _requestor.GetOrderInfo(_buyOrderId).result.orderData;

                    switch (buyOrder.Status)
                    {
                        case OrderStatus.Open:
                            {
                                log("BUY order ID={0} open (amount={1} BTC, price={2} EUR)", _buyOrderId, buyOrder.Amount, _buyOrderPrice);

                                double price = suggestBuyPrice(market);

                                //Partially filled
                                if (!buyOrder.Amount.eq(_buyOrderAmount))
                                {
                                    log("BUY order ID={0} partially filled at price={1} EUR. Remaining amount={2} BTC;", ConsoleColor.Green, _buyOrderId, buyOrder.price, buyOrder.Amount);
                                    _buyOrderId = _requestor.UpdateBuyOrder(_buyOrderId, price, buyOrder.Amount);
                                    _buyOrderAmount = buyOrder.Amount;
                                    _buyOrderPrice = price;
                                    log("Updated BUY order ID={0}; amount={1} BTC; price={2} EUR", _buyOrderId, _buyOrderAmount, price);
                                }
                                //If there were some money released by filling a BUY order, increase this SELL order
                                else if (_operativeAmount - _sellOrderAmount > _buyOrderAmount)
                                {
                                    var newAmount = _operativeAmount - _sellOrderAmount;
                                    log("SELL dumped some BTC. Increasing BUY amount to {0} BTC", ConsoleColor.Cyan, newAmount);
                                    _buyOrderId = _requestor.UpdateBuyOrder(_buyOrderId, price, newAmount);
                                    _buyOrderAmount = newAmount;
                                    _buyOrderPrice = price;
                                    log("Updated BUY order ID={0}; amount={1} BTC; price={2} EUR", _buyOrderId, _buyOrderAmount, price);
                                }
                                //Or if we simply need to change price.
                                else if (!_buyOrderPrice.eq(price))
                                {
                                    _buyOrderId = _requestor.UpdateBuyOrder(_buyOrderId, price, _buyOrderAmount);
                                    _buyOrderPrice = price;
                                    log("Updated BUY order ID={0}; amount={1} BTC; price={2} EUR", _buyOrderId, _buyOrderAmount, price);
                                }
                                break;
                            }
                        case OrderStatus.Closed:
                            {
                                log("BUY order ID={0} (amount={1} BTC) was closed at price={2} EUR", ConsoleColor.Green, _buyOrderId, _buyOrderAmount, buyOrder.price);
                                _buyOrderAmount = 0;
                                _buyOrderId = null;
                                break;
                            }
                        case OrderStatus.Pending:
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
                    log("Successfully created BUY order with ID={0}; amount={1} BTC; price={2} EUR", ConsoleColor.Cyan, _buyOrderId, _buyOrderAmount, _buyOrderPrice);
                }
            }

            log(new string('=', 84));
        }

        private double suggestSellPrice(MarketDepth market)
        {
            double sum = 0;
            var minDiff = _volumeWall * PRICE_DELTA;
            var highestBid = market.XXBTZEUR.Bids.First().Price;

            foreach (var ask in market.XXBTZEUR.Asks)
            {
                if (sum + _operativeAmount > _volumeWall && ask.Price - MIN_DIFFERENCE > highestBid)
                {
                    double sellPrice = ask.Price - 0.001;

                    //The difference is too small and we'd be not the first SELL order. Leave previous price to avoid server call
                    if (null != _sellOrderId && sellPrice > market.XXBTZEUR.Asks[0].Price && Math.Abs(sellPrice - _sellOrderPrice) < minDiff)
                    {
                        log("DEBUG: SELL price {0} too similar, using previous", sellPrice);
                        return _sellOrderPrice;
                    }

                    return sellPrice;
                }
                sum += ask.Amount;

                //Don't consider volume of own order
                if (ask.Price.eq(_sellOrderPrice))
                    sum -= _sellOrderAmount;
            }

            //Market too dry, use SELL order before last, so we see it in chart
            var price = market.XXBTZEUR.Asks.Last().Price - 0.001;
            if (null != _sellOrderId && Math.Abs(price - _sellOrderPrice) < minDiff)
                return _sellOrderPrice;
            return price;
        }

        private double suggestBuyPrice(MarketDepth market)
        {
            const double MIN_WALL_VOLUME = 0.1;

            double sumVolume = 0.0;
            foreach (var bid in market.XXBTZEUR.Bids)
            {
                //Don't count self
                if (bid.Price.eq(_buyOrderPrice) && bid.Amount.eq(_buyOrderAmount))
                    continue;
                //Skip BUY orders with tiny amount
                sumVolume += bid.Amount;
                if (sumVolume < MIN_WALL_VOLUME)
                    continue;

                if (bid.Price < _executedSellPrice - MIN_DIFFERENCE)
                {
                    return bid.Price.eq(_buyOrderPrice)
                        ? _buyOrderPrice
                        : bid.Price + 0.001;
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
