﻿using System;
using System.Collections.Generic;
﻿using System.IO;
﻿using System.Net;
using System.Net.Sockets;
using System.Text;
using SuperSocket.ClientEngine;


namespace RippleBot
{
    public class Socks5Connector : ProxyConnectorBase
    {
        enum SocksState
        {
            NotAuthenticated,
            Authenticating,
            Authenticated,
            FoundLength,
            Connected
        }

        class SocksContext
        {
            public Socket Socket { get; set; }

            public SocksState State { get; set; }

            public EndPoint TargetEndPoint { get; set; }

            public List<byte> ReceivedData { get; set; }

            public int ExpectedLength { get; set; }
        }

        private ArraySegment<byte> m_UserNameAuthenRequest;

        private static byte[] m_AuthenHandshake = new byte[] { 0x05, 0x02, 0x00, 0x02 };

#if SILVERLIGHT && !WINDOWS_PHONE
        public Socks5Connector(EndPoint proxyEndPoint, SocketClientAccessPolicyProtocol clientAccessPolicyProtocol)
            : base(proxyEndPoint, clientAccessPolicyProtocol)
        {

        }
#else
        public Socks5Connector(EndPoint proxyEndPoint)
            : base(proxyEndPoint)
        {

        }
#endif

#if SILVERLIGHT && !WINDOWS_PHONE
        public Socks5Connector(EndPoint proxyEndPoint, SocketClientAccessPolicyProtocol clientAccessPolicyProtocol, string username, string password)
            : base(proxyEndPoint, clientAccessPolicyProtocol)
#else
        public Socks5Connector(EndPoint proxyEndPoint, string username, string password)
            : base(proxyEndPoint)
#endif
        {
            if (string.IsNullOrEmpty(username))
                throw new ArgumentNullException("username");

            var buffer = new byte[3 + ASCIIEncoding.GetMaxByteCount(username.Length) + (string.IsNullOrEmpty(password) ? 0 : ASCIIEncoding.GetMaxByteCount(password.Length))];
            var actualLength = 0;

            buffer[0] = 0x05;
            var len = ASCIIEncoding.GetBytes(username, 0, username.Length, buffer, 2);

            if (len > 255)
                throw new ArgumentException("the length of username cannot exceed 255", "username");

            buffer[1] = (byte)len;

            actualLength = len + 2;

            if (!string.IsNullOrEmpty(password))
            {
                len = ASCIIEncoding.GetBytes(password, 0, password.Length, buffer, actualLength + 1);

                if (len > 255)
                    throw new ArgumentException("the length of password cannot exceed 255", "password");

                buffer[actualLength] = (byte)len;
                actualLength += len + 1;
            }
            else
            {
                buffer[actualLength] = 0x00;
                actualLength++;
            }

            m_UserNameAuthenRequest = new ArraySegment<byte>(buffer, 0, actualLength);
        }

        public override void Connect(EndPoint remoteEndPoint)
        {
            if (remoteEndPoint == null)
                throw new ArgumentNullException("remoteEndPoint");

            if (!(remoteEndPoint is IPEndPoint || remoteEndPoint is DnsEndPoint))
                throw new ArgumentException("remoteEndPoint must be IPEndPoint or DnsEndPoint", "remoteEndPoint");


            try
            {
#if SILVERLIGHT && !WINDOWS_PHONE
                ProxyEndPoint.ConnectAsync(ClientAccessPolicyProtocol, ProcessConnect, remoteEndPoint);
#else
                ProxyEndPoint.ConnectAsync(ProcessConnect, remoteEndPoint);
#endif
            }
            catch (Exception e)
            {
                OnException(new Exception("Failed to connect proxy server", e));
            }
        }

        protected override void ProcessConnect(Socket socket, object targetEndPoint, SocketAsyncEventArgs e)
        {
            if (e != null)
            {
                if (!ValidateAsyncResult(e))
                    return;
            }

            if (socket == null)
            {
                OnException(new SocketException((int)SocketError.ConnectionAborted));
                return;
            }

            if (e == null)
                e = new SocketAsyncEventArgs();

            e.UserToken = new SocksContext { TargetEndPoint = (EndPoint)targetEndPoint, Socket = socket, State = SocksState.NotAuthenticated };
            e.Completed += new EventHandler<SocketAsyncEventArgs>(AsyncEventArgsCompleted);

            e.SetBuffer(m_AuthenHandshake, 0, m_AuthenHandshake.Length);

            StartSend(socket, e);
        }

