using Common;
using RippleBot.Business;


namespace RippleBot.ApiHelpers
{
    /// <summary>
    /// Cleanup item to address dangling offers (e.g. due to failed cancel, double creation etc)
    /// </summary>
    internal class OrderZombie
    {
        private readonly double _price;
        private readonly double _amount;
        private readonly string _currCode;

        internal OrderZombie(double price, double amount, string currencyCode)
        {
            _price = price;
            _amount = amount;
            _currCode = currencyCode;
        }

        internal bool IsMatch(Offer offer)
        {
            const double precise = 0.00000001;

            return offer.Type == TradeType.BUY &&
                   offer.Currency == _currCode &&
                   offer.AmountXrp.eq(_amount, precise) &&
                   offer.Price.eq(_price, precise);
        }
    }
}
