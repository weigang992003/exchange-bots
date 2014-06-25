using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Runtime.Serialization.Json;
using System.Text;
using Common;
using HuobiBot.Business;


namespace HuobiBot
{
    internal class HuobiRequestHelper
    {
        private const string MARKET_URL = "http://market.huobi.com/staticmarket/depth_btc_json.js";
        private const string TRADE_STATS_URL = "http://market.huobi.com/staticmarket/detail_btc_json.js";
        private const byte RETRY_COUNT = 5;
        private const int RETRY_DELAY = 750;

        private readonly Logger _logger;
        private readonly WebProxy _webProxy;


        internal HuobiRequestHelper(Logger logger)
        {
            _logger = logger;
            var proxyHost = Configuration.GetValue("proxyHost");
            var proxyPort = Configuration.GetValue("proxyPort");
            if (null != proxyHost && null != proxyPort)
            {
                _webProxy = new WebProxy(proxyHost, int.Parse(proxyPort));
                _webProxy.Credentials = CredentialCache.DefaultCredentials;
            }
        }


        internal MarketDepthResponse GetMarketDepth()
        {
            var client = new WebClient();

            if (null != _webProxy)
                client.Proxy = _webProxy;

            var data = client.DownloadString(MARKET_URL);
            return deserializeJSON<MarketDepthResponse>(data);
        }

        internal TradeStatisticsResponse GetTradeStatistics()
        {
            var client = new WebClient();

            if (null != _webProxy)
                client.Proxy = _webProxy;

            var data = client.DownloadString(TRADE_STATS_URL);
            var trades = deserializeJSON<TradeStatisticsResponse>(data);

            return trades;
        }



        //TODO: might be duplicate from BtcChinaRequestHelper, refactor
        private static T deserializeJSON<T>(string json)
        {
            using (var ms = new MemoryStream(Encoding.Unicode.GetBytes(json)))
            {
                var deserializer = new DataContractJsonSerializer(typeof(T));
                return (T)deserializer.ReadObject(ms);
            }
        }
    }
}