        protected override void ProcessSend(SocketAsyncEventArgs e)
        {
            if (!ValidateAsyncResult(e))
                return;

            var context = e.UserToken as SocksContext;

            if (context.State == SocksState.NotAuthenticated)
            {
                e.SetBuffer(0, 2);
                StartReceive(context.Socket, e);
            }
            else if (context.State == SocksState.Authenticating)
            {
                e.SetBuffer(0, 2);
                StartReceive(context.Socket, e);
            }
            else
            {
                e.SetBuffer(0, e.Buffer.Length);
                StartReceive(context.Socket, e);
            }
        }

        private bool ProcessAuthenticationResponse(Socket socket, SocketAsyncEventArgs e)
        {
            int total = e.BytesTransferred + e.Offset;

            if (total < 2)
            {
                e.SetBuffer(total, 2 - total);
                StartReceive(socket, e);
                return false;
            }
            else if (total > 2)
            {
                OnException("received length exceeded");
                return false;
            }

            if (e.Buffer[0] != 0x05)
            {
                OnException("invalid protocol version");
                return false;
            }

            return true;
        }

        protected override void ProcessReceive(SocketAsyncEventArgs e)
        {
            if (!ValidateAsyncResult(e))
                return;

            var context = (SocksContext)e.UserToken;

            if (context.State == SocksState.NotAuthenticated)
            {
                if (!ProcessAuthenticationResponse(context.Socket, e))
                    return;

                var method = e.Buffer[1];

                if (method == 0x00)
                {
                    context.State = SocksState.Authenticated;
                    SendHandshake(e);
                    return;
                }
                else if (method == 0x02)
                {
                    context.State = SocksState.Authenticating;
                    AutheticateWithUserNamePassword(e);
                    return;
                }
                else if (method == 0xff)
                {
                    OnException("no acceptable methods were offered");
                    return;
                }
                else
                {
                    OnException("protocol error");
                    return;
                }
            }
            else if (context.State == SocksState.Authenticating)
            {
                if (!ProcessAuthenticationResponse(context.Socket, e))
                    return;

                var method = e.Buffer[1];

                if (method == 0x00)
                {
                    context.State = SocksState.Authenticated;
                    SendHandshake(e);
                    return;
                }
                else
                {
                    OnException("authentication failure");
                    return;
                }
            }
            else
            {
                byte[] data = new byte[e.BytesTransferred];
                Buffer.BlockCopy(e.Buffer, e.Offset, data, 0, e.BytesTransferred);

                context.ReceivedData.AddRange(data);

                if (context.ExpectedLength > context.ReceivedData.Count)
                {
                    StartReceive(context.Socket, e);
                    return;
                }
                else
                {
                    if (context.State != SocksState.FoundLength)
                    {
                        var addressType = context.ReceivedData[3];
                        int expectedLength;

                        if (addressType == 0x01)
                        {
                            expectedLength = 10;
                        }
                        else if (addressType == 0x03)
                        {
                            expectedLength = 4 + 1 + 2 + (int)context.ReceivedData[4];
                        }
                        else
                        {
                            expectedLength = 22;
                        }

                        if (context.ReceivedData.Count < expectedLength)
                        {
                            context.ExpectedLength = expectedLength;
                            StartReceive(context.Socket, e);
                            return;
                        }
                        else if (context.ReceivedData.Count > expectedLength)
                        {
                            OnException("response length exceeded");
                            return;
                        }
                        else
                        {
                            OnGetFullResponse(context);
                            return;
                        }
                    }
                    else
                    {
                        if (context.ReceivedData.Count > context.ExpectedLength)
                        {
                            OnException("response length exceeded");
                            return;
                        }

                        OnGetFullResponse(context);
                        return;
                    }
                }
            }
        }

