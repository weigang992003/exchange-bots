using System;
using System.Collections.Generic;
using System.Linq;
using HuobiBot.Business;
using Common;


namespace HuobiBot
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
        internal float GetSentiment(TradeStatisticsResponse tradeHistory)
        {
            throw new NotImplementedException("TODO");
        }

        /// <summary>
        /// Returns NULL if market doesn't give enough SELL signals, description of SELL reason otherwise. Good for BEAR strategy.
        /// </summary>
        internal string ReasonToSell(List<Candle> candles, TradeStatisticsResponse tradeHistory)
        {
            if (runningSelloff(tradeHistory))
                return "DUMP";
            if (volumeDeclineAfterPriceRise(candles))
                return "Volume and price decline after price rise";

            //TODO: recognition of all the other patterns
            
            return null;
        }

        /// <summary>
        /// Returns non-NULL description of BUY reason for bearish bot.
        /// </summary>
        internal string ReasonToBuyBack(List<Candle> tradeHistory)
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
        private static bool runningSelloff(TradeStatisticsResponse tradeHistory)
        {
            //TODO: do lot of vain runs, watch carefully and tune the magic numbers here


            //Activity criterion: if mean time between recent trades is 5 seconds or less, it's high activity
            const int MAX_SPEED = 5;

            var uniqueTradesCount = 1;
            var upTrades = 0;
            var downTrades = 0;
            var lastTime = tradeHistory.trades.First().TimeTyped;
            var lastPrice = tradeHistory.trades.First().price;
            double totalSeconds = 0.0;
            foreach (var trade in tradeHistory.trades)
            {
                if (trade.TimeTyped != lastTime)
                {
                    totalSeconds += (trade.TimeTyped - lastTime).TotalSeconds;
                    if (trade.price < lastPrice)
                        downTrades++;
                    else upTrades++;

                    uniqueTradesCount++;
                    lastTime = trade.TimeTyped;
                    lastPrice = trade.price;
                }
            }

            double avgSeconds = totalSeconds/uniqueTradesCount;
            if (avgSeconds > MAX_SPEED)
                return false;

            //Trend criterion: at least 80% of recent trades are down trend, means lower price than previous trade
            const double MIN_DOWN_TRADES = 0.8;
            double downPercentage = (double)downTrades / (upTrades + downTrades);
            if (downPercentage < MIN_DOWN_TRADES)
                return false;

            //Price criterion: price has fallen enough, that is at least 8 CNY per BTC
            const double MIN_PRICE_DIFF = 8.0;

            var startPrice = tradeHistory.trades.First().price;
            var endPrice = tradeHistory.trades.Last().price;
            return (startPrice > endPrice && startPrice - endPrice >= MIN_PRICE_DIFF);
        }

        /// <summary>Price and volume are going down after price rise.</summary>
        private bool volumeDeclineAfterPriceRise(List<Candle> candles)
        {
            const int MIN_CANDLES = 7;

            //Not enough data, can't reliably say
            if (candles.Count < MIN_CANDLES)
                return false;
            candles = candles.TakeLast(MIN_CANDLES).ToList();

            //Minimum rise of a candle to be considered significant. TODO: no magic here!
            const double SIGNIFICANT_RISE = 3.0;

            //In first 4 candles there are at least 3 significant rises and no significant fall
            const int MIN_RISE_CANDLES = 3;
            int rises = 0;
            double prevClosePrice = -1.0;           //(BUG?) hmm, that makes first candle automatic rise
            for (var c = 0; c < 4; c++)
            {
                if (candles[c].ClosingPrice > candles[c].OpeningPrice + PRICE_SIGNIFICANCE_LIMIT && //rise
                    candles[c].ClosingPrice > prevClosePrice + SIGNIFICANT_RISE)
                {
                    prevClosePrice = candles[c].ClosingPrice;
                }
                else if (candles[c].ClosingPrice < candles[c].OpeningPrice - PRICE_SIGNIFICANCE_LIMIT)
                    return false; //fall, good bye
            }
            if (rises < MIN_RISE_CANDLES)
                return false;

            //5th candle
            if (candles[4].ClosingPrice + PRICE_SIGNIFICANCE_LIMIT > candles[4].OpeningPrice || candles[4].Volume > candles[3].Volume)
                return false;   //Not fall or volume still rising
            //6th candle (before last)
            if (candles[5].ClosingPrice + PRICE_SIGNIFICANCE_LIMIT > candles[5].OpeningPrice || candles[5].Volume > candles[4].Volume)
                return false;   //Not fall or volume still rising

            //Present candle, most recent trade decides
            return candles[6].ClosingPrice < candles[5].ClosingPrice;
        }

        private bool declineIsSlowing(List<Candle> tradeHistory)
        {
            return false;
            //TODO
        }

        private bool isRising(List<Candle> candles)
        {
            const int MIN_CANDLES = 3;

            if (candles.Count < MIN_CANDLES)
                return false;
            candles = candles.TakeLast(MIN_CANDLES).ToList();

            //Present candle is significant rise
            if (candles.Last().ClosingPrice > candles.Last().OpeningPrice + 3.0*PRICE_SIGNIFICANCE_LIMIT)
                return true;

            //Previous 2 candles were rises and latest price was rise too //TODO: maybe something more sofisticated
            return candles[0].ClosingPrice > candles[0].OpeningPrice + PRICE_SIGNIFICANCE_LIMIT &&
                   candles[1].ClosingPrice > candles[1].OpeningPrice + PRICE_SIGNIFICANCE_LIMIT &&
                   candles[1].ClosingPrice < candles.Last().ClosingPrice;   //Most recent price decides
        }

        #endregion


        /*internal struct candle
        {
            internal readonly TradeStatisticsResponse Trades;
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
                            if (trade.TimeTyped < first)
                            {
                                first = trade.TimeTyped;
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
                            if (trade.TimeTyped > last)
                            {
                                last = trade.TimeTyped;
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
                return String.Format("OPEN={0:0.00} ({1:hh:mm:ss})  |  CLOSE={2:0.00} ({3:hh:mm:ss})  |  SumVolume={4:0.00}  |  AvgVolume={5:0.00}",
                                     OpeningPrice, IntervalStart, ClosingPrice, IntervalStart.Add(IntervalLength), BtcVolume, AverageVolume);
            }
        }*/
    }
}
