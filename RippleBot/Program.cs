using System;
using System.IO;
using System.Net;


namespace RippleBot
{
    class Program
    {
        static void Main(string[] args)
        {
            const string BASE_URL = "http://s-east.ripple.com:443/";

            var postData = "{ \"method\" : \"account_info\", \"params\" : [ { \"account\" : \"rpMV1zYgR5P6YWA2JSXDPcbsbqivkooKVY\"} ] }";

            var webRequest = (HttpWebRequest)WebRequest.Create(BASE_URL);
//            webRequest.ContentType = "application/x-www-form-urlencoded";
            webRequest.Method = "POST";

            var _webProxy = new WebProxy("wsproxybra.ext.crifnet.com", 8080);
            _webProxy.Credentials = CredentialCache.DefaultCredentials;
            webRequest.Proxy = _webProxy;

            using (var writer = new StreamWriter(webRequest.GetRequestStream()))
            {
                writer.Write(postData);
            }

            using (WebResponse webResponse = webRequest.GetResponse())
            {
                using (Stream stream = webResponse.GetResponseStream())
                {
                    using (StreamReader reader = new StreamReader(stream))
                    {
                        var text = reader.ReadToEnd();
                        Console.WriteLine(text);
                    }
                }
            }
        }
    }
}
