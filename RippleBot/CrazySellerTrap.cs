using Common;
using RippleBot.Business;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;


namespace RippleBot
{
    internal class CrazySellerTrap : ITrader
    {
        private bool _killSignal;
        private bool _verbose = true;
        private readonly Logger _logger;
        private readonly RippleApi _requestor;
        private int _intervalMs;

        //BTC amount to trade
        private readonly double _operativeAmount;
        private readonly double _minWallVolume;
        private readonly double _maxWallVolume;
        //Volumen of XRP necessary to accept our offer
        private double _volumeWall;
        //Minimum difference between BUY price and subsequent SELL price (so we have at least some profit)
        private const double MIN_DIFFERENCE = 0.000015;
        //Tolerance of BUY price. Usefull if possible price change is minor, to avoid frequent order updates.
        private const double PRICE_DELTA = 0.0000015;    //0.0000015 XRP

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


        public CrazySellerTrap(Logger logger)
        {
            _logger = logger;
            _operativeAmount = double.Parse(Configuration.GetValue("operative_amount"));
            _minWallVolume = double.Parse(Configuration.GetValue("min_volume"));
            _maxWallVolume = double.Parse(Configuration.GetValue("max_volume"));
            _logger.AppendMessage(String.Format("Crazy seller trap trader initialized with operative={0}; MinWall={1}; MaxWall={2}", _operativeAmount, _minWallVolume, _maxWallVolume));
            _requestor = new RippleApi(logger);
            _requestor.Init();
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
            _requestor.Close();
            log("Crazy Seller Trap trader received kill signal. Good bye.");
        }


        private void check()
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
                        log("BUY order ID={0} untouched (amount={1} XRP, price={2} USD)", _buyOrderId, _buyOrderAmount, _buyOrderPrice);

                        double price = suggestBuyPrice(market);
                        var newAmount = _operativeAmount - _sellOrderAmount;

                        //Evaluate and update if needed
                        if (newAmount > _buyOrderAmount || !_buyOrderPrice.eq(price))
                        {
                            _buyOrderAmount = newAmount;
                            _buyOrderId = _requestor.UpdateBuyOrder(_buyOrderId, price, newAmount);
                            _buyOrderPrice = price;
                            log("Updated BUY order ID={0}; amount={1} XRP; price={2} USD", _buyOrderId, _buyOrderAmount, price);
                        }
                    }
                    else    //Partially filled
                    {
                        _executedBuyPrice = buyOrder.Price;
                        _buyOrderAmount = buyOrder.AmountXrp;
                        log("BUY order ID={0} partially filled at price={1} USD. Remaining amount={2} XRP;", ConsoleColor.Green, _buyOrderId, _executedBuyPrice, buyOrder.AmountXrp);
                        var price = suggestBuyPrice(market);
                        //The same price is totally unlikely, so we don't check it here
                        _buyOrderId = _requestor.UpdateBuyOrder(_buyOrderId, price, buyOrder.AmountXrp);
                        _buyOrderPrice = price;
                        log("Updated BUY order ID={0}; amount={1} XRP; price={2} USD", _buyOrderId, _buyOrderAmount, _buyOrderPrice);
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
                            log("Successfully created BUY order with ID={0}; amount={1} XRP; price={2} USD", ConsoleColor.Cyan, _buyOrderId, _buyOrderAmount, _buyOrderPrice);
                    }
                    else
                    {
                        _executedBuyPrice = _buyOrderPrice;
                        log("BUY order ID={0} (amount={1} XRP) was closed at price={2} USD", ConsoleColor.Green, _buyOrderId, _buyOrderAmount, _executedBuyPrice);
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
                    log("Successfully created BUY order with ID={0}; amount={1} XRP; price={2} USD", ConsoleColor.Cyan, _buyOrderId, _buyOrderAmount, _buyOrderPrice);
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
                        log("SELL order ID={0} open (amount={1} XRP, price={2} USD)", _sellOrderId, sellOrder.AmountXrp, _sellOrderPrice);

                        double price = suggestSellPrice(market);

                        //Partially filled
                        if (!sellOrder.AmountXrp.eq(_sellOrderAmount))
                        {
                            log("SELL order ID={0} partially filled at price={1} USD. Remaining amount={2} XRP;", ConsoleColor.Green, _sellOrderId, sellOrder.Price, sellOrder.AmountXrp);
                            var amount = sellOrder.AmountXrp;
                            _sellOrderId = _requestor.UpdateSellOrder(_sellOrderId, price, ref amount);
                            _sellOrderAmount = amount;
                            _sellOrderPrice = price;
                            log("Updated SELL order ID={0}; amount={1} XRP; price={2} USD", _sellOrderId, _sellOrderAmount, price);
                        }
                        //If there were some money released by filling a BUY order, increase this SELL order
                        else if (_operativeAmount - _buyOrderAmount > _sellOrderAmount)
                        {
                            var newAmount = _operativeAmount - _buyOrderAmount;
                            _sellOrderId = _requestor.UpdateSellOrder(_sellOrderId, price, ref newAmount);
                            _sellOrderAmount = newAmount;
                            _sellOrderPrice = price;
                            log("Updated SELL order ID={0}; amount={1} XRP; price={2} USD", _sellOrderId, _sellOrderAmount, price);
                        }
                        //Or if we simply need to change price.
                        else if (!_sellOrderPrice.eq(price))
                        {
                            _sellOrderId = _requestor.UpdateSellOrder(_sellOrderId, price, ref _sellOrderAmount);
                            _sellOrderPrice = price;
                            log("Updated SELL order ID={0}; amount={1} XRP; price={2} USD", _sellOrderId, _sellOrderAmount, price);
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
                                log("Successfully created SELL order with ID={0}; amount={1} XRP; price={2} USD", ConsoleColor.Cyan, _sellOrderId, _sellOrderAmount, _sellOrderPrice);
                        }
                        else
                        {
                            log("SELL order ID={0} (amount={1} XRP) was closed at price={2} USD", ConsoleColor.Green, _sellOrderId, _sellOrderAmount, _sellOrderPrice);
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
                        log("Successfully created SELL order with ID={0}; amount={1} XRP; price={2} USD", ConsoleColor.Cyan, _sellOrderId, _sellOrderAmount, _sellOrderPrice);
                }
            }

            _xrpBalance = _requestor.GetXrpBalance();
            log("### Balance= {0} XRP", _xrpBalance);
            log(new string('=', 80));
        }

        private double suggestBuyPrice(Market market)
        {
            double sum = 0;
            var minDiff = PRICE_DELTA;
            var lowestAsk = market.Asks.First().Price;

            foreach (var bid in market.Bids)
            {
                if (sum + _operativeAmount > _volumeWall && bid.Price + 2.0 * MIN_DIFFERENCE < lowestAsk)
                {
                    double buyPrice = Math.Round(bid.Price + 0.000001, 7);

                    //The difference is too small and we'd be not first in BUY orders. Leave previous price to avoid server call
                    if (-1 != _buyOrderId && buyPrice < market.Bids[0].Price && Math.Abs(buyPrice - _buyOrderPrice) < minDiff)
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
            var price = market.Bids.Last().Price + 0.000001;
            if (-1 != _buyOrderId && Math.Abs(price - _buyOrderPrice) < minDiff)
                return _buyOrderPrice;
            return Math.Round(price, 7);
        }

        private double suggestSellPrice(Market market)
        {
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

                if (ask.Price > _executedBuyPrice + MIN_DIFFERENCE)
                {
                    return ask.Price.eq(_sellOrderPrice)
                        ? _sellOrderPrice
                        : Math.Round(ask.Price - 0.000001, 7);
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
