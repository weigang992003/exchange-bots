using RippleBot.Business.DataApi;
using System;
using System.Collections.Generic;
using System.Linq;


namespace RippleBot
{
    internal class TradeHelper
    {
        /// <summary>Returns numeric indicator of market activity. Higher value means higher activity (i.e. lot of trades with higher volume).</summary>
        /// <param name="last5mCandle">Recent trading statistics</param>
        /// <returns>Coeficient in [0.0, 1.0] where 0.0 means totally peacefull market, 1.0 is wild.</returns>
        internal static float GetMadness(List<Candle> candles)
        {
            //No recent trading at all
            if (!candles.Any())
                return 0.0f;

            var last5mCandle = candles.Last();

            //Last candle is too old
            if (last5mCandle.StartTime < DateTime.Now.Subtract(new TimeSpan(0, 5, 0)))
                return 0.0f;

            //Last candle has just been open, merge it with previous
            if (last5mCandle.partial && last5mCandle.StartTime > DateTime.Now.Subtract(new TimeSpan(0, 2, 0)))
            {
                if (candles.Count > 1)
                {
                    var beforeLast = candles[candles.Count - 2];
                    last5mCandle = new Candle
                    {
                        startTime = beforeLast.startTime,
                        count = beforeLast.count + last5mCandle.count,
                        baseVolume = beforeLast.baseVolume + last5mCandle.count
                    };
                }
            }

            const int MIN_TRADES = 2;
            const int MAX_TRADES = 10;
            float intenseCoef;
            if (last5mCandle.count < MIN_TRADES)        //Too few trades
                intenseCoef = 0.0f;
            else if (last5mCandle.count >= MAX_TRADES)  //Too many trades
                intenseCoef = 1.0f;
            else
                intenseCoef = (float)(last5mCandle.count - MIN_TRADES) / (MAX_TRADES - MIN_TRADES);

            const double MIN_AVG_VOLUME = 400.0;
            const double MAX_AVG_VOLUME = 3000.0;
            float volumeCoef;
            double avgVolume = last5mCandle.baseVolume / last5mCandle.count;

            if (avgVolume < MIN_AVG_VOLUME)
                volumeCoef = 0.0f;
            else if (avgVolume >= MAX_AVG_VOLUME)
                volumeCoef = 1.0f;
            else
                volumeCoef = (float)((avgVolume - MIN_AVG_VOLUME) / (MAX_AVG_VOLUME - MIN_AVG_VOLUME));

            //Average of volume and frequency coeficients
            return (intenseCoef + volumeCoef) / 2;
        }
    }
}
