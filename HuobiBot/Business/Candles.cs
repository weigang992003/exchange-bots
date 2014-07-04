using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.Serialization;
using Common;


namespace HuobiBot.Business
{
    [DataContract]
    internal class CandlesResponse
    {
        /// <summary>Raw OHLC data for 1 minute candles. Format is DateTime, Open, High, Low, Close, Volue.</summary>
        [DataMember] internal List<List<string>> candleData { get; set; }

        /// <summary>Build denser candle chart</summary>
        internal List<Candle> MergeAsCandles(int minutes)
        {
            var merged = new Dictionary<DateTime, Candle>();

            foreach (var data in candleData)
            {
                var timestamp = DateTime.ParseExact(data[0], "yyyyMMddHHmmss000", CultureInfo.InvariantCulture);

                var startTime = timestamp.AddMinutes(-1* (timestamp.Minute%minutes));
                if (!merged.ContainsKey(startTime))
                    merged.Add(startTime, new Candle(startTime, new TimeSpan(0, minutes, 0)));

                var candle = merged[startTime];

                var open = double.Parse(data[1]);
                var close = double.Parse(data[4]);
                var volume = double.Parse(data[5]);

                if (timestamp == startTime || candle.OpeningPrice.eq(-1.0))
                    candle.OpeningPrice = open;
                candle.ClosingPrice = close;    //Rewritten few times
                candle.Volume += volume;
            }

            var candles = merged.Values.OrderBy(candle => candle.StartTime);
            return candles.ToList();
        }
    }

    internal class Candle
    {
        internal readonly DateTime StartTime;
        internal readonly TimeSpan Length;
        internal double OpeningPrice = -1.0;
        internal double ClosingPrice = -1.0;

        /// <summary>BTC volume</summary>
        internal double Volume = 0.0;

        internal Candle(DateTime start, TimeSpan length)
        {
            StartTime = start;
            Length = length;
        }
    }
}
