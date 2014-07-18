using Common;
using System;
using System.IO;
using System.Net;
using System.Threading;


namespace RippleBot
{
    class Program
    {
        static void Main(string[] args)
        {
            //TODO: standard code

            var strategy = "cst";
            var logger = new Logger("C:\\temp\\ripple_test.log");

            ITrader trader;
            switch (strategy.ToLower())
            {
                case "cst":
                    trader = new CrazySellerTrap(logger);
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
    }
}
