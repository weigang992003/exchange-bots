using System;
using System.Linq;
using BitfinexBot.Business;
using Common;
using Common.Business;


namespace BitfinexBot
{
    internal class CrazyBuyerTrap : TraderBase
    {
        private readonly BitfinexApi _requestor;

        //LTC amount to trade
        private readonly double _operativeAmount;
        private readonly double _minWallVolume;
        private readonly double _maxWallVolume;
        //Volumen of LTC necessary to accept our offer
        private double _volumeWall;
        //Minimum difference between SELL price and subsequent BUY price (so we have at least some profit)
        private const double MIN_DIFFERENCE = 0.06;
        //Tolerance of SELL price (absolute value in USD). Usefull if possible price change is minor, to avoid frequent order updates.
        private const double MIN_PRICE_DELTA = 0.02;    //2 cents per LTC

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


        public CrazyBuyerTrap(Logger logger) : base(logger)
        {
            _operativeAmount = double.Parse(Configuration.GetValue("operative_amount"));
            _minWallVolume = double.Parse(Configuration.GetValue("min_volume"));
            _maxWallVolume = double.Parse(Configuration.GetValue("max_volume"));
            log(String.Format("Bitfinex Litecoin CBT trader initialized with operative={0}; MinWall={1}; MaxWall={2}", _operativeAmount, _minWallVolume, _maxWallVolume));
            _requestor = new BitfinexApi(logger);
        }


