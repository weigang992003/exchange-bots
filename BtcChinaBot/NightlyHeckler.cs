using System;
using Common;


namespace BtcChinaBot
{
    //TODO: give it a very carefull try
    //Fresh new idea: when the market is really peacefull, maintain 3 orders: one Heckler BUY order, one SELL order and one "real" BUY order.
    //                PHASE #1:
    //                The heckler BUYer is sort of atack against miscoded greedy bots. And it's the only active order most of the time.
    //                When it successfully pushes another bot to high BUY offer, cancel Heckler and create a SELL order with price and volume (or threshold)
    //                of the poor buyer.
    //                PHASE #3:
    //                Then maintain real BUY order with lower price than the SELL had.

    //Problem 1: There's no greedy bot to cope with. Generally I need some very clever greedy bot recognition. To find whether there is some and what's his limit.  Approach: sleep for some time(?)
    //Problem 2: Heckler accidentally bought all BTC. Approach: Try to SELL it asap, no profit necessary (keep it as lowest SELL above executed BUY price). Don't keep any other order during this.
    //Problem 3: Heckler accidentally bought part of BTC. Approach: Keep his strategy with remaining amount. Try to SELL the bought BTC asap (watch out not to SELL to self!!)
    internal class NightlyHeckler : ITrader
    {
        private const double HECKLER_OPERATIVE_AMOUNT = 0.1;
        private const int RETRY_PERIOD = 60000; //60000 ms = 1 minute ;-)


        public void StartTrading()
        {
            throw new NotImplementedException();
        }

        public void Kill()
        {
            throw new NotImplementedException();
        }


        /*
         *if (spread > MIN_SPREAD && market_is_peacefull)
         *  we can afford doing stunts: put best BUY order, watch what's happening. If no one tries to overcome us for some time, give up for now and try later.
         * 
         */
    }
}


/*
 * Cafe discussion: what happens if I place 15 BUY orders with difference 0.01 in price and volume 0.0001, all above present highest BUY offer?
 * TODO: try and watch
 */
