using System;
using System.Linq;
using BtceBot.Business;
using Common;
using Common.Business;


namespace BtceBot
{
    internal class CrazySellerTrap : TraderBase
    {
        private readonly BtceApi _requestor;

        //Mininum order amount on BTC-E, the server returns error when trying to create smaller one
        private const double MIN_ORDER_AMOUNT = 0.1;

        //LTC amount to trade
        private readonly double _operativeAmount;
        private readonly double _minWallVolume;
        private readonly double _maxWallVolume;
        //Volumen of LTC necessary to accept our offer
        private double _volumeWall;
        //Minimum difference between SELL price and subsequent BUY price (so we have at least some profit). Note: fee is 0.2%
        private const double MIN_DIFFERENCE = 0.05;
        //Tolerance of SELL price (absolute value in USD). Usefull if possible price change is minor, to avoid frequent order updates.
        private const double MIN_PRICE_DELTA = 0.015;    //1.5 cents per LTC

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


        public CrazySellerTrap(Logger logger) : base(logger)
        {
            _operativeAmount = double.Parse(Configuration.GetValue("operative_amount"));
            _minWallVolume = double.Parse(Configuration.GetValue("min_volume"));
            _maxWallVolume = double.Parse(Configuration.GetValue("max_volume"));
            log("LTC-E Litecoin CST trader initialized with operative={0}; MinWall={1}; MaxWall={2}", _operativeAmount, _minWallVolume, _maxWallVolume);
            _requestor = new BtceApi(logger);
        }

