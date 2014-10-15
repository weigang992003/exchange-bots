using System;
using System.Linq;
using Common;
using RippleBot.Business;


namespace RippleBot
{
    internal class CrazyBuyerTrap : TraderBase
    {
        private readonly RippleApi _requestor;

        //XRP amount to trade
        private readonly double _operativeAmount;
        private readonly double _minWallVolume;
        private readonly double _maxWallVolume;
        //Volumen of XRP necessary to accept our offer
        private double _volumeWall;
        //Minimum difference between BUY price and subsequent SELL price (so we have at least some profit). Value from config.
        private readonly double _minDifference;
        //Tolerance of BUY price. Usefull if possible price change is minor, to avoid frequent order updates. Value from config.
        private readonly double _minPriceUpdate;    //fiat/XRP
        private const double MIN_ORDER_AMOUNT = 0.5;
        private readonly string _currencyCode;

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

        private double _xrpBalance;


        public CrazyBuyerTrap(Logger logger) : base(logger)
        {
            _operativeAmount = double.Parse(Configuration.GetValue("operative_amount"));
            _minWallVolume = double.Parse(Configuration.GetValue("min_volume"));
            _maxWallVolume = double.Parse(Configuration.GetValue("max_volume"));
            var gateway = Configuration.GetValue("gateway_address");
            if (null == gateway)
                throw new Exception("Configuration key 'gateway_address' missing");
            _currencyCode = Configuration.GetValue("currency_code");
            if (null == _currencyCode)
                throw new Exception("Configuration key 'currency_code' missing");
            _minDifference = double.Parse(Configuration.GetValue("trade_spread"));
            _minPriceUpdate = double.Parse(Configuration.GetValue("min_price_update"));
            var cleanup = Configuration.GetValue("cleanup_zombies");
            _cleanup = bool.Parse(cleanup ?? false.ToString());
            log("Zombie cleanup: " + cleanup);

            _requestor = new RippleApi(logger, gateway, _currencyCode);
            _requestor.Init();
            log("CST trader started for currency {0} with operative={1}; MinWall={2}; MaxWall={3}",
                _currencyCode, _operativeAmount, _minWallVolume, _maxWallVolume);
        }