        private void OnGetFullResponse(SocksContext context)
        {
            var response = context.ReceivedData;

            if (response[0] != 0x05)
            {
                OnException("invalid protocol version");
                return;
            }

            var status = response[1];

            if (status == 0x00)
            {
                OnCompleted(new ProxyEventArgs(context.Socket));
                return;
            }

            //0x01 = general failure
            //0x02 = connection not allowed by ruleset
            //0x03 = network unreachable
            //0x04 = host unreachable
            //0x05 = connection refused by destination host
            //0x06 = TTL expired
            //0x07 = command not supported / protocol error
            //0x08 = address type not supported

            string message = string.Empty;

            switch (status)
            {
                case (0x02):
                    message = "connection not allowed by ruleset";
                    break;

                case (0x03):
                    message = "network unreachable";
                    break;

                case (0x04):
                    message = "host unreachable";
                    break;

                case (0x05):
                    message = "connection refused by destination host";
                    break;

                case (0x06):
                    message = "TTL expired";
                    break;

                case (0x07):
                    message = "command not supported / protocol error";
                    break;

                case (0x08):
                    message = "address type not supported";
                    break;

                default:
                    message = "general failure";
                    break;
            }

            OnException(message);
        }

        private void SendHandshake(SocketAsyncEventArgs e)
        {
            var context = e.UserToken as SocksContext;

            var targetEndPoint = context.TargetEndPoint;

            byte[] buffer;
            int actualLength;
            int port = 0;

            if (targetEndPoint is IPEndPoint)
            {
                var endPoint = targetEndPoint as IPEndPoint;
                port = endPoint.Port;

                if (endPoint.AddressFamily == AddressFamily.InterNetwork)
                {
                    buffer = new byte[10];
                    buffer[3] = 0x01;

                    Buffer.BlockCopy(endPoint.Address.GetAddressBytes(), 0, buffer, 4, 4);
                }
                else if (endPoint.AddressFamily == AddressFamily.InterNetworkV6)
                {
                    buffer = new byte[22];
                    buffer[3] = 0x04;

                    Buffer.BlockCopy(endPoint.Address.GetAddressBytes(), 0, buffer, 4, 16);
                }
                else
                {
                    OnException("unknown address family");
                    return;
                }

                actualLength = buffer.Length;
            }
            else
            {
                var endPoint = targetEndPoint as DnsEndPoint;

                port = endPoint.Port;

                var maxLen = 7 + ASCIIEncoding.GetMaxByteCount(endPoint.Host.Length);
                buffer = new byte[maxLen];

                buffer[3] = 0x03;

                actualLength = 5;
                actualLength += ASCIIEncoding.GetBytes(endPoint.Host, 0, endPoint.Host.Length, buffer, actualLength);
                actualLength += 2;
            }

            buffer[0] = 0x05;
            buffer[1] = 0x01;
            buffer[2] = 0x00;

            buffer[actualLength - 2] = (byte)(port / 256);
            buffer[actualLength - 1] = (byte)(port % 256);

            e.SetBuffer(buffer, 0, actualLength);

            context.ReceivedData = new List<byte>(actualLength + 5);
            context.ExpectedLength = 5; //When the client receive 5 bytes, we can know how many bytes should be received exactly

            StartSend(context.Socket, e);
        }

        private void AutheticateWithUserNamePassword(SocketAsyncEventArgs e)
        {
            var context = (SocksContext)e.UserToken;

            var socket = context.Socket;

            e.SetBuffer(m_UserNameAuthenRequest.Array, m_UserNameAuthenRequest.Offset, m_UserNameAuthenRequest.Count);

            StartSend(socket, e);
        }
    }




    public abstract class ProxyConnectorBase : IProxyConnector
    {
        public EndPoint ProxyEndPoint { get; private set; }

        protected static Encoding ASCIIEncoding = new ASCIIEncoding();

#if SILVERLIGHT && !WINDOWS_PHONE
        protected SocketClientAccessPolicyProtocol ClientAccessPolicyProtocol { get; private set; }

        public ProxyConnectorBase(EndPoint proxyEndPoint, SocketClientAccessPolicyProtocol clientAccessPolicyProtocol)
        {
            ProxyEndPoint = proxyEndPoint;
            ClientAccessPolicyProtocol = clientAccessPolicyProtocol;
        }

#else
        public ProxyConnectorBase(EndPoint proxyEndPoint)
        {
            ProxyEndPoint = proxyEndPoint;
        }
#endif

        public abstract void Connect(EndPoint remoteEndPoint);