        /// <summary>The core method to do one iteration of orders' check and updates</summary>
        protected override void Check()
        {
            var market = _requestor.GetMarketDepth();
            var tradeHistory = _requestor.GetTradeHistory();
            var serverTime = _requestor.GetServerTime();

            var coef = TradeHelpers.GetMadness(tradeHistory, serverTime);
            _volumeWall = Helpers.SuggestWallVolume(coef, _minWallVolume, _maxWallVolume);
            _intervalMs = Helpers.SuggestInterval(coef, 5000, 18000);
            log("Coef={0}, Volume={1} LTC; Interval={2} ms; ", coef, _volumeWall, _intervalMs);

            //We have active SELL order
            if (-1 != _sellOrderId)
            {
                var sellOrder = _requestor.GetOrderInfo(_sellOrderId);

                switch (sellOrder.Status)
                {
                    case OrderStatus.Open:
                    {
                        //Untouched
                        if (sellOrder.Amount.eq(_sellOrderAmount))
                        {
                            log("SELL order ID={0} untouched (amount={1} LTC, price={2} USD)", _sellOrderId, _sellOrderAmount, _sellOrderPrice);

                            double price = suggestSellPrice(market);
                            var newAmount = _operativeAmount - _buyOrderAmount;

                            //Evaluate and update if needed
                            if (newAmount > _sellOrderAmount || !_sellOrderPrice.eq(price))
                            {
                                _sellOrderId = _requestor.UpdateSellOrder(_sellOrderId, price, ref newAmount);
                                _sellOrderAmount = newAmount;
                                _sellOrderPrice = price;
                                log("Updated SELL order ID={0}; amount={1} LTC; price={2} USD", _sellOrderId, _sellOrderAmount, price);
                            }
                        }
                        else    //Partially filled
                        {
                            _executedSellPrice = sellOrder.Price;
                            _sellOrderAmount = sellOrder.Amount;
                            log("SELL order ID={0} partially filled at price={1} USD. Remaining amount={2} LTC;", ConsoleColor.Green, _sellOrderId, _executedSellPrice, sellOrder.Amount);
                            var price = suggestSellPrice(market);
                            //The same price is totally unlikely, so we don't check it here
                            var amount = sellOrder.Amount;
                            _sellOrderId = _requestor.UpdateSellOrder(_sellOrderId, price, ref amount);
                            _sellOrderAmount = amount;
                            _sellOrderPrice = price;
                            log("Updated SELL order ID={0}; amount={1} LTC; price={2} USD", _sellOrderId, _sellOrderAmount, _sellOrderPrice);
                        }
                        break;
                    }
                    case OrderStatus.Closed:
                    {
                        _executedSellPrice = sellOrder.Price;
                        log("SELL order ID={0} (amount={1} LTC) was closed at price={2} USD", ConsoleColor.Green, _sellOrderId, _sellOrderAmount, _executedSellPrice);
                        _sellOrderId = -1;
                        _sellOrderAmount = 0;
                        break;
                    }
                    default:
                        var message = String.Format("SELL order ID={0} has unexpected status '{1}'", _sellOrderId, sellOrder.Status);
                        log(message, ConsoleColor.Red);
                        throw new Exception(message);
                }
            }
            else if (_operativeAmount - _buyOrderAmount > 0.00001)    //No SELL order (and there are some LTC available). So create one
            {
                _sellOrderPrice = suggestSellPrice(market);
                var amount = _operativeAmount - _buyOrderAmount;
                _sellOrderId = _requestor.PlaceSellOrder(_sellOrderPrice, ref amount);
                _sellOrderAmount = amount;
                log("Successfully created SELL order with ID={0}; amount={1} LTC; price={2} USD", ConsoleColor.Cyan, _sellOrderId, _sellOrderAmount, _sellOrderPrice);
            }

            //Handle BUY order
            if (_operativeAmount - _sellOrderAmount > 0.00001)
            {
                //BUY order already existed
                if (-1 != _buyOrderId)
                {
                    var buyOrder = _requestor.GetOrderInfo(_buyOrderId);

                    switch (buyOrder.Status)
                    {
                        case OrderStatus.Open:
                            {
                                log("BUY order ID={0} open (amount={1} LTC, price={2} USD)", _buyOrderId, buyOrder.Amount, _buyOrderPrice);

                                double price = SuggestBuyPrice(market);

                                //Partially filled
                                if (!buyOrder.Amount.eq(_buyOrderAmount))
                                {
                                    log("BUY order ID={0} partially filled at price={1} USD. Remaining amount={2} LTC;", ConsoleColor.Green, _buyOrderId, buyOrder.price, buyOrder.Amount);
                                    _buyOrderId = _requestor.UpdateBuyOrder(_buyOrderId, price, buyOrder.Amount);
                                    _buyOrderAmount = buyOrder.Amount;
                                    _buyOrderPrice = price;
                                    log("Updated BUY order ID={0}; amount={1} LTC; price={2} USD", _buyOrderId, _buyOrderAmount, price);
                                }
                                //If there were some money released by filling a BUY order, increase this SELL order
                                else if (_operativeAmount - _sellOrderAmount > _buyOrderAmount)
                                {
                                    var newAmount = _operativeAmount - _sellOrderAmount;
                                    log("SELL dumped some LTC. Increasing BUY amount to {0} LTC", ConsoleColor.Cyan, newAmount);
                                    _buyOrderId = _requestor.UpdateBuyOrder(_buyOrderId, price, newAmount);
                                    _buyOrderAmount = newAmount;
                                    _buyOrderPrice = price;
                                    log("Updated BUY order ID={0}; amount={1} LTC; price={2} USD", _buyOrderId, _buyOrderAmount, price);
                                }
                                //Or if we simply need to change price.
                                else if (!_buyOrderPrice.eq(price))
                                {
                                    _buyOrderId = _requestor.UpdateBuyOrder(_buyOrderId, price, _buyOrderAmount);
                                    _buyOrderPrice = price;
                                    log("Updated BUY order ID={0}; amount={1} LTC; price={2} USD", _buyOrderId, _buyOrderAmount, price);
                                }
                                break;
                            }
                        case OrderStatus.Closed:
                            {
                                log("BUY order ID={0} (amount={1} LTC) was closed at price={2} USD", ConsoleColor.Green, _buyOrderId, _buyOrderAmount, buyOrder.Price);
                                _buyOrderAmount = 0;
                                _buyOrderId = -1;
                                break;
                            }
                        default:
                            var message = String.Format("BUY order ID={0} has unexpected status '{1}", _buyOrderId, buyOrder.Status);
                            log(message, ConsoleColor.Red);
                            throw new Exception(message);
                    }
                }
                else    //No BUY order, create one
                {
                    _buyOrderPrice = SuggestBuyPrice(market);
                    _buyOrderAmount = _operativeAmount - _sellOrderAmount;
                    _buyOrderId = _requestor.PlaceBuyOrder(_buyOrderPrice, _buyOrderAmount);
                    log("Successfully created BUY order with ID={0}; amount={1} LTC; price={2} USD", ConsoleColor.Cyan, _buyOrderId, _buyOrderAmount, _buyOrderPrice);
                }
            }

            var balance = _requestor.GetAccountBalance().AvailableLtc;
            log("DEBUG: Balance = {0} LTC", balance);
            log(new string('=', 84));
        }

        private double suggestSellPrice(MarketDepthResponse market)
        {
            double sum = 0;
            var highestBid = market.bids.First().Price;

            foreach (var ask in market.asks)
            {
                if (sum + _operativeAmount > _volumeWall && ask.Price - MIN_DIFFERENCE > highestBid)
                {
                    double sellPrice = Math.Round(ask.Price - 0.001, 3);

                    //The difference is too small and we'd be not the first SELL order. Leave previous price to avoid server call
                    if (-1 != _sellOrderId && sellPrice > market.asks[0].Price && Math.Abs(sellPrice - _sellOrderPrice) < MIN_PRICE_DELTA)
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
            var price = market.asks.Last().Price - 0.001;
            if (-1 != _sellOrderId && Math.Abs(price - _sellOrderPrice) < MIN_PRICE_DELTA)
                return _sellOrderPrice;
            return Math.Round(price, 3);
        }

        protected virtual double SuggestBuyPrice(IMarketDepthResponse<Order> market)     //TODO: Clear candidate for movement to parent class
        {
            const double MIN_WALL_VOLUME = 0.1;

            double sumVolume = 0.0;
            foreach (var bid in market.Bids)
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
    }
}