        protected override void Check()
        {
            var candles = _requestor.GetTradeStatistics(new TimeSpan(2, 0, 0));
            var market = _requestor.GetMarketDepth();

            if (null == market)
                return;

            var coef = TradeHelper.GetMadness(candles.results);
            _volumeWall = Helpers.SuggestWallVolume(coef, _minWallVolume, _maxWallVolume);
            _intervalMs = Helpers.SuggestInterval(coef, 8000, 20000);
            log("Madness={0}; Volume={1} XRP; Interval={2} ms", coef, _volumeWall, _intervalMs);

            //We have active SELL order
            if (-1 != _sellOrderId)
            {
                var sellOrder = _requestor.GetOrderInfo(_sellOrderId);

                if (null == sellOrder)
                    return;

                if (!sellOrder.Closed)
                {
                    //Untouched
                    if (sellOrder.AmountXrp.eq(_sellOrderAmount))
                    {
                        log("SELL order ID={0} untouched (amount={1} XRP, price={2} {3})", _sellOrderId, _sellOrderAmount, _sellOrderPrice, sellOrder.Currency);

                        double price = suggestSellPrice(market);
                        var newAmount = _operativeAmount - _buyOrderAmount;

                        //Evaluate and update if needed
                        if (newAmount > _sellOrderAmount || !_sellOrderPrice.eq(price))
                        {
                            _sellOrderId = _requestor.UpdateSellOrder(_sellOrderId, price, ref newAmount);
                            _sellOrderAmount = newAmount;
                            _sellOrderPrice = price;
                            log("Updated SELL order ID={0}; amount={1} XRP; price={2} {3}", _sellOrderId, _sellOrderAmount, price, sellOrder.Currency);
                        }
                    }
                    else    //Partially filled
                    {
                        _executedSellPrice = sellOrder.Price;
                        _sellOrderAmount = sellOrder.AmountXrp;
                        log("SELL order ID={0} partially filled at price={1} {2}. Remaining amount={3} XRP;",
                            ConsoleColor.Green, _sellOrderId, _executedSellPrice, sellOrder.Currency, sellOrder.AmountXrp);


                        //Check remaining amount, drop the BUY if it's very tiny
                        if (sellOrder.AmountXrp < MIN_ORDER_AMOUNT)
                        {
                            log("The remaining SELL amount is too small, canceling the order ID={0}", ConsoleColor.Cyan, _sellOrderId);
                            _requestor.CancelOrder(_sellOrderId);    //Note: no problem if the cancel fails, the breadcrumbs can live own life
                            _executedSellPrice = _sellOrderPrice;
                            _sellOrderId = -1;
                            _sellOrderAmount = 0.0;
                        }
                        else
                        {
                            var price = suggestSellPrice(market);
                            //The same price is totally unlikely, so we don't check it here
                            var amount = sellOrder.AmountXrp;
                            _sellOrderId = _requestor.UpdateSellOrder(_sellOrderId, price, ref amount);
                            _sellOrderAmount = amount;
                            _sellOrderPrice = price;
                            log("Updated SELL order ID={0}; amount={1} XRP; price={2} {3}", _sellOrderId, _sellOrderAmount, _sellOrderPrice, sellOrder.Currency);
                        }
                    }
                }
                else
                {
                    //Check if cancelled by Ripple due to "lack of funds"
                    var balance = _requestor.GetXrpBalance();
                    if (balance.eq(_xrpBalance, 0.1))
                    {
                        log("SELL order ID={0} closed but asset validation failed (balance={1} XRP). Asuming was cancelled, trying to recreate",
                            ConsoleColor.Yellow, _sellOrderId, balance);
                        _sellOrderPrice = suggestSellPrice(market);
                        _sellOrderId = _requestor.PlaceSellOrder(_sellOrderPrice, ref _sellOrderAmount);

                        if (-1 != _sellOrderId)
                        {
                            log("Successfully created SELL order with ID={0}; amount={1} XRP; price={2} {3}",
                                ConsoleColor.Cyan, _sellOrderId, _sellOrderAmount, _sellOrderPrice, _currencyCode);
                        }
                    }
                    else
                    {
                        _executedSellPrice = _sellOrderPrice;
                        log("SELL order ID={0} (amount={1} XRP) was closed at price={2} {3}",
                            ConsoleColor.Green, _sellOrderId, _sellOrderAmount, _executedSellPrice, _currencyCode);
                        _sellOrderId = -1;
                        _sellOrderAmount = 0;
                    }
                }
            }
            else if (_operativeAmount - _buyOrderAmount > 0.00001)    //No SELL order (and there are some XRP available). So create one
            {
                _sellOrderPrice = suggestSellPrice(market);
                var amount = _operativeAmount - _buyOrderAmount;
                _sellOrderId = _requestor.PlaceSellOrder(_sellOrderPrice, ref amount);
                _sellOrderAmount = amount;
                log("Successfully created SELL order with ID={0}; amount={1} XRP; price={2} {3}",
                    ConsoleColor.Cyan, _sellOrderId, _sellOrderAmount, _sellOrderPrice, _currencyCode);
            }

            //Handle BUY order
            if (_operativeAmount - _sellOrderAmount > 0.00001)
            {
                //BUY order already existed
                if (-1 != _buyOrderId)
                {
                    var buyOrder = _requestor.GetOrderInfo(_buyOrderId);

                    if (null == buyOrder)
                        return;

                    if (!buyOrder.Closed)
                    {
                        log("BUY order ID={0} open (amount={1} XRP, price={2} {3})", _buyOrderId, buyOrder.AmountXrp, _buyOrderPrice, buyOrder.Currency);

                        double price = suggestBuyPrice(market);

                        //Partially filled
                        if (!buyOrder.AmountXrp.eq(_buyOrderAmount))
                        {
                            log("BUY order ID={0} partially filled at price={1} CNY. Remaining amount={2} XRP;", ConsoleColor.Green, _buyOrderId, buyOrder.Price, buyOrder.AmountXrp);
                            _buyOrderId = _requestor.UpdateBuyOrder(_buyOrderId, price, buyOrder.AmountXrp);
                            _buyOrderAmount = buyOrder.AmountXrp;
                            _buyOrderPrice = price;
                            log("Updated BUY order ID={0}; amount={1} XRP; price={2} {3}", _buyOrderId, _buyOrderAmount, price, buyOrder.Currency);
                        }
                        //If there were some money released by filling a SELL order, increase this BUY order
                        else if (_operativeAmount - _sellOrderAmount > _buyOrderAmount)
                        {
                            var newAmount = _operativeAmount - _sellOrderAmount;
                            log("SELL dumped some XRP. Increasing BUY amount to {0} XRP", ConsoleColor.Cyan, newAmount);
                            _buyOrderId = _requestor.UpdateBuyOrder(_buyOrderId, price, newAmount);
                            _buyOrderAmount = newAmount;
                            _buyOrderPrice = price;
                            log("Updated BUY order ID={0}; amount={1} XRP; price={2} {3}", _buyOrderId, _buyOrderAmount, price, buyOrder.Currency);
                        }
                        //Or if we simply need to change price.
                        else if (!_buyOrderPrice.eq(price))
                        {
                            _buyOrderId = _requestor.UpdateBuyOrder(_buyOrderId, price, _buyOrderAmount);
                            _buyOrderPrice = price;
                            log("Updated BUY order ID={0}; amount={1} XRP; price={2} {3}", _buyOrderId, _buyOrderAmount, price, buyOrder.Currency);
                        }
                    }
                    else
                    {
                        //Check if cancelled by Ripple due to "lack of funds"
                        var balance = _requestor.GetXrpBalance();
                        if (balance.eq(_xrpBalance, 0.1))
                        {
                            log("BUY order ID={0} closed but asset validation failed (balance={1} XRP). Asuming was cancelled, trying to recreate",
                                ConsoleColor.Yellow, _buyOrderId, balance);
                            _buyOrderPrice = suggestBuyPrice(market);
                            _buyOrderId = _requestor.PlaceBuyOrder(_buyOrderPrice, _buyOrderAmount);

                            if (-1 != _buyOrderId)
                            {
                                log("Successfully created BUY order with ID={0}; amount={1} XRP; price={2} {3}",
                                    ConsoleColor.Cyan, _buyOrderId, _buyOrderAmount, _buyOrderPrice, _currencyCode);
                            }
                        }
                        {
                            log("BUY order ID={0} (amount={1} XRP) was closed at price={2} {3}",
                                ConsoleColor.Green, _buyOrderId, _buyOrderAmount, _buyOrderPrice, _currencyCode);
                            _buyOrderAmount = 0;
                            _buyOrderId = -1;
                        }
                    }
                }
                else    //No BUY order, create one
                {
                    _buyOrderPrice = suggestBuyPrice(market);
                    _buyOrderAmount = _operativeAmount - _sellOrderAmount;
                    _buyOrderId = _requestor.PlaceBuyOrder(_buyOrderPrice, _buyOrderAmount);
                    log("Successfully created BUY order with ID={0}; amount={1} XRP; price={2} {3}",
                        ConsoleColor.Cyan, _buyOrderId, _buyOrderAmount, _buyOrderPrice, _currencyCode);
                }
            }

            if (_cleanup)
                _requestor.CleanupZombies(_buyOrderId, _sellOrderId);

            _xrpBalance = _requestor.GetXrpBalance();
            log("### Balance= {0} XRP", _xrpBalance);
            log(new string('=', 84));
        }



