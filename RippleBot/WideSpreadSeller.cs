using System;
using System.Linq;
using Common;
using RippleBot.Business;


namespace RippleBot
{
    /// <summary>
    /// Bearish trading strategy for chinese RippleCN.com. When spread is big enough, sells and then re-buys in between.
    /// Only one order is active at a time, SELL order is not created until BUY order is completely filled.
    /// </summary>
    internal class WideSpreadSeller : TraderBase
    {
        private readonly RippleApi _requestor;

        //BTC amount to trade
        private readonly double _operativeAmount;
        private const double MIN_SPREAD = 0.0002;
        //Minimum difference between BUY price and subsequent SELL price (so we have at least some profit)
        private const double MIN_DIFFERENCE = 0.000015;
        //Tolerance of BUY price. Usefull if possible price change is minor, to avoid frequent order updates.
        private const double MIN_PRICE_DELTA = 0.0000012;    //0.0000012 XRP

        private bool _selling = true;

        //Active SELL order ID
        private int _sellOrderId = -1;
        //Active SELL order price
        private double _sellOrderPrice;

        //Active BUY order ID
        private int _buyOrderId = -1;
        //Active BUY order amount
        private double _buyOrderAmount;
        //Active BUY order price
        private double _buyOrderPrice;
        //The price at which we sold to a buyer
        private double _executedSellPrice = -1.0;
        private double _executedSellAmount;

        private double _xrpBalance;



        public WideSpreadSeller(Logger logger) : base(logger)
        {
            _operativeAmount = double.Parse(Configuration.GetValue("operative_amount"));
            log("Wide spread trader for RippleCN initialized with operative={0}; MinSpread={1}", _operativeAmount, MIN_SPREAD);
            _requestor = new RippleApi(logger, "rnuF96W4SZoCJmbHYBFoJZpR8eCaxNvekK"/*RippleCN.com*/, "CNY");
            _requestor.Init();
        }

        protected override void Check()
        {
            var candles = _requestor.GetTradeStatistics(new TimeSpan(2, 0, 0));
            var market = _requestor.GetMarketDepth();

            if (null == market)
                return;

            var spread = Math.Round(getLowestAsk(market) - market.Bids.First().Price, 5);

            var coef = TradeHelper.GetMadness(candles.results);
            _intervalMs = Helpers.SuggestInterval(coef, 8000, 20000);
            log("Madness={0}; spread={1} XRP; Interval={2} ms", coef, spread, _intervalMs);

            if (_selling)
            {
                //No active SELL order
                if (-1 == _sellOrderId)
                {
                    if (spread >= MIN_SPREAD)
                    {
                        double price = suggestSellPrice(market);
                        var amount = _operativeAmount;
                        _sellOrderId = _requestor.PlaceSellOrder(price, ref amount);

                        if (-1 != _sellOrderId)
                        {
                            log("Successfully created SELL order with ID={0}; amount={1} XRP; price={2} CNY", ConsoleColor.Cyan, _sellOrderId, amount, price);
                            _sellOrderPrice = price;
                        }
                    }
                    else log("Spread too small for selling");
                }
                else    //We have active SELL order
                {
                    var sellOrder = _requestor.GetOrderInfo(_sellOrderId);

                    if (null == sellOrder)
                        return;

                    if (!sellOrder.Closed)
                    {
                        //Untouched
                        if (sellOrder.AmountXrp.eq(_operativeAmount))
                        {
                            log("SELL order ID={0} untouched (amount={1} XRP, price={2} CNY)", _sellOrderId, _operativeAmount, _sellOrderPrice);

                            if (spread < MIN_SPREAD)
                            {
                                log("Spread too small, canceling order ID={0}", ConsoleColor.Cyan, _sellOrderId);
                                if (_requestor.CancelOrder(_sellOrderId))
                                {
                                    _sellOrderId = -1;
                                    _sellOrderPrice = -1;
                                }
                            }
                            else
                            {
                                double price = suggestSellPrice(market);

                                //Evaluate and update if needed
                                if (!_sellOrderPrice.eq(price))
                                {
                                    var amount = _operativeAmount;
                                    _sellOrderId = _requestor.UpdateSellOrder(_sellOrderId, price, ref amount);
                                    _sellOrderPrice = price;
                                    log("Updated SELL order ID={0}; amount={1} XRP; price={2} CNY", _sellOrderId, _operativeAmount, price);
                                }
                            }
                        }
                        else    //Partially filled
                        {
                            _executedSellPrice = sellOrder.Price;
                            _executedSellAmount = _operativeAmount - sellOrder.AmountXrp;
                            log("SELL order ID={0} partially filled at price={1} CNY. Filled amount={2} XRP;", ConsoleColor.Green, _sellOrderId, _executedSellPrice, _executedSellAmount);

                            //Cancel the rest of order
                            if (_requestor.CancelOrder(_sellOrderId))
                            {
                                log("Successfully cancelled SELL order ID={0}", ConsoleColor.Cyan, _sellOrderId);
                                _sellOrderId = -1;
                                _selling = false;
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
                            var amount = _operativeAmount;
                            _sellOrderId = _requestor.PlaceSellOrder(_sellOrderPrice, ref amount);

                            if (-1 != _sellOrderId)
                                log("Successfully recreated SELL order with ID={0}; amount={1} XRP; price={2} CNY", ConsoleColor.Cyan, _sellOrderId, amount, _sellOrderPrice);
                        }
                        else
                        {
                            _executedSellPrice = _sellOrderPrice;
                            _executedSellAmount = _operativeAmount;
                            log("SELL order ID={0} (amount={1} XRP) was closed at price={2} CNY", ConsoleColor.Green, _sellOrderId, _operativeAmount, _executedSellPrice);
                            _sellOrderId = -1;
                            _selling = false;
                        }
                    }
                }
            }
            else
            {
                //No active BUY order
                if (-1 == _buyOrderId)
                {
                    _buyOrderPrice = suggestBuyPrice(market);
                    _buyOrderAmount = _executedSellAmount;
                    _buyOrderId = _requestor.PlaceBuyOrder(_buyOrderPrice, _buyOrderAmount);
                    log("Successfully created BUY order with ID={0}; amount={1} XRP; price={2} CNY", ConsoleColor.Cyan, _buyOrderId, _buyOrderAmount, _buyOrderPrice);
                }
                else
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
                        //We simply need to change price.
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
                        else
                        { 
                            log("BUY order ID={0} (amount={1} XRP) was closed at price={2} CNY", ConsoleColor.Green, _buyOrderId, _buyOrderAmount, _buyOrderPrice);
                            _buyOrderAmount = 0;
                            _buyOrderId = -1;
                            _selling = true;
                        }
                    }
                }
            }

