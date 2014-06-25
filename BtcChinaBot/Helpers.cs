using System;
using System.Collections.Generic;
using System.Linq;
using BtcChinaBot.Business;


public static class Helpers
{
    /// <summary>Returns numeric indicator of market activity. Higher value means higher activity (i.e. lot of trades with higher volume).</summary>
    /// <param name="tradeHistory">Last executed trades of exchange</param>
    /// <param name="now">Current local time of the exchange</param>
    /// <returns>Coeficient in [0.0, 1.0] where 0.0 means totally peacefull market, 1.0 is wild.</returns>
    internal static float GetMadness(List<TradeResponse> tradeHistory, DateTime now)
    {
        //Trades of past 120sec
        var trades = tradeHistory.Where(trade => trade.DateTyped >= now.AddSeconds(-120)).ToList();
        if (!trades.Any())
            return 0.0f;

        //Group by time, so that single trade with big volume doesn't look like many trades
        var groupped = new Dictionary<string, TradeResponse>();
        foreach (var trade in trades)
        {
            var key = trade.date + "_" + trade.type;
            if (!groupped.ContainsKey(key))
                groupped.Add(key, new TradeResponse{date = trade.date, tid = "groupped", type = trade.type, amount = trade.amount, price = trade.price});
            else
            {
                groupped[key].amount += trade.amount;
                if ("buy" == trade.type && trade.amount > groupped[key].amount)
                    groupped[key].amount = trade.amount;
                else if ("sell" == trade.type && trade.amount < groupped[key].amount)
                    groupped[key].amount = trade.amount;
            }
        }

//        Console.WriteLine("DEBUG: {0} trades in past 90sec, {1} groupped by time", tradeHistory.Count, groupped.Count);

        const int MIN_TRADES = 3;
        const int MAX_TRADES = 30;
        float intenseCoef;
        if (groupped.Count < MIN_TRADES)        //Too few trades
            intenseCoef = 0.0f;
        else if (groupped.Count >= MAX_TRADES)  //Too many trades
            intenseCoef = 1.0f;
        else
            intenseCoef = (float) (groupped.Count - MIN_TRADES)/(MAX_TRADES - MIN_TRADES);

        const double MIN_AVG_VOLUME = 0.3;
        const double MAX_AVG_VOLUME = 0.8;
        float volumeCoef;
        double avgVolume = groupped.Sum(trade => trade.Value.amount) / groupped.Count;
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

    internal static int SuggestInterval(float madnessCoef)
    {
        const int MIN_INTERVAL = 2000;
        const int MAX_INTERVAL = 11000;

        if (madnessCoef <= 0.0f)
            return MAX_INTERVAL;
        if (madnessCoef >= 1.0f)
            return MIN_INTERVAL;

        return (int) (MIN_INTERVAL + ( (1.0f - madnessCoef) * (MAX_INTERVAL - MIN_INTERVAL)) );
    }

    internal static double SuggestWallVolume(float madnessCoef, double minVolume, double maxVolue)
    {
        if (madnessCoef <= 0.0f)
            return minVolume;
        if (madnessCoef >= 1.0f)
            return maxVolue;

        return (minVolume + (madnessCoef * (maxVolue - minVolume)));
    }
}
