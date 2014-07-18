using Common;
using RippleBot.Business;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;


namespace RippleBot
{
    /// <summary>Communication wrapper for local Ripple REST API</summary>
    internal class RippleRestApi            //TODO: once, when you have totally nothing to do, create API for WebSockets
    {
        private const string BASE_URL = "http://localhost:5990/v1";
        private const byte RETRY_COUNT = 6;
        private const int RETRY_DELAY = 1000;

        private readonly Logger _logger;
        private readonly WebProxy _webProxy;


        public RippleRestApi(Logger logger)
        {
            _logger = logger;
        }

        internal BalancesResponse GetAccountBalance(string rippleAddress)
        {
            var data = sendGetRequest(String.Format("{0}/accounts/{1}/balances", BASE_URL, rippleAddress));
            return Helpers.DeserializeJSON<BalancesResponse>(data);
        }

        #region private helpers

        private string sendGetRequest(string url)
        {
            var client = new WebClient();

            if (null != _webProxy)
                client.Proxy = _webProxy;

            WebException exc = null;
            for (int i = 1; i <= RETRY_COUNT; i++)
            {
                try
                {
                    return client.DownloadString(url);
                }
                catch (WebException we)
                {
                    var text = String.Format("(ATTEMPT {0}/{1}) Web request failed with exception={2}; status={3}", i, RETRY_COUNT, we.Message, we.Status);
                    _logger.AppendMessage(text, true, ConsoleColor.Yellow);
                    exc = we;
                    Thread.Sleep(RETRY_DELAY);
                }
            }

            throw new Exception(String.Format("Web request failed {0} times in a row with error '{1}'. Giving up.", RETRY_COUNT, exc.Message));
        }
        #endregion
    }
}
