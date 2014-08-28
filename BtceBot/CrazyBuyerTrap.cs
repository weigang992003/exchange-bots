using System.Threading;
using Common;


namespace BtceBot
{
    internal class CrazyBuyerTrap : TraderBase
    {
        private readonly BtceApi _requestor;

        //LTC amount to trade
        private readonly double _operativeAmount;
        private readonly double _minWallVolume;
        private readonly double _maxWallVolume;
        //Volumen of LTC necessary to accept our offer
        private double _volumeWall;
        //Minimum difference between SELL price and subsequent BUY price (so we have at least some profit). Note: fee is 0.2%
        private const double MIN_DIFFERENCE = 0.06;
        //Tolerance of SELL price (absolute value in USD). Usefull if possible price change is minor, to avoid frequent order updates.
        private const double MIN_PRICE_DELTA = 0.02;    //2 cents per LTC

        //Active SELL order ID
        private int _sellOrderId = -1;
        //Active SELL order amount
        private double _sellOrderAmount;
        //Active SELL order price
        private double _sellOrderPrice;

        //Active BUY order ID
        private int _buyOrderId = -1;
        //Active BUY order amount
        private double _buyOrderAmount;
        //Active BUY order price
        private double _buyOrderPrice;
        //The price at which we sold to crazy buyer
        private double _executedSellPrice = -1.0;


        public CrazyBuyerTrap(Logger logger) : base(logger)
        {
            _operativeAmount = double.Parse(Configuration.GetValue("operative_amount"));
            _minWallVolume = double.Parse(Configuration.GetValue("min_volume"));
            _maxWallVolume = double.Parse(Configuration.GetValue("max_volume"));
            log("BTC-E Crazy buyer trap trader initialized with operative={0}; MinWall={1}; MaxWall={2}", _operativeAmount, _minWallVolume, _maxWallVolume);
            _requestor = new BtceApi(logger);
        }


        protected override void Check()
        {
            var serverTime = _requestor.GetServerTime();
            var tradeHistory = _requestor.GetTradeHistory();
            _intervalMs = 5555;

            log("ServerTime = " + serverTime);
            
            log("Trade history: ");
            foreach (var trade in tradeHistory.trades)
                log((trade.Type == TradeType.BUY ? "Bought " : "Sold ") + trade.amount + " LTC for " + trade.price + " USD\t\t at " + trade.Time);

            log(new string('=', 84));

        }
    }
}
