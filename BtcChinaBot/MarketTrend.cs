using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using BtcChinaBot.Business;


namespace BtcChinaBot
{
    /// <summary>
    /// Estimation engine for future market trend based on past trades.
    /// </summary>
    internal class MarketTrend
    {
        //Interval length in minutes
        private const int GROUP_INTERVAL = 3;

        /*
         * TODO: 
         * - connect to Huobi
         * 
         * - one (or few) out-of-trend intervals can't fool the overall trend
         * 
         */


        /// <summary>
        /// Market sentiment indicator in [-1.0, 1.0]. Closer to -1.0 indicates BTC price is falling, 1.0 means it's rising,
        /// around 0.0 is peaceful market
        /// </summary>
        internal float GetSentiment(List<TradeResponse> tradeHistory)
        {
            throw new NotImplementedException("TODO");
        }

        /// <summary>
        /// Returns NULL if market doesn't give enough SELL signals, description of SELL reason otherwise.
        /// </summary>
        internal string ReasonToSell(List<TradeResponse> tradeHistory)
        {
            if (runningSelloff(tradeHistory))
                return "DUMP";

            #region pattern: significant-enough rise followed

            //TODO: this is very primitive! Think, read and analyse! Then code.
            const double SIGNIFICANCE_LIMIT = 1.0;      //When price moved <= 1.0 CNY in one candle, it's not rise/fall
            const double SIGNIFICANT_RISE = 6.0;       //todo: no magic here!
            const int CANDLE_COUNT = 7;
            var candles = getCandleStickData(tradeHistory, new TimeSpan(0, GROUP_INTERVAL, 0));
            if (candles.Count < CANDLE_COUNT)
                throw new ArgumentException("Not enough data. Need wider trade history.");
            candles = candles.GetRange(candles.Count - CANDLE_COUNT, CANDLE_COUNT).ToList();    //Get only last 7 candles

            bool initialRise = true;
            double totalRise = 0.0;
            for (int c = 0; c < candles.Count-2; c++)
            {
                if (candles[c].ClosingPrice - candles[c].OpeningPrice - SIGNIFICANCE_LIMIT < 0.0)
                {
                    initialRise = false;
                    break;
                }
                totalRise += candles[c].ClosingPrice - candles[c].OpeningPrice;
            }
            //Last 2 candles suggest fall
            if (initialRise && totalRise / (candles.Count-2) >= SIGNIFICANT_RISE)
            {
                var beforeLast = candles[candles.Count - 2];
                var last = candles.Last();

                if (beforeLast.OpeningPrice - beforeLast.ClosingPrice > SIGNIFICANCE_LIMIT && last.OpeningPrice > last.ClosingPrice)
                    return "Decent rise (TODO!!!)";
            }
            #endregion


            //TODO: recognition of all the other patterns
            
            return null;
        }


        #region private helpers

        /// <summary>There's a dump on market, best time to SELL.</summary>
        private static bool runningSelloff(List<TradeResponse> tradeHistory)
        {
            //There's lot of activity in recent time and price is falling
            const double PRICE_DIFF = 3 * 0.001;  //0.3%

            var endTime = tradeHistory.Last().DateTyped;
            var startTime = endTime.AddSeconds(-90);    //Trades of past 90 seconds
            var pastTrades = tradeHistory.Where(trade => trade.DateTyped >= startTime).ToList();
            if (pastTrades.Count < 10)        //TODO: tune up
                return false;

            var startPrice = pastTrades.First().price;
            var endPrice = pastTrades.Last().price;
            return (startPrice > endPrice && startPrice - endPrice >= startPrice * PRICE_DIFF);
        }


        /* Steps:
         * - group past trades by interval (3m?)
         * - count weighted mean price (use value from previous interval if this has no trades)
         */
        private Dictionary<DateTime, double> preprocessTradeData(IEnumerable<TradeResponse> tradeHistory)
        {
            //Group trades by interval
            var tradesInItervals = new Dictionary<DateTime, List<TradeResponse>>();   //KEY=interval start

            foreach (var trade in tradeHistory)
            {
                var interval = getIntervalStart(trade, GROUP_INTERVAL);
                if (!tradesInItervals.ContainsKey(interval))
                    tradesInItervals.Add(interval, new List<TradeResponse>());

                tradesInItervals[interval].Add(trade);
            }

            //Count weighted mean price
            var grouppedData = new Dictionary<DateTime, double>();      //VALUE=weighted mean price
            foreach (var interval in tradesInItervals)
            {
                double weightedSum = 0.0;
                double totalAmount = 0.0;

                foreach (var trade in interval.Value)
                {
                    weightedSum += trade.amount * trade.price;
                    totalAmount += trade.amount;
                }

                grouppedData.Add(interval.Key, weightedSum/totalAmount);
            }

            return grouppedData;
        }

        /*TODO: private*/internal List<candle> getCandleStickData(IEnumerable<TradeResponse> tradeHistory, TimeSpan intervalLength)
        {
            var candles = new Dictionary<DateTime, candle>();        //KEY=interval start
            int minutes = (int)intervalLength.TotalMinutes;

            foreach (var trade in tradeHistory)
            {
                var interval = getIntervalStart(trade, minutes);
                if (!candles.ContainsKey(interval))
                    candles.Add(interval, new candle(interval, intervalLength));

                candles[interval].Trades.Add(trade);
            }

            var times = candles.Keys.ToList();
            times.Sort();
            var result = new List<candle>();
            foreach (var time in times)
                result.Add(candles[time]);

            return result;
        }

        private static DateTime getIntervalStart(TradeResponse trade, int intervalMinutes)
        {
            var start = trade.DateTyped;
            var extra = start.Minute % intervalMinutes;
            return new DateTime(start.Year, start.Month, start.Day, start.Hour, start.Minute - extra, 0);
        }
        #endregion


        /*TODO: private*/internal struct candle
        {
            internal readonly List<TradeResponse> Trades;
            internal readonly DateTime IntervalStart;
            internal readonly TimeSpan IntervalLength;
            //TODO: weighted average price?

            private double _openPrice;
            internal double OpeningPrice
            {
                //NOTE: quietly relies that will be called when Trades is fully filled and will not change
                get
                {
                    if (_openPrice < 0.0)
                    {
                        DateTime first = DateTime.MaxValue;
                        foreach (var trade in Trades)
                        {
                            if (trade.DateTyped < first)
                            {
                                first = trade.DateTyped;
                                _openPrice = trade.price;
                            }
                        }
                    }
                    return _openPrice;
                }
            }

            private double _closePrice;

            internal double ClosingPrice
            {
                //NOTE: quietly relies that will be called when Trades is fully filled and will not change
                get
                {
                    if (_closePrice < 0.0)
                    {
                        var last = DateTime.MinValue;
                        foreach (var trade in Trades)
                        {
                            if (trade.DateTyped > last)
                            {
                                last = trade.DateTyped;
                                _closePrice = trade.price;
                            }
                        }
                    }
                    return _closePrice;
                }
            }

            internal candle(DateTime start, TimeSpan length)
            {
                Trades = new List<TradeResponse>();
                IntervalStart = start;
                IntervalLength = length;
                _openPrice = -1.0;
                _closePrice = -1.0;
            }

            public override string ToString()
            {
                return "OPEN=" + OpeningPrice + " (" + IntervalStart.ToString("hh:mm:ss") + ") ### CLOSE=" +
                       ClosingPrice + " (" + (IntervalStart.Add(IntervalLength)).ToString("hh:mm:ss");
            }
        }
    }
}
