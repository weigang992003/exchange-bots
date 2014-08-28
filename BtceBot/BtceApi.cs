using System;
using System.Collections.Generic;
using System.Data;
using System.Net;
using System.Threading;
using BtceBot.Business;
using Common;


namespace BtceBot
{
    internal class BtceApi
    {
        private const string DATA_BASE_URL = "https://btc-e.com/api/2/ltc_usd/";
        private const byte RETRY_COUNT = 6;
        private const int RETRY_DELAY = 1000;

        private readonly Logger _logger;
        private readonly long _nonceOffset;
        private readonly WebProxy _webProxy;


        public BtceApi(Logger logger)
        {
            _logger = logger;
            var proxyHost = Configuration.GetValue("proxyHost");
            var proxyPort = Configuration.GetValue("proxyPort");
            if (null != proxyHost && null != proxyPort)
            {
                _webProxy = new WebProxy(proxyHost, int.Parse(proxyPort));
                _webProxy.Credentials = CredentialCache.DefaultCredentials;
            }

            var nonceOffset = Configuration.GetValue("nonce_offset");
            if (!String.IsNullOrEmpty(nonceOffset))
                _nonceOffset = long.Parse(nonceOffset);
        }


        internal DateTime GetServerTime()
        {
            var data = sendGetRequest(DATA_BASE_URL + "ticker");
            return Helpers.DeserializeJSON<TickerResponse>(data).ticker.ServerTime;
        }



        internal TradeHistory GetTradeHistory(/*TODO: do I need this?   DateTime? since = null*/)
        {
            var data = sendGetRequest(DATA_BASE_URL + "trades");
            data = "{ \"trades\" : " + data + " }";
            var trades = Helpers.DeserializeJSON<TradeHistory>(data);   //todo: will not be so easy...
            return trades;
        }





        private string sendGetRequest(string url)
        {
            WebException exc = null;
            var delay = 0;
            for (int i = 1; i <= RETRY_COUNT; i++)
            {
                var client = new WebClient2(_logger, 20000);

                if (null != _webProxy)
                    client.Proxy = _webProxy;

                delay += RETRY_DELAY;
                try
                {
                    return client.DownloadString(url);
                }
                catch (WebException we)
                {
                    var text = String.Format("(ATTEMPT {0}/{1}) Web request failed with exception={2}; status={3}", i, RETRY_COUNT, we.Message, we.Status);
                    _logger.AppendMessage(text, true, ConsoleColor.Yellow);
                    exc = we;
                    Thread.Sleep(delay);
                }
            }

            throw new Exception(String.Format("Web request failed {0} times in a row with error '{1}'. Giving up.", RETRY_COUNT, exc.Message));
        }
    }
}