        private EventHandler<ProxyEventArgs> m_Completed;

        public event EventHandler<ProxyEventArgs> Completed
        {
            add { m_Completed += value; }
            remove { m_Completed -= value; }
        }

        protected void OnCompleted(ProxyEventArgs args)
        {
            if (m_Completed == null)
                return;

            m_Completed(this, args);
        }

        protected void OnException(Exception exception)
        {
            OnCompleted(new ProxyEventArgs(exception));
        }

        protected void OnException(string exception)
        {
            OnCompleted(new ProxyEventArgs(new Exception(exception)));
        }

        protected bool ValidateAsyncResult(SocketAsyncEventArgs e)
        {
            if (e.SocketError != SocketError.Success)
            {
                var socketException = new SocketException((int)e.SocketError);
                OnCompleted(new ProxyEventArgs(new Exception(socketException.Message, socketException)));
                return false;
            }

            return true;
        }

        protected void AsyncEventArgsCompleted(object sender, SocketAsyncEventArgs e)
        {
            if (e.LastOperation == SocketAsyncOperation.Send)
                ProcessSend(e);
            else
                ProcessReceive(e);
        }

        protected void StartSend(Socket socket, SocketAsyncEventArgs e)
        {
            bool raiseEvent = false;

            try
            {
                raiseEvent = socket.SendAsync(e);
            }
            catch (Exception exc)
            {
                OnException(new Exception(exc.Message, exc));
                return;
            }

            if (!raiseEvent)
            {
                ProcessSend(e);
            }
        }

        protected virtual void StartReceive(Socket socket, SocketAsyncEventArgs e)
        {
            bool raiseEvent = false;

            try
            {
                raiseEvent = socket.ReceiveAsync(e);
            }
            catch (Exception exc)
            {
                OnException(new Exception(exc.Message, exc));
                return;
            }

            if (!raiseEvent)
            {
                ProcessReceive(e);
            }
        }

        protected abstract void ProcessConnect(Socket socket, object targetEndPoint, SocketAsyncEventArgs e);

        protected abstract void ProcessSend(SocketAsyncEventArgs e);

        protected abstract void ProcessReceive(SocketAsyncEventArgs e);
    }


    /// <summary>
    /// Yet another proxy attempt :-\
    /// </summary>
    public class HttpConnectProxy : ProxyConnectorBase
    {
        class ConnectContext
        {
            public Socket Socket { get; set; }

            public SearchMarkState<byte> SearchState { get; set; }
        }

        private const string m_RequestTemplate = "CONNECT {0}:{1} HTTP/1.1\r\nHost: {0}:{1}\r\nProxy-Connection: Keep-Alive\r\n\r\n";

        private const string m_ResponsePrefix = "HTTP/1.1";
        private const char m_Space = ' ';

        private static byte[] m_LineSeparator;

        static HttpConnectProxy()
        {
            m_LineSeparator = ASCIIEncoding.GetBytes("\r\n\r\n");
        }

        private int m_ReceiveBufferSize;

#if SILVERLIGHT && !WINDOWS_PHONE
        public HttpConnectProxy(EndPoint proxyEndPoint, SocketClientAccessPolicyProtocol clientAccessPolicyProtocol)
            : this(proxyEndPoint, clientAccessPolicyProtocol, 128)
        {

        }

        public HttpConnectProxy(EndPoint proxyEndPoint, SocketClientAccessPolicyProtocol clientAccessPolicyProtocol, int receiveBufferSize)
            : base(proxyEndPoint, clientAccessPolicyProtocol)
        {
            m_ReceiveBufferSize = receiveBufferSize;
        }
#else
        public HttpConnectProxy(EndPoint proxyEndPoint)
            : this(proxyEndPoint, 128)
        {

        }

        public HttpConnectProxy(EndPoint proxyEndPoint, int receiveBufferSize)
            : base(proxyEndPoint)
        {
            m_ReceiveBufferSize = receiveBufferSize;
        }
#endif

