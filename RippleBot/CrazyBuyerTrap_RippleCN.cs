using System;
using System.Linq;
using Common;
using RippleBot.Business;


namespace RippleBot
{
    internal class CrazyBuyerTrap_RippleCN : TraderBase
    {
        private readonly RippleApi _requestor;

        //BTC amount to trade
        private readonly double _operativeAmount;
        private readonly double _minWallVolume;
        private readonly double _maxWallVolume;
        //Volumen of XRP necessary to accept our offer
        private double _volumeWall;
        //Minimum difference between BUY price and subsequent SELL price (so we have at least some profit)
        private const double MIN_DIFFERENCE = 0.000015;
        //Tolerance of BUY price. Usefull if possible price change is minor, to avoid frequent order updates.
        private const double MIN_PRICE_DELTA = 0.0000015;    //0.0000015 XRP

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


        public CrazyBuyerTrap_RippleCN(Logger logger) : base(logger)
        {
            _operativeAmount = double.Parse(Configuration.GetValue("operative_amount"));
            _minWallVolume = double.Parse(Configuration.GetValue("min_volume"));
            _maxWallVolume = double.Parse(Configuration.GetValue("max_volume"));
            log("Crazy buyer trap trader for RippleCN initialized with operative={0}; MinWall={1}; MaxWall={2}", _operativeAmount, _minWallVolume, _maxWallVolume);
            _requestor = new RippleApi(logger, "rnuF96W4SZoCJmbHYBFoJZpR8eCaxNvekK"/*RippleCN.com*/, "CNY");
            _requestor.Init();
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
                        log("SELL order ID={0} untouched (amount={1} XRP, price={2} CNY)", _sellOrderId, _sellOrderAmount, _sellOrderPrice);

                        double price = suggestSellPrice(market);
                        var newAmount = _operativeAmount - _buyOrderAmount;

                        //Evaluate and update if needed
                        if (newAmount > _sellOrderAmount || !_sellOrderPrice.eq(price))
                        {
                            _sellOrderId = _requestor.UpdateSellOrder(_sellOrderId, price, ref newAmount);
                            _sellOrderAmount = newAmount;
                            _sellOrderPrice = price;
                            log("Updated SELL order ID={0}; amount={1} XRP; price={2} CNY", _sellOrderId, _sellOrderAmount, price);
                        }
                    }
                    else    //Partially filled
                    {
                        _executedSellPrice = sellOrder.Price;
                        _sellOrderAmount = sellOrder.AmountXrp;
                        log("SELL order ID={0} partially filled at price={1} CNY. Remaining amount={2} XRP;", ConsoleColor.Green, _sellOrderId, _executedSellPrice, sellOrder.AmountXrp);
                        var price = suggestSellPrice(market);
                        //The same price is totally unlikely, so we don't check it here
                        var amount = sellOrder.AmountXrp;
                        _sellOrderId = _requestor.UpdateSellOrder(_sellOrderId, price, ref amount);
                        _sellOrderAmount = amount;
                        _sellOrderPrice = price;
                        log("Updated SELL order ID={0}; amount={1} XRP; price={2} CNY", _sellOrderId, _sellOrderAmount, _sellOrderPrice);
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
                            log("Successfully created SELL order with ID={0}; amount={1} XRP; price={2} CNY", ConsoleColor.Cyan, _sellOrderId, _sellOrderAmount, _sellOrderPrice);
                    }
                    else
                    {
                        _executedSellPrice = _sellOrderPrice;
                        log("SELL order ID={0} (amount={1} XRP) was closed at price={2} CNY", ConsoleColor.Green, _sellOrderId, _sellOrderAmount, _executedSellPrice);
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
                log("Successfully created SELL order with ID={0}; amount={1} XRP; price={2} CNY", ConsoleColor.Cyan, _sellOrderId, _sellOrderAmount, _sellOrderPrice);
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
                        log("BUY order ID={0} open (amount={1} XRP, price={2} CNY)", _buyOrderId, buyOrder.AmountXrp, _buyOrderPrice);

                        double price = suggestBuyPrice(market);

                        //Partially filled
                        if (!buyOrder.AmountXrp.eq(_buyOrderAmount))
                        {
                            log("BUY order ID={0} partially filled at price={1} CNY. Remaining amount={2} XRP;", ConsoleColor.Green, _buyOrderId, buyOrder.Price, buyOrder.AmountXrp);
                            _buyOrderId = _requestor.UpdateBuyOrder(_buyOrderId, price, buyOrder.AmountXrp);
                            _buyOrderAmount = buyOrder.AmountXrp;
                            _buyOrderPrice = price;
                            log("Updated BUY order ID={0}; amount={1} XRP; price={2} CNY", _buyOrderId, _buyOrderAmount, price);
                        }
                        //If there were some money released by filling a SELL order, increase this BUY order
                        else if (_operativeAmount - _sellOrderAmount > _buyOrderAmount)
                        {
                            var newAmount = _operativeAmount - _sellOrderAmount;
                            log("SELL dumped some XRP. Increasing BUY amount to {0} XRP", ConsoleColor.Cyan, newAmount);
                            _buyOrderId = _requestor.UpdateBuyOrder(_buyOrderId, price, newAmount);
                            _buyOrderAmount = newAmount;
                            _buyOrderPrice = price;
                            log("Updated BUY order ID={0}; amount={1} XRP; price={2} CNY", _buyOrderId, _buyOrderAmount, price);
                        }
                        //Or if we simply need to change price.
                        else if (!_buyOrderPrice.eq(price))
                        {
                            _buyOrderId = _requestor.UpdateBuyOrder(_buyOrderId, price, _buyOrderAmount);
                            _buyOrderPrice = price;
                            log("Updated BUY order ID={0}; amount={1} XRP; price={2} CNY", _buyOrderId, _buyOrderAmount, price);
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
                                log("Successfully created BUY order with ID={0}; amount={1} XRP; price={2} CNY", ConsoleColor.Cyan, _buyOrderId, _buyOrderAmount, _buyOrderPrice);
                        }
                        {
                            log("BUY order ID={0} (amount={1} XRP) was closed at price={2} CNY", ConsoleColor.Green, _buyOrderId, _buyOrderAmount, _buyOrderPrice);
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
                    log("Successfully created BUY order with ID={0}; amount={1} XRP; price={2} CNY", ConsoleColor.Cyan, _buyOrderId, _buyOrderAmount, _buyOrderPrice);
                }
            }

