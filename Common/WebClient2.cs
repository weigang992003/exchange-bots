using System;
using System.Net;


namespace Common
{
    public class WebClient2 : WebClient
    {
        private readonly int _timeout;

        public WebClient2(int timeout)
        {
            _timeout = timeout;
        }

        protected override WebRequest GetWebRequest(Uri uri)
        {
            WebRequest w = base.GetWebRequest(uri);
            w.Timeout = _timeout;
            return w;
        }
    }
}
