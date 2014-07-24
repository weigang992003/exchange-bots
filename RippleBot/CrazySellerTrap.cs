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
        private readonly RippleWebSocketApi _requestor;
        private int _intervalMs;

        //BTC amount to trade
        private readonly double _operativeAmount;
        private readonly double _minWallVolume;
        private readonly double _maxWallVolume;
        //Volumen of BTC necessary to accept our offer
        private double _volumeWall;
        //Minimum difference between BUY price and subsequent SELL price (so we have at least some profit)
        private const double MIN_DIFFERENCE = 0.8;
        //Tolerance of BUY price (factor). Usefull if possible price change is minor, to avoid frequent order updates.
        private const double PRICE_DELTA = 0.05;    //5%

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
            _requestor = new RippleWebSocketApi(logger);
            _requestor.Init();
        }

        public void StartTrading()
        {
/*            do
            {
                try
                {*/
                    check();
/*                    Thread.Sleep(_intervalMs);
                }
                catch (Exception ex)
                {
                    log("ERROR: " + ex.Message + Environment.NewLine + ex.StackTrace);
                    throw;
                }
            } while (!_killSignal);*/
        }

        public void Kill()
        {
            _killSignal = true;
            _requestor.Close();
            log("Crazy Seller Trap trader received kill signal. Good bye.");
        }


        private void check()
        {
/*            var balances = _requestor.GetAccountBalance(myAddress);
            log("XRP balance={0}; USD balance={1}", balances.AvailableXrp, balances.AvailableUsd);
*/

            

            var candles = _requestor.GetTradeStatistics(new TimeSpan(2, 0, 0));

/*            var market = _requestor.GetMarketDepth();
            log("BIDs:");
            foreach (var bid in market.Bids)
                log("BUY " + bid.Amount + " for " + bid.Price + " USD");

            log("==================");
            log("ASKs:");
            foreach (var ask in market.Asks)
                log("SELL " + ask.Amount + " for " + ask.Price + " USD");*/

            var coef = TradeHelper.GetMadness(candles.results);
            _volumeWall = Helpers.SuggestWallVolume(coef, _minWallVolume, _maxWallVolume);
            _intervalMs = Helpers.SuggestInterval(coef, 8000, 20000);
            log("Madness={0}; Volume={1} BTC; Interval={2} ms;", coef, _volumeWall, _intervalMs);

/*            var buyId = _requestor.PlaceBuyOrder(0.004321, 14);
            log("Success created BUY order with ID " + buyId);
            log("==================");
            var debug = _requestor.GetOrderInfo(buyId);
            log(debug.Type + " " + debug.AmountXrp + " for " + debug.Price + " USD (absolute " + debug.AmountUsd + " USD)");

            _requestor.CancelOrder(buyId);*/

            var balanceXrp = _requestor.GetXrpBalance();
            log("I have {0:0.000} XRP", balanceXrp);

            var amount = 4.0;
            var sellId = _requestor.PlaceSellOrder(0.00654, ref amount);
            log("Success created SELL order with ID " + sellId);
            log("==================");
            var debug = _requestor.GetOrderInfo(sellId);
            log(debug.Type + " " + debug.AmountXrp + " for " + debug.Price + " USD (absolute " + debug.AmountUsd + " USD)");

            _requestor.CancelOrder(sellId);

            log(new string('=', 80));
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