            _xrpBalance = _requestor.GetXrpBalance();
            log("### Balance= {0} XRP", _xrpBalance);
            log(new string('=', 84));
        }

        private double getLowestAsk(Market market)
        {
            var lowestAsk = market.Asks.First().Price;
            if (-1 != _sellOrderId)
            {
                //Don't count own order if it's the lowest ask
                var ask = market.Asks.First();
                if (ask.Amount.eq(_operativeAmount) && ask.Price.eq(_sellOrderPrice))
                    lowestAsk = market.Asks[1].Price;
            }

            return lowestAsk;
        }

        private double suggestSellPrice(Market market)
        {
            var lowestAsk = getLowestAsk(market);
            var highestBid = market.Bids.First().Price;
            var spread = lowestAsk - highestBid;

            //Suggest price as 1/3 between lowest ask and highest bid price, closer to ask
            var sellPrice = Math.Round(lowestAsk - (spread/3.0), 7);

            //The difference is too small and we'd be not the first SELL order. Leave previous price to avoid server call
            if (-1 != _sellOrderId && Math.Abs(sellPrice - _sellOrderPrice) < MIN_PRICE_DELTA)
            {
                log("DEBUG: SELL price {0} too similar, using previous", sellPrice);
                return _sellOrderPrice;
            }

            return sellPrice;
        }

/*TODO: test, delete        private double suggestBuyPrice(Market market)
        {
            var maxPrice = _executedSellPrice - MIN_DIFFERENCE;

            var highestBid = market.Bids.First().Price;

            if (-1 != _buyOrderId)
            {
                //Don't count own order
                var bid = market.Bids.First();
                if (bid.Amount.eq(_buyOrderAmount) && bid.Price.eq(_buyOrderPrice))
                    highestBid = market.Bids[1].Price;
            }

            //Somebody offers higher price than we can
            if (highestBid > maxPrice)
                return maxPrice;

            //Sugest buy price as middle between our threshold and highest bid
            var buyPrice = maxPrice - ((maxPrice - highestBid)/2.0);
            return Math.Round(buyPrice, 7);
        }
*/

        private double suggestBuyPrice(Market market)
        {
            const double MIN_WALL_VOLUME = 100.0;
            var maxPrice = _executedSellPrice - MIN_DIFFERENCE;
            var highestBid = market.Bids.First().Price;

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
                    highestBid = bid.Price;
            }

            //Sugest buy price as middle between our threshold and highest bid
            var buyPrice = maxPrice - ((maxPrice - highestBid) / 2.0);
            return Math.Round(buyPrice, 7);
        }
    }
}
