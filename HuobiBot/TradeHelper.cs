using System;
using System.Collections.Generic;
using System.Linq;
using Common;
using HuobiBot.Business;


namespace HuobiBot
{
    internal static class TradeHelper
    {
        /// <summary>Returns numeric indicator of market activity. Higher value means higher activity (i.e. lot of trades with higher volume).</summary>
        /// <param name="tradeStats">Huobi statistics about trading activity</param>
        /// <param name="now">Current local time of the exchange</param>
        /// <returns>Coeficient in [0.0, 1.0] where 0.0 means totally peacefull market, 1.0 is wild.</returns>
        internal static float GetMadness(TradeStatisticsResponse tradeStats, DateTime now)
        {
            //For case we have broken data returned from server
            if (null == tradeStats || null == tradeStats.trades || !tradeStats.trades.Any() || DateTime.MinValue == now)
                return 0.75f;

            //There's always exactly 60 past trades
            var trades = tradeStats.trades.Where(trade => trade.TimeTyped >= now.AddSeconds(-120)).ToList();
            if (!trades.Any())
                return 0.0f;

            //Group by time, so that single trade with big volume doesn't look like many trades
            var groupped = new Dictionary<string, Trade>();
            foreach (var trade in trades)
            {
                var key = trade.time + "_" + trade.type;
                if (!groupped.ContainsKey(key))
                    groupped.Add(key, new Trade { time = trade.time, type = trade.type, amount = trade.amount, price = trade.price });
                else
                {
                    groupped[key].amount += trade.amount;
                    if (TradeType.BUY == trade.Type && trade.amount > groupped[key].amount)
                        groupped[key].amount = trade.amount;
                    else if (TradeType.SELL == trade.Type && trade.amount < groupped[key].amount)
                        groupped[key].amount = trade.amount;
                }
            }

            //        Console.WriteLine("DEBUG: past {0} trades, {1} groupped by time", tradeStats.trades.Count, groupped.Count);

            var firstTradeTime = tradeStats.trades.Last().TimeTyped;
            var lastTradeTime = tradeStats.trades.First().TimeTyped;
            var tradesTimeRange = lastTradeTime - firstTradeTime;

            var MIN_SPEED = new TimeSpan(0, 6, 0);  //All the trades spread in 6 minutes
            var MAX_SPEED = new TimeSpan(0, 2, 0);
            float intenseCoef;
            if (tradesTimeRange > MIN_SPEED)        //Trading speed (trades/time) too low
                intenseCoef = 0.0f;
            else if (tradesTimeRange < MAX_SPEED)
                intenseCoef = 1.0f;
            else
                intenseCoef = 1.0f - (float) ((tradesTimeRange - MAX_SPEED).TotalSeconds / (MIN_SPEED - MAX_SPEED).TotalSeconds);

            const double MIN_AVG_VOLUME = 0.8;
            const double MAX_AVG_VOLUME = 20.0;
            float volumeCoef;
            double avgVolume = groupped.Sum(trade => trade.Value.amount) / groupped.Count;
            //        Console.WriteLine("DEBUG: avgVolume={0}", avgVolume);
            if (avgVolume < MIN_AVG_VOLUME)
                volumeCoef = 0.0f;
            else if (avgVolume >= MAX_AVG_VOLUME)
                volumeCoef = 1.0f;
            else
                volumeCoef = (float)((avgVolume - MIN_AVG_VOLUME) / (MAX_AVG_VOLUME - MIN_AVG_VOLUME));

            //        Console.WriteLine("DEBUG: intensityCoef={0}, volumeCoef={1}", intenseCoef, volumeCoef);

            //Average of volume and frequency coeficients
            return (intenseCoef + volumeCoef) / 2;
        }
    }
}
