using System;
using System.Linq;
using Common;
using RippleBot.Business;


namespace RippleBot
{
    /// <summary>
    /// General CST strategy for Ripple network. Particular ripple account, gateway and currency pair are
    /// parameters given by configuration.
    /// </summary>
    public class CrazySellerTrap : TraderBase
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

        private double _xrpBalance;


        public CrazySellerTrap(Logger logger) : base(logger)
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

            //We have active BUY order
            if (-1 != _buyOrderId)
            {
                var buyOrder = _requestor.GetOrderInfo(_buyOrderId);

                if (null == buyOrder)
                    return;

                //The order is still open
                if (!buyOrder.Closed)
                {
                    //Untouched
                    if (buyOrder.AmountXrp.eq(_buyOrderAmount))
                    {
                        log("BUY order ID={0} untouched (amount={1} XRP, price={2} {3})", _buyOrderId, _buyOrderAmount, _buyOrderPrice, buyOrder.Currency);

                        double price = suggestBuyPrice(market);
                        var newAmount = _operativeAmount - _sellOrderAmount;

                        //Evaluate and update if needed
                        if (newAmount > _buyOrderAmount || !_buyOrderPrice.eq(price))
                        {
                            _buyOrderAmount = newAmount;
                            _buyOrderId = _requestor.UpdateBuyOrder(_buyOrderId, price, newAmount);
                            _buyOrderPrice = price;
                            log("Updated BUY order ID={0}; amount={1} XRP; price={2} {3}", _buyOrderId, _buyOrderAmount, price, buyOrder.Currency);
                        }
                    }
                    else    //Partially filled
                    {
                        _executedBuyPrice = buyOrder.Price;
                        _buyOrderAmount = buyOrder.AmountXrp;
                        log("BUY order ID={0} partially filled at price={1} {2}. Remaining amount={3} XRP;",
                            ConsoleColor.Green, _buyOrderId, _executedBuyPrice, buyOrder.Currency, buyOrder.AmountXrp);

                        //Check remaining amount, drop the BUY if it's very tiny
                        if (buyOrder.AmountXrp < MIN_ORDER_AMOUNT)
                        {
                            log("The remaining BUY amount is too small, canceling the order ID={0}", ConsoleColor.Cyan, _buyOrderId);
                            _requestor.CancelOrder(_buyOrderId);    //Note: no problem if the cancel fails, the breadcrumbs can live own life
                            _executedBuyPrice = _buyOrderPrice;
                            _buyOrderId = -1;
                            _buyOrderAmount = 0.0;
                        }
                        else
                        {
                            var price = suggestBuyPrice(market);
                            //The same price is totally unlikely, so we don't check it here
                            _buyOrderId = _requestor.UpdateBuyOrder(_buyOrderId, price, buyOrder.AmountXrp);
                            _buyOrderPrice = price;
                            log("Updated BUY order ID={0}; amount={1} XRP; price={2} {3}", _buyOrderId, _buyOrderAmount, _buyOrderPrice, buyOrder.Currency); 
                        }
                    }
                }
                else
                {
                    //Check if cancelled by Ripple due to "lack of funds"
                    //TODO: check for closed offers that were partially filled ("rest of order cancelled due to lack...")
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
                    else
                    {
                        _executedBuyPrice = _buyOrderPrice;
                        log("BUY order ID={0} (amount={1} XRP) was closed at price={2} {3}",
                            ConsoleColor.Green, _buyOrderId, _buyOrderAmount, _executedBuyPrice, _currencyCode);
                        _buyOrderId = -1;
                        _buyOrderAmount = 0;
                    }
                }
            }
            else if (_operativeAmount - _sellOrderAmount > 0.00001)    //No BUY order (and there are some money available). So create one
            {
                _buyOrderPrice = suggestBuyPrice(market);
                _buyOrderAmount = _operativeAmount - _sellOrderAmount;
                _buyOrderId = _requestor.PlaceBuyOrder(_buyOrderPrice, _buyOrderAmount);

                if (-1 != _buyOrderId)
                {
                    log("Successfully created BUY order with ID={0}; amount={1} XRP; price={2} {3}",
                        ConsoleColor.Cyan, _buyOrderId, _buyOrderAmount, _buyOrderPrice, _currencyCode);
                }
            }

