using System;
using System.Net;


namespace Common
{
    public class WebClient2 : WebClient
    {
        private readonly int _timeout;
        private readonly Logger _logger;


        /// <summary>Create new WebClient2 instance</summary>
        /// <param name="logger">Logger for (mostly debugging) messages</param>
        /// <param name="timeout">Timeout in miliseconds</param>
        public WebClient2(Logger logger, int timeout)
        {
            _logger = logger;
            _timeout = timeout;
        }

        protected override WebRequest GetWebRequest(Uri uri)
        {
            WebRequest w = base.GetWebRequest(uri);
            w.Timeout = _timeout;
            return w;
        }


        public string DownloadStringSafe(string url)
        {
            try
            {
                var data = DownloadString(url);
                return data;
            }
            catch (Exception e)
            {
                _logger.AppendMessage("Error downloading string. Message=" + e.Message, true, ConsoleColor.Yellow);
                return null;
            }
        }

        public T DownloadObject<T>(string url) where T : new()
        {
            try
            {
                var data = DownloadString(url);
                return Helpers.DeserializeJSON<T>(data);
            }
            catch (Exception e)
            {
                _logger.AppendMessage("Error downloading string. Message=" + e.Message, true, ConsoleColor.Yellow);
                return default(T);
            }
        }
    }
}
