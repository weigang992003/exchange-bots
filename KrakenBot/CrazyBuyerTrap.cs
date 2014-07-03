using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Common;

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
        private const double MIN_DIFFERENCE = 0.8;
        //Tolerance of SELL price (factor). Usefull if possible price change is minor, to avoid frequent order updates.
        private const double PRICE_DELTA = 0.05;    //5%


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
            var balance = _requestor.GetAccountBalance().result;
            log("EUR = " + balance.BalanceEur + "; BTC = " + balance.BalanceBtc);

            var now = _requestor.GetServerTime();
            log("Server time is " + now);

            var trades = _requestor.GetTradeHistory();
            Console.WriteLine("Last 15 trades:");
            foreach (var trade in trades.result.Trades.TakeLast(15))
                Console.WriteLine("({0:hh:mm:ss}) {1} {2:0.000} BTC for {3:0.000} EUR", trade.Time, trade.Type, trade.Amount, trade.Price);

            var market = _requestor.GetMarketDepth().result.XXBTZEUR;
            Console.WriteLine("Market: ");
            foreach (var bid in market.Bids)
                Console.WriteLine("BID {0} BTC for {1} EUR", bid.Amount, bid.Price);
            Console.WriteLine();
            foreach (var ask in market.Asks)
                Console.WriteLine("ASK {0} BTC for {1} EUR", ask.Amount, ask.Price);

            var orderInfo = _requestor.GetOrderInfo("OQW26D-ZL74M-5JE7ML").result.orderData;
            log(orderInfo.descr.type + " " + orderInfo.vol + " BTC for " + orderInfo.descr.price + " EUR. Remaining amount=" + orderInfo.Amount + ". Status=" + orderInfo.Status);
            orderInfo = _requestor.GetOrderInfo("OYIGCB-SPMFC-ULV43Q").result.orderData;
            log(orderInfo.descr.type + " " + orderInfo.vol + " BTC for " + orderInfo.descr.price + " EUR. Remaining amount=" + orderInfo.Amount + ". Status=" + orderInfo.Status);

            var cancel = _requestor.CancelOrder("OQW26D-ZL74M-5JE7ML");     //Closed
            log("Cancelled=" + cancel);
            cancel = _requestor.CancelOrder("OWQBUH-H5T2N-RC3BCP");         //Cancelled
            log("Cancelled=" + cancel);

            var debug = _requestor.PlaceBuyOrder(465.88, 0.0222);
            log("New order ID=" + debug);
            orderInfo = _requestor.GetOrderInfo(debug).result.orderData;
            log(orderInfo.descr.type + " " + orderInfo.vol + " BTC for " + orderInfo.descr.price + " EUR. Remaining amount=" + orderInfo.Amount + ". Status=" + orderInfo.Status);

            cancel = _requestor.CancelOrder(debug);
            log("Cancelled=" + cancel);
        }

        public void Kill()
        {
            _killSignal = true;
            log("Crazy Buyer Trap trader received kill signal. Good bye.");
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
