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
        //When price moved <= 1.5 CNY in one candle, it's not rise/fall
        private const double PRICE_SIGNIFICANCE_LIMIT = 1.5;
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
            if (dump(tradeHistory))
                return "DUMP";
            if (volumeDeclineAfterPriceRise(candles))
                return "Volume and price decline after price rise";

            //TODO: recognition of all the other patterns
            
            return null;
        }

        /// <summary>
        /// Returns non-NULL description of BUY reason for bearish bot.
        /// </summary>
        internal string ReasonToBuyBack(List<Candle> candles, TradeStatisticsResponse tradeHistory)
        {
            if (pump(tradeHistory))
                return "PUMP";
            if (isRising(candles))
                return "Significant rise";

            return null;
        }


        #region private helpers

        /// <summary>There's a dump on market, best time to SELL.</summary>
        private static bool dump(TradeStatisticsResponse tradeHistory)
        {
            //TODO: do lot of vain runs, watch carefully and tune the magic numbers here


            //Activity criterion: if mean time between recent trades is 5 seconds or less, it's high activity
            const int MAX_SPEED = 5;

            var uniqueTradesCount = 1;
            var upTrades = 0;
            var downTrades = 0;
            var lastTime = tradeHistory.trades.Last().TimeTyped;
            var lastPrice = tradeHistory.trades.Last().price;
            double totalSeconds = 0.0;

            for (int i = tradeHistory.trades.Count-1; i >= 0; i--)
            {
                var trade = tradeHistory.trades[i];

                if (trade.TimeTyped != lastTime)
                {
                    totalSeconds += (trade.TimeTyped - lastTime).TotalSeconds;
                    if (trade.price < lastPrice)
                        downTrades++;
                    else if (trade.price > lastPrice)
                        upTrades++;
                    else if (TradeType.SELL == trade.Type)
                        downTrades++;
                    else
                        upTrades++;

                    uniqueTradesCount++;
                    lastTime = trade.TimeTyped;
                    lastPrice = trade.price;
                }
            }

            double avgSeconds = totalSeconds/uniqueTradesCount;
            if (avgSeconds > MAX_SPEED)
                return false;

            //Trend criterion: at least 75% of recent trades are down trend, means lower price than previous trade
            const double MIN_DOWN_TRADES = 0.75;
            double downPercentage = (double)downTrades / (upTrades + downTrades);
            if (downPercentage < MIN_DOWN_TRADES)
                return false;

            //Price criterion: price has fallen enough, that is at least 6 CNY per BTC
            const double MIN_PRICE_DIFF = 6.0;

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
                    rises++;
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

        private bool pump(TradeStatisticsResponse tradeHistory)
        {
            //TODO: do lot of vain runs, watch carefully and tune the magic numbers here


            //Activity criterion: if mean time between recent trades is 5 seconds or less, it's high activity
            const int MAX_SPEED = 5;

            var uniqueTradesCount = 1;
            var upTrades = 0;
            var downTrades = 0;
            var lastTime = tradeHistory.trades.Last().TimeTyped;
            var lastPrice = tradeHistory.trades.Last().price;
            double totalSeconds = 0.0;

            for (int i = tradeHistory.trades.Count - 1; i >= 0; i--)
            {
                var trade = tradeHistory.trades[i];

                if (trade.TimeTyped != lastTime)
                {
                    totalSeconds += (trade.TimeTyped - lastTime).TotalSeconds;
                    if (trade.price < lastPrice)
                        downTrades++;
                    else if (trade.price > lastPrice)
                        upTrades++;
                    else if (TradeType.BUY == trade.Type)
                        upTrades++;
                    else
                        downTrades++;

                    uniqueTradesCount++;
                    lastTime = trade.TimeTyped;
                    lastPrice = trade.price;
                }
            }

            double avgSeconds = totalSeconds / uniqueTradesCount;
            if (avgSeconds > MAX_SPEED)
                return false;

            //Trend criterion: at least 75% of recent trades are up trend, means higher price than previous trade
            const double MIN_UP_TRADES = 0.75;
            double upPercentage = (double)upTrades / (upTrades + downTrades);
            if (upPercentage < MIN_UP_TRADES)
                return false;

            //Price criterion: price has fallen enough, that is at least 5 CNY per BTC
            const double MIN_PRICE_DIFF = 5.0;

            var startPrice = tradeHistory.trades.First().price;
            var endPrice = tradeHistory.trades.Last().price;
            return (startPrice < endPrice && endPrice - startPrice >= MIN_PRICE_DIFF);
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
    }
}