        public override void Connect(EndPoint remoteEndPoint)
        {
            if (remoteEndPoint == null)
                throw new ArgumentNullException("remoteEndPoint");

            if (!(remoteEndPoint is IPEndPoint || remoteEndPoint is DnsEndPoint))
                throw new ArgumentException("remoteEndPoint must be IPEndPoint or DnsEndPoint", "remoteEndPoint");

            try
            {
#if SILVERLIGHT && !WINDOWS_PHONE
                ProxyEndPoint.ConnectAsync(ClientAccessPolicyProtocol, ProcessConnect, remoteEndPoint);
#else
                ProxyEndPoint.ConnectAsync(ProcessConnect, remoteEndPoint);
#endif
            }
            catch (Exception e)
            {
                OnException(new Exception("Failed to connect proxy server", e));
            }
        }

        protected override void ProcessConnect(Socket socket, object targetEndPoint, SocketAsyncEventArgs e)
        {
            if (e != null)
            {
                if (!ValidateAsyncResult(e))
                    return;
            }

            if (socket == null)
            {
                OnException(new SocketException((int)SocketError.ConnectionAborted));
                return;
            }

            if (e == null)
                e = new SocketAsyncEventArgs();

            string request;

            if (e.UserToken is DnsEndPoint)
            {
                var targetDnsEndPoint = (DnsEndPoint)targetEndPoint;
                request = string.Format(m_RequestTemplate, targetDnsEndPoint.Host, targetDnsEndPoint.Port);
            }
            else
            {
                var targetIPEndPoint = (IPEndPoint)targetEndPoint;
                request = string.Format(m_RequestTemplate, targetIPEndPoint.Address, targetIPEndPoint.Port);
            }

            var requestData = ASCIIEncoding.GetBytes(request);

            e.Completed += AsyncEventArgsCompleted;
            e.UserToken = new ConnectContext { Socket = socket, SearchState = new SearchMarkState<byte>(m_LineSeparator) };
            e.SetBuffer(requestData, 0, requestData.Length);

            StartSend(socket, e);
        }

        protected override void ProcessSend(SocketAsyncEventArgs e)
        {
            if (!ValidateAsyncResult(e))
                return;

            var context = (ConnectContext)e.UserToken;

            var buffer = new byte[m_ReceiveBufferSize];
            e.SetBuffer(buffer, 0, buffer.Length);

            StartReceive(context.Socket, e);
        }

        protected override void ProcessReceive(SocketAsyncEventArgs e)
        {
            if (!ValidateAsyncResult(e))
                return;

            var context = (ConnectContext)e.UserToken;

            int prevMatched = context.SearchState.Matched;

            int result = e.Buffer.SearchMark(e.Offset, e.BytesTransferred, context.SearchState);

            if (result < 0)
            {
                int total = e.Offset + e.BytesTransferred;

                if (total >= m_ReceiveBufferSize)
                {
                    OnException("receive buffer size has been exceeded");
                    return;
                }

                e.SetBuffer(total, m_ReceiveBufferSize - total);
                StartReceive(context.Socket, e);
                return;
            }

            int responseLength = prevMatched > 0 ? (e.Offset - prevMatched) : (e.Offset + result);

            if (e.Offset + e.BytesTransferred > responseLength + m_LineSeparator.Length)
            {
                OnException("protocol error: more data has been received");
                return;
            }

            var lineReader = new StringReader(ASCIIEncoding.GetString(e.Buffer, 0, responseLength));

            var line = lineReader.ReadLine();

            if (string.IsNullOrEmpty(line))
            {
                OnException("protocol error: invalid response");
                return;
            }

            //HTTP/1.1 2** OK
            var pos = line.IndexOf(m_Space);

            if (pos <= 0 || line.Length <= (pos + 2))
            {
                OnException("protocol error: invalid response");
                return;
            }

            var httpProtocol = line.Substring(0, pos);

            if (!m_ResponsePrefix.Equals(httpProtocol))
            {
                OnException("protocol error: invalid protocol");
                return;
            }

            var statusPos = line.IndexOf(m_Space, pos + 1);

            if (statusPos < 0)
            {
                OnException("protocol error: invalid response");
                return;
            }

            int statusCode;
            //Status code should be 2**
            if (!int.TryParse(line.Substring(pos + 1, statusPos - pos - 1), out statusCode) || (statusCode > 299 || statusCode < 200))
            {
                OnException("the proxy server refused the connection");
                return;
            }

            OnCompleted(new ProxyEventArgs(context.Socket));
        }
    }
}
