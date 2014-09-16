using Common;
using System;
using System.Linq;
using System.Threading;


namespace RippleBot
{
    class Program
    {
        static void Main(string[] args)
        {
            if (args.Length != 2 || !args.Any(arg => arg.StartsWith("--config=")) || !args.Any(arg => arg.StartsWith("--log=")))
            {
                usage();
                return;
            }

            var logFile = args.First(arg => arg.StartsWith("--log=")).Substring("--log=".Length);
            var logger = new Logger(logFile);

            var configFile = args.First(arg => arg.StartsWith("--config=")).Substring("--config=".Length);
            Configuration.Load(configFile);
            var strategy = Configuration.Strategy;

            ITrader trader;
            switch (strategy.ToLower())
            {
                case "cst":
                    trader = new CrazySellerTrap(logger);
                    break;
                //TODO: get rid of rest CST junk
                case "cst-bitstamp":
                    trader = new CrazySellerTrap_BitStamp(logger);
                    break;
                case "cst-rippletradejapan":
                    trader = new CrazySellerTrap_RippleTradeJapan(logger);
                    break;
                case "cbt-ripplecn":
                    trader = new CrazyBuyerTrap_RippleCN(logger);
                    break;
                case "cbt":
                    trader = new CrazyBuyerTrap(logger);
                    break;
                case "wss-ripplecn":
                    trader = new WideSpreadSeller(logger);
                    break;
                default:
                    throw new ArgumentException("Unknown strategy " + strategy);
            }

            Thread t = new Thread(trader.StartTrading);
            t.Start();

            Console.WriteLine("ENTER quits this app...");
            Console.ReadLine();
            trader.Kill();
        }

        static void usage()
        {
            Console.WriteLine("Ripple trading bot. Usage: bot.exe --config=<config file path> --log=<log file path>");
            Console.WriteLine("Config is in form key=value on each line. Mandatory keys are 'strategy', 'access_key', 'secret_key'.");
        }
    }
}
