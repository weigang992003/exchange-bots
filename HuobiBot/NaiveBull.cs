using System;
using System.Linq;
using System.Threading;
using Common;


namespace HuobiBot
{
    /// <summary>
    /// Very experimental predictive bearish strategy. Based on bull, sells when the price rises ehough above previous executed buy.
    /// </summary>
    internal class NaiveBull : ITrader
    {
        private bool _killSignal;
        private bool _verbose = true;
        private readonly Logger _logger;
        private readonly HuobiApi _requestor;
        private readonly MarketTrend _trend;
        private int _intervalMs;

        //Available BTC to trade
        private const double OPERATIVE_AMOUNT = 0.1;    //TODO: whatever
        //Minimum difference between SELL price and subsequent BUY price (so we have at least some profit)
        private const double MIN_DIFFERENCE = 2.5;

        //Active SELL order ID
        private int _sellOrderId = -1;
        //Active SELL order amount
        private double _sellOrderAmount;
        //Active SELL order price
        private double _sellOrderPrice;
        //The price at which we bought
        private double _executedBuyPrice = -1.0;


        public NaiveBull(Logger logger)
        {
            _logger = logger;
            _logger.AppendMessage("Naive Bull trader initialized with operative amount " + OPERATIVE_AMOUNT + " BTC");
            _requestor = new HuobiApi(logger);
            _trend = new MarketTrend();
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
            log("Naive Bull trader received kill signal. Good bye.");
        }


        private void check()
        {
            var candles = _requestor.GetCandles();
            if (null == candles)
            {
                log("candles==NULL, jump", ConsoleColor.Yellow);
                return;
            }

            var market = _requestor.GetMarketDepth();
            if (null == market || !market.IsValid)
            {
                log("market==NULL or doesn't bear enough data; jump", ConsoleColor.Yellow);
                return;
            }
            var tradeStats = _requestor.GetTradeStatistics();
            var serverTime = _requestor.GetServerTime();

            var coef = TradeHelper.GetMadness(tradeStats, serverTime);
            _intervalMs = Helpers.SuggestInterval(coef, 5000, 20000);    //TODO: shouldn't be needed here
            log("Madness={0}, Interval={1} ms; ", coef, _intervalMs);

            //We're in buying mode
            if (-1 == _sellOrderId)
            {
                var buyReason = _trend.ReasonToBuyBack(candles, tradeStats);
                if (null != buyReason)
                {
                    log(DateTime.Now.ToShortTimeString() + " DEBUG: Reason to BUY=" + buyReason, ConsoleColor.Cyan);
                    _executedBuyPrice = market.Asks.First().Price;      //TODO: probably do explicit request to ask what actual price was
                    log("Doing market BUY at price=" + _executedBuyPrice, ConsoleColor.Green);
                    _sellOrderPrice = _executedBuyPrice + MIN_DIFFERENCE;
                    log("Placing SELL order at price=" + _sellOrderPrice, ConsoleColor.Cyan);
                    _sellOrderId = 12345;
                }
                log("Waiting for BUY reason...");
            }
            else
            {
                if (tradeStats.sells.Any(b => b.Price >= _sellOrderPrice))
                {
                    log("SELL order ID={0} closed at price={1}", ConsoleColor.Green, _sellOrderId, _sellOrderPrice);
                    _sellOrderId = -1;
                    _sellOrderPrice = -1.0;
                }
                else log("SELL order ID={0} untouched (bought at {1} CNY)", _sellOrderId, _executedBuyPrice);
            }

            log(new string('=', 80));
        }



        //TODO: put this (and Kill) into AbstractTrader
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