        protected override void Check()
        {
            var market = _requestor.GetMarketDepth();
            var tradeHistory = _requestor.GetTradeHistory();
            var serverTime = _requestor.GetServerTime();

            var coef = TradeHelper.GetMadness(tradeHistory.trades, serverTime);
            _volumeWall = Helpers.SuggestWallVolume(coef, _minWallVolume, _maxWallVolume);
            _intervalMs = Helpers.SuggestInterval(coef, 5000, 18000);
            log("Coef={0}, Volume={1} LTC; Interval={2} ms; ", coef, _volumeWall, _intervalMs);

            //We have active BUY order
            if (-1 != _buyOrderId)
            {
                var buyOrder = _requestor.GetOrderInfo(_buyOrderId);

                if (!buyOrder.Closed)
                {
                    //Untouched
                    if (buyOrder.Amount.eq(_buyOrderAmount))
                    {
                        log("BUY order ID={0} untouched (amount={1} LTC, price={2} USD)", _buyOrderId, _buyOrderAmount, _buyOrderPrice);

                        var newAmount = _operativeAmount - _sellOrderAmount;
                        if (newAmount < MIN_ORDER_AMOUNT)
                            log("The order has amount under limit of {0} LTC, will not update", MIN_ORDER_AMOUNT);
                        else
                        {
                            double price = SuggestBuyPrice(market);

                            //Evaluate and update if needed
                            if (newAmount > _buyOrderAmount || !_buyOrderPrice.eq(price))
                            {
                                _buyOrderAmount = newAmount;
                                _buyOrderId = _requestor.UpdateBuyOrder(_buyOrderId, price, newAmount);
                                _buyOrderPrice = price;
                                log("Updated BUY order ID={0}; amount={1} LTC; price={2} USD", _buyOrderId, _buyOrderAmount, price);
                            }
                        }
                    }
                    else    //Partially filled
                    {
                        _executedBuyPrice = buyOrder.Price;
                        _buyOrderAmount = buyOrder.amount;
                        log("BUY order ID={0} partially filled at price={1} USD. Remaining amount={2} LTC;",
                            ConsoleColor.Green, _buyOrderId, _executedBuyPrice, buyOrder.Amount);

                        if (_buyOrderAmount < MIN_ORDER_AMOUNT)
                            log("The order has amount under limit of {0} LTC, will not update", MIN_ORDER_AMOUNT);
                        else
                        {
                            var price = SuggestBuyPrice(market);
                            //The same price is totally unlikely, so we don't check it here
                            _buyOrderId = _requestor.UpdateBuyOrder(_buyOrderId, price, buyOrder.Amount);
                            _buyOrderPrice = price;
                            log("Updated BUY order ID={0}; amount={1} LTC; price={2} USD", _buyOrderId, _buyOrderAmount, _buyOrderPrice);
                        }
                    }
                }
                else
                {
                    _executedBuyPrice = buyOrder.Price;
                    _buyOrderId = -1;
                    log("BUY order ID={0} (amount={1} LTC) was closed at price={2} USD", ConsoleColor.Green, buyOrder.id, _buyOrderAmount, _executedBuyPrice);
                    _buyOrderAmount = 0;
                }
                
            }
            else if (_operativeAmount - _sellOrderAmount > 0.00001)    //No BUY order (and there are some money available). So create one
            {
                _buyOrderPrice = SuggestBuyPrice(market);
                _buyOrderAmount = _operativeAmount - _sellOrderAmount;
                _buyOrderId = _requestor.PlaceBuyOrder(_buyOrderPrice, _buyOrderAmount);
                log("Successfully created BUY order with ID={0}; amount={1} LTC; price={2} USD", ConsoleColor.Cyan, _buyOrderId, _buyOrderAmount, _buyOrderPrice);
            }

            //Handle SELL order
            if (_operativeAmount - _buyOrderAmount > 0.00001)
            {
                //SELL order already existed
                if (-1 != _sellOrderId)
                {
                    var sellOrder = _requestor.GetOrderInfo(_sellOrderId);

                    if (!sellOrder.Closed)
                    {
                        log("SELL order ID={0} open (amount={1} LTC, price={2} USD)", _sellOrderId, sellOrder.Amount, _sellOrderPrice);

                        double price = SuggestSellPrice(market);

                        //Partially filled
                        if (!sellOrder.Amount.eq(_sellOrderAmount))
                        {
                            log("SELL order ID={0} partially filled at price={1} USD. Remaining amount={2} LTC;",
                                ConsoleColor.Green, _sellOrderId, sellOrder.Price, sellOrder.Amount);

                            if (sellOrder.amount < MIN_ORDER_AMOUNT)
                            {
                                log("The order has amount under limit of {0} LTC, will not update", MIN_ORDER_AMOUNT);
                                _buyOrderAmount = sellOrder.amount;
                            }
                            else
                            {
                                var amount = sellOrder.Amount;
                                _sellOrderId = _requestor.UpdateSellOrder(_sellOrderId, price, ref amount);
                                _sellOrderAmount = amount;
                                _sellOrderPrice = price;
                                log("Updated SELL order ID={0}; amount={1} LTC; price={2} USD", _sellOrderId, _sellOrderAmount, price);
                            }
                        }
                        //If there were some money released by filling a BUY order, increase this SELL order
                        else if (_operativeAmount - _buyOrderAmount > _sellOrderAmount)
                        {
                            var newAmount = _operativeAmount - _buyOrderAmount;
                            if (newAmount < MIN_ORDER_AMOUNT)
                            {
                                log("BUY gained some LTC (available={0}), but not enough to create new SELL order", ConsoleColor.Cyan, newAmount);
                                _buyOrderAmount = newAmount;
                            }
                            else
                            {
                                log("BUY gained some LTC. Increasing SELL amount to {0} LTC", ConsoleColor.Cyan, newAmount);
                                _sellOrderId = _requestor.UpdateSellOrder(_sellOrderId, price, ref newAmount);
                                _sellOrderAmount = newAmount;
                                _sellOrderPrice = price;
                                log("Updated SELL order ID={0}; amount={1} LTC; price={2} USD", _sellOrderId, _sellOrderAmount, price);
                            }
                        }
                        //Or if we simply need to change price.
                        else if (!_sellOrderPrice.eq(price))
                        {
                            _sellOrderId = _requestor.UpdateSellOrder(_sellOrderId, price, ref _sellOrderAmount);
                            _sellOrderPrice = price;
                            log("Updated SELL order ID={0}; amount={1} LTC; price={2} USD", _sellOrderId, _sellOrderAmount, price);
                        }
                    }
                    else
                    {
                        log("SELL order ID={0} (amount={1} LTC) was closed at price={2} USD", ConsoleColor.Green, _sellOrderId, _sellOrderAmount, sellOrder.Price);
                        _sellOrderAmount = 0;
                        _sellOrderId = -1;
                    }
                }
                else    //No SELL order, create one
                {
                    _sellOrderPrice = SuggestSellPrice(market);
                    var amount = _operativeAmount - _buyOrderAmount;
                    _sellOrderId = _requestor.PlaceSellOrder(_sellOrderPrice, ref amount);
                    _sellOrderAmount = amount;
                    log("Successfully created SELL order with ID={0}; amount={1} LTC; price={2} USD", ConsoleColor.Cyan, _sellOrderId, _sellOrderAmount, _sellOrderPrice);
                }
            }

            var balance = _requestor.GetAccountBalance();
            log("DEBUG: Balance = {0} LTC", balance);
            log(new string('=', 90));
        }

        protected virtual double SuggestBuyPrice(IMarketDepthResponse<MarketOrder> market)        //TODO: To TraderBase
        {
            double sum = 0;
            var lowestAsk = market.Asks.First().Price;

            foreach (var bid in market.Bids)
            {
                if (sum + _operativeAmount > _volumeWall && bid.Price + MIN_DIFFERENCE < lowestAsk)
                {
                    double buyPrice = Math.Round(bid.Price + 0.001, 3);

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
            var price = market.Bids.Last().Price + 0.001;
            if (-1 != _buyOrderId && Math.Abs(price - _buyOrderPrice) < MIN_PRICE_DELTA)
                return _buyOrderPrice;
            return Math.Round(price, 3);
        }

        protected virtual double SuggestSellPrice(IMarketDepthResponse<MarketOrder> market)
        {
            const double MIN_WALL_VOLUME = 0.8;

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
                        : ask.Price - 0.001;
                }
            }

            //All SELL orders are too low (probably some terrible fall). Suggest SELL order with minimum profit and hope :-( TODO: maybe some stop-loss strategy
            return _executedBuyPrice + MIN_DIFFERENCE;
        }
    }
}
