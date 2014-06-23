using System;
using System.Collections.Generic;
using System.Linq;
using BtcChinaBot.Business;


namespace BtcChinaBot
{
    /// <summary>
    /// Estimation engine for future market trend based on past trades.
    /// </summary>
    internal class MarketTrend
    {
        //When price moved <= 2.0 CNY in one candle, it's not rise/fall
        private const double PRICE_SIGNIFICANCE_LIMIT = 2.0;
        //Average volume limit, to avoid price swings by single whale trade
        private const double AVG_VOLUME_LIMIT = 0.15;
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
        /// Returns NULL if market doesn't give enough SELL signals, description of SELL reason otherwise. Good for BEAR strategy.
        /// </summary>
        internal string ReasonToSell(List<TradeResponse> tradeHistory)
        {
            if (runningSelloff(tradeHistory))
                return "DUMP";
            if (volumeDeclineAfterPriceRise(tradeHistory))
                return "Volume and price decline after price rise";

            //TODO: recognition of all the other patterns
            
            return null;
        }

        /// <summary>
        /// Returns non-NULL description of BUY reason for bearish bot.
        /// </summary>
        internal string ReasonToBuyBack(List<TradeResponse> tradeHistory)
        {
            if (declineIsSlowing(tradeHistory))
                return "Decline slowing down";
            if (isRising(tradeHistory))
                return "Significant rise";
            //TODO: all the strategies
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

        /// <summary>Price and volume are going down after price rise.</summary>
        private bool volumeDeclineAfterPriceRise(List<TradeResponse> tradeHistory)
        {
            const int MIN_CANDLES = 7;
            var candles = getCandleStickData(tradeHistory, new TimeSpan(0, GROUP_INTERVAL, 0));

            if (candles.Count < MIN_CANDLES)
                return false;
            candles = candles.TakeLast(MIN_CANDLES).ToList();

            const double SIGNIFICANT_RISE = 3.0;       //Minimum rise of a candle to be considered significant. TODO: no magic here!

            //First 4 candles price rise
            double previousePrice = -1.0;
            for (var c = 0; c < 4; c++)
            {
                if (candles[c].ClosingPrice > candles[c].OpeningPrice + PRICE_SIGNIFICANCE_LIMIT && //rise
                    candles[c].ClosingPrice > previousePrice + SIGNIFICANT_RISE)
                {
                    previousePrice = candles[c].ClosingPrice;
                }
                else return false;  //Not rise
            }

            //5th candle
            if (candles[4].ClosingPrice + PRICE_SIGNIFICANCE_LIMIT > candles[4].OpeningPrice || candles[4].BtcVolume > candles[3].BtcVolume)
                return false;   //Not fall or volume still rising
            //6th candle (before last)
            if (candles[5].ClosingPrice + PRICE_SIGNIFICANCE_LIMIT > candles[5].OpeningPrice || candles[5].BtcVolume > candles[4].BtcVolume)
                return false;   //Not fall or volume still rising

            //Present candle, most recent trade decides
            return candles[6].ClosingPrice < candles[5].ClosingPrice;
        }

        private bool declineIsSlowing(List<TradeResponse> tradeHistory)
        {
            return false;
            //TODO
        }

        private bool isRising(List<TradeResponse> tradeHistory)
        {
            const int MIN_CANDLES = 2;
            var candles = getCandleStickData(tradeHistory, new TimeSpan(0, GROUP_INTERVAL, 0));

            if (candles.Count < MIN_CANDLES)
                return false;
            candles = candles.TakeLast(MIN_CANDLES).ToList();

            //Previous candle is simply rise and latest price was rise too //TODO: maybe something more sofisticated
            return candles[0].ClosingPrice > candles[0].OpeningPrice + PRICE_SIGNIFICANCE_LIMIT &&
                   candles[0].ClosingPrice < candles[1].ClosingPrice;
        }


        /*TODO: private*/internal static List<candle> getCandleStickData(IEnumerable<TradeResponse> tradeHistory, TimeSpan intervalLength)
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

            private double _btcVolume;
            internal double BtcVolume
            {
                get
                {
                    if (_btcVolume < 0.0)
                    {
                        _btcVolume = 0.0;
                        foreach (var trade in Trades)
                            _btcVolume += trade.amount;
                    }

                    return _btcVolume;
                }
            }

            private double _avgVolume;
            /// <summary>Mean BTC volume per trade.</summary>
            internal double AverageVolume
            {
                get
                {
                    if (_avgVolume < 0.0)
                    {
                        //Group by time
                        var volumes = new Dictionary<string, double>();
                        foreach (var trade in Trades)
                        {
                            if (!volumes.ContainsKey(trade.date))
                                volumes.Add(trade.date, 0.0);
                            volumes[trade.date] += trade.amount;
                        }

                        _avgVolume = volumes.Values.Sum() / volumes.Count;
                    }

                    return _avgVolume;
                }
            }

            internal candle(DateTime start, TimeSpan length)
            {
                Trades = new List<TradeResponse>();
                IntervalStart = start;
                IntervalLength = length;
                _openPrice = -1.0;
                _closePrice = -1.0;
                _btcVolume = -1.0;
                _avgVolume = -1.0;
            }

            public override string ToString()
            {
                return "OPEN=" + OpeningPrice + " (" + IntervalStart.ToString("hh:mm:ss") + ") ### CLOSE=" +
                       ClosingPrice + " (" + (IntervalStart.Add(IntervalLength)).ToString("hh:mm:ss") + ") ### AvgVolume=" + AverageVolume.ToString("0.00");
            }
        }
    }
}