        private double suggestSellPrice(Market market)
        {
            const int decPlaces = 14;
            double increment = Math.Pow(10.0, -1.0 * decPlaces); // 0.00000000000001;

            double sum = 0;
            var highestBid = market.Bids.First().Price;

            foreach (var ask in market.Asks)
            {
                if (sum + _operativeAmount > _volumeWall && ask.Price - _minDifference > highestBid)
                {
                    double sellPrice = Math.Round(ask.Price - increment, decPlaces);

                    //The difference is too small and we'd be not the first SELL order. Leave previous price to avoid server call
                    if (-1 != _sellOrderId && sellPrice > market.Asks[0].Price && Math.Abs(sellPrice - _sellOrderPrice) < _minPriceUpdate)
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
            var price = market.Asks.Last().Price - increment;
            if (-1 != _sellOrderId && Math.Abs(price - _sellOrderPrice) < _minPriceUpdate)
                return _sellOrderPrice;
            return Math.Round(price, decPlaces);
        }

        private double suggestBuyPrice(Market market)
        {
            const int decPlaces = 14;
            double increment = Math.Pow(10.0, -1.0 * decPlaces); // 0.00000000000001;
            const double MIN_WALL_VOLUME = 100.0;

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

                if (bid.Price < _executedSellPrice - _minDifference)
                {
                    return bid.Price.eq(_buyOrderPrice, increment)
                        ? _buyOrderPrice
                        : Math.Round(bid.Price + increment, decPlaces);
                }
            }

            //All BUY orders are too high (probably some wild race). Suggest BUY order with minimum profit and hope
            return _executedSellPrice - _minDifference;
        }
    }
}
