using Common;
using RippleBot.Business;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using WebSocket4Net;


namespace RippleBot
{
    internal class RippleWebSocketApi : IDisposable
    {
        private const string BASE_URL = "wss://s-west.ripple.com:443";
        private const byte RETRY_COUNT = 6;
        private const int RETRY_DELAY = 1000;

        private readonly string _walletAddress = "rpMV1zYgR5P6YWA2JSXDPcbsbqivkooKVY";      //TODO: Config.AccessKey

        private readonly Logger _logger;
//TODO        private readonly ??? _webProxy;
        private bool _open;

        private readonly WebSocket _webSocket;

        private string _lastResponse;


        internal RippleWebSocketApi(Logger logger)
        {
            _logger = logger;
            _webSocket = new WebSocket(BASE_URL);

            _webSocket.Opened += websocket_Opened;
            _webSocket.Error += websocket_Error;
            _webSocket.Closed += websocket_Closed;
            _webSocket.MessageReceived += websocket_MessageReceived;            
        }

        internal void Init()
        {
            _webSocket.Open();

            while (!_open)
                Thread.Sleep(50);
        }

        internal Offer GetOrderInfo(int orderId)
        {
            var command = "{" + String.Format("\"command\":\"account_offers\",\"id\":1,\"account\":\"{0}\"", _walletAddress) + "}";       //TODO: serialize from class

            var data = sendToRippleNet(command);
            var offerList = Helpers.DeserializeJSON<OffersResponse>(data);
            return offerList.result.offers.FirstOrDefault(o => o.seq == orderId);
        }

        internal void Close()
        {
            if (_open)
                _webSocket.Close();
            _open = false;
        }

        public void Dispose()
        {
            Close();
        }

        #region private helpers

        private string sendToRippleNet(string commandData)
        {
            _webSocket.Send(commandData);

            if (!_open)
                throw new InvalidOperationException("WebSocket not open");

            while (null == _lastResponse)
                Thread.Sleep(50);

            var ret = _lastResponse;
            _lastResponse = ret;
            return ret;
        }

        private void websocket_MessageReceived(object sender, MessageReceivedEventArgs mrea)
        {
            _lastResponse = mrea.Message;
        }

        private void websocket_Opened(object sender, EventArgs e)
        {
            _logger.AppendMessage("WebSocket connection established", true, ConsoleColor.Yellow);
            _open = true;
        }

        private void websocket_Error(object sender, SuperSocket.ClientEngine.ErrorEventArgs eea)
        {
            _logger.AppendMessage("WebSocket error: " + eea.Exception.Message, true, ConsoleColor.Yellow);
        }        

        private void websocket_Closed(object sender, EventArgs e)
        {
            _logger.AppendMessage("WebSocket connection was closed", true, ConsoleColor.Yellow);
            _open = false;
        }
        #endregion
    }
}
