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