            _xrpBalance = _requestor.GetXrpBalance();
            log("### Balance= {0} XRP", _xrpBalance);
            log(new string('=', 84));
        }


        private double suggestSellPrice(Market market)
        {
            double sum = 0;
            var highestBid = market.Bids.First().Price;

            foreach (var ask in market.Asks)
            {
                if (sum + _operativeAmount > _volumeWall && ask.Price - MIN_DIFFERENCE > highestBid)
                {
                    double sellPrice = Math.Round(ask.Price - 0.000001, 7);

                    //The difference is too small and we'd be not the first SELL order. Leave previous price to avoid server call
                    if (-1 != _sellOrderId && sellPrice > market.Asks[0].Price && Math.Abs(sellPrice - _sellOrderPrice) < MIN_PRICE_DELTA)
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
            var price = market.Asks.Last().Price - 0.000001;
            if (-1 != _sellOrderId && Math.Abs(price - _sellOrderPrice) < MIN_PRICE_DELTA)
                return _sellOrderPrice;
            return Math.Round(price, 7);
        }

        private double suggestBuyPrice(Market market)
        {
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

                if (bid.Price < _executedSellPrice - MIN_DIFFERENCE)
                {
                    return bid.Price.eq(_buyOrderPrice, 0.000000000000001)
                        ? _buyOrderPrice
                        : Math.Round(bid.Price + 0.000001, 15);
                }
            }

            //All BUY orders are too high (probably some wild race). Suggest BUY order with minimum profit and hope
            return _executedSellPrice - MIN_DIFFERENCE;
        }
    }
}
