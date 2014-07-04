using System;
using System.Collections.Generic;
using System.Linq;
using KrakenBot.Business;


namespace KrakenBot
{
    public static class TradeHelpers
    {
        /// <summary>Returns numeric indicator of market activity. Higher value means higher activity (i.e. lot of trades with higher volume).</summary>
        /// <param name="tradeHistory">Description of last executed trades of exchange</param>
        /// <param name="now">Current local time of the exchange</param>
        /// <returns>Coeficient in [0.0, 1.0] where 0.0 means totally peacefull market, 1.0 is wild.</returns>
        internal static float GetMadness(TradesHistory tradeHistory, DateTime now)
        {
            //Trades of past 120sec
            List<Trade> trades;
            try
            {
                trades = tradeHistory.Trades.Where(trade => trade.Time >= now.AddSeconds(-120)).ToList();
            }
            catch (Exception debug)
            {
                Console.BackgroundColor = ConsoleColor.DarkMagenta;
                Console.WriteLine(debug.Message + " (tradeHistory=" + (null == tradeHistory ? "NULL" : tradeHistory.last) + ")");
                Console.ResetColor();
                return 0.5f;
            }
            if (!trades.Any())
                return 0.0f;

            //Group by time, so that single trade with big volume doesn't look like many trades
            var groupped = new Dictionary<string, Trade>();
            foreach (var trade in trades)
            {
                var key = trade.Time + "_" + trade.Type;
                if (!groupped.ContainsKey(key))
                    groupped.Add(key, new Trade(trade.Price, trade.Amount, trade.Time,  trade.Type));
                else
                {
                    groupped[key].Amount += trade.Amount;
                    if (TradeType.BUY == trade.Type && trade.Amount > groupped[key].Amount)
                        groupped[key].Amount = trade.Amount;
                    else if (TradeType.SELL == trade.Type && trade.Amount < groupped[key].Amount)
                        groupped[key].Amount = trade.Amount;
                }
            }

//        Console.WriteLine("DEBUG: {0} trades in past 90sec, {1} groupped by time", tradeHistory.Count, groupped.Count);

            const int MIN_TRADES = 1;
            const int MAX_TRADES = 8;
            float intenseCoef;
            if (groupped.Count < MIN_TRADES)        //Too few trades
                intenseCoef = 0.0f;
            else if (groupped.Count >= MAX_TRADES)  //Too many trades
                intenseCoef = 1.0f;
            else
                intenseCoef = (float) (groupped.Count - MIN_TRADES)/(MAX_TRADES - MIN_TRADES);

            const double MIN_AVG_VOLUME = 0.15;
            const double MAX_AVG_VOLUME = 1.5;
            float volumeCoef;
            double avgVolume = groupped.Sum(trade => trade.Value.Amount) / groupped.Count;
//        Console.WriteLine("DEBUG: avgVolume={0}", avgVolume);
            if (avgVolume < MIN_AVG_VOLUME)
                volumeCoef = 0.0f;
            else if (avgVolume >= MAX_AVG_VOLUME)
                volumeCoef = 1.0f;
            else
                volumeCoef = (float) ((avgVolume - MIN_AVG_VOLUME)/(MAX_AVG_VOLUME - MIN_AVG_VOLUME));

//        Console.WriteLine("DEBUG: intensityCoef={0}, volumeCoef={1}", intenseCoef, volumeCoef);

            //Average of volume and frequency coeficients
            return (intenseCoef + volumeCoef) / 2;
        }
    }
}