            //Handle SELL order
            if (_operativeAmount - _buyOrderAmount > 0.00001)
            {
                //SELL order already existed
                if (-1 != _sellOrderId)
                {
                    var sellOrder = _requestor.GetOrderInfo(_sellOrderId);

                    if (null == sellOrder)
                        return;

                    //The order is still open
                    if (!sellOrder.Closed)
                    {
                        log("SELL order ID={0} open (amount={1} XRP, price={2} {3})", _sellOrderId, sellOrder.AmountXrp, _sellOrderPrice, sellOrder.Currency);

                        double price = suggestSellPrice(market);

                        //Partially filled
                        if (!sellOrder.AmountXrp.eq(_sellOrderAmount))
                        {
                            log("SELL order ID={0} partially filled at price={1} {2}. Remaining amount={3} XRP;",
                                ConsoleColor.Green, _sellOrderId, sellOrder.Price, sellOrder.Currency, sellOrder.AmountXrp);

                            //Check remaining amount, drop the SELL if it's very tiny
                            if (sellOrder.AmountXrp < MIN_ORDER_AMOUNT)
                            {
                                log("The remaining SELL amount is too small, canceling the order ID={0}", ConsoleColor.Cyan, _sellOrderId);
                                _requestor.CancelOrder(_sellOrderId);    //Note: no problem if the cancel fails, the breadcrumbs can live own life
                                _sellOrderId = -1;
                                _sellOrderAmount = 0.0;
                            }
                            else
                            {
                                var amount = sellOrder.AmountXrp;
                                _sellOrderId = _requestor.UpdateSellOrder(_sellOrderId, price, ref amount);
                                _sellOrderAmount = amount;
                                _sellOrderPrice = price;
                                log("Updated SELL order ID={0}; amount={1} XRP; price={2} {3}", _sellOrderId, _sellOrderAmount, price, sellOrder.Currency);
                            }
                        }
                        //If there were some money released by filling a BUY order, increase this SELL order
                        else if (_operativeAmount - _buyOrderAmount > _sellOrderAmount)
                        {
                            var newAmount = _operativeAmount - _buyOrderAmount;
                            _sellOrderId = _requestor.UpdateSellOrder(_sellOrderId, price, ref newAmount);
                            _sellOrderAmount = newAmount;
                            _sellOrderPrice = price;
                            log("Updated SELL order ID={0}; amount={1} XRP; price={2} {3}", _sellOrderId, _sellOrderAmount, price, sellOrder.Currency);
                        }
                        //Or if we simply need to change price.
                        else if (!_sellOrderPrice.eq(price))
                        {
                            _sellOrderId = _requestor.UpdateSellOrder(_sellOrderId, price, ref _sellOrderAmount);
                            _sellOrderPrice = price;
                            log("Updated SELL order ID={0}; amount={1} XRP; price={2} {3}", _sellOrderId, _sellOrderAmount, price, sellOrder.Currency);
                        }
                    }
                    else        //Closed or cancelled
                    {
                        //Check if cancelled by the network
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
                            log("SELL order ID={0} (amount={1} XRP) was closed at price={2} {3}",
                                ConsoleColor.Green, _sellOrderId, _sellOrderAmount, _sellOrderPrice, _currencyCode);
                            _sellOrderAmount = 0;
                            _sellOrderId = -1;
                        }
                    }
                }
                else    //No SELL order, create one
                {
                    _sellOrderPrice = suggestSellPrice(market);
                    var amount = _operativeAmount - _buyOrderAmount;
                    _sellOrderId = _requestor.PlaceSellOrder(_sellOrderPrice, ref amount);
                    _sellOrderAmount = amount;

                    if (-1 != _sellOrderId)
                    {
                        log("Successfully created SELL order with ID={0}; amount={1} XRP; price={2} {3}",
                            ConsoleColor.Cyan, _sellOrderId, _sellOrderAmount, _sellOrderPrice, _currencyCode);
                    }
                }
            }

            _xrpBalance = _requestor.GetXrpBalance();
            log("### Balance= {0} XRP", _xrpBalance);
            log(new string('=', 84));
        }

        private double suggestBuyPrice(Market market)
        {
            const int decPlaces = 14;
            double increment = Math.Pow(10.0, -1.0*decPlaces); // 0.00000000000001;
            double sum = 0;
            var lowestAsk = market.Asks.First().Price;

            foreach (var bid in market.Bids)
            {
                if (sum + _operativeAmount > _volumeWall && bid.Price + 2.0 * _minDifference < lowestAsk)
                {
                    double buyPrice = Math.Round(bid.Price + increment, decPlaces);

                    //The difference is too small and we'd be not first in BUY orders. Leave previous price to avoid server call
                    if (-1 != _buyOrderId && buyPrice < market.Bids[0].Price && Math.Abs(buyPrice - _buyOrderPrice) < _minPriceUpdate)
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
            var price = market.Bids.Last().Price + increment;
            if (-1 != _buyOrderId && Math.Abs(price - _buyOrderPrice) < _minPriceUpdate)
                return _buyOrderPrice;
            return Math.Round(price, 7);
        }

        private double suggestSellPrice(Market market)
        {
            const int decPlaces = 14;
            double increment = Math.Pow(10.0, -1.0 * decPlaces); // 0.00000000000001;
            //Ignore offers with tiny XRP volume (<100 XRP)
            const double MIN_WALL_VOLUME = 100.0;

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

                if (ask.Price > _executedBuyPrice + _minDifference)
                {
                    return ask.Price.eq(_sellOrderPrice)
                        ? _sellOrderPrice
                        : Math.Round(ask.Price - increment, decPlaces);
                }
            }

            //All SELL orders are too low (probably some terrible fall). Suggest SELL order with minimum profit and hope :-( TODO: maybe some stop-loss strategy
            return _executedBuyPrice + _minDifference;
        }
    }
}
