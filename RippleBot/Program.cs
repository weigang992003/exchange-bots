using System;
using System.IO;
using System.Net;


namespace RippleBot
{
    class Program
    {
        static void Main(string[] args)
        {
            const string BASE_URL = "http://s-west.ripple.com:443/";

            var postData = "{ \"method\" : \"account_info\", \"params\" : [ { \"account\" : \"rpMV1zYgR5P6YWA2JSXDPcbsbqivkooKVY\"} ], \"id\" : \"1\" }";

            Console.WriteLine(post(BASE_URL, postData));

/*            var postData2 = "{ \"id\": 1, \"command\": \"account_info\", \"account\": \"r9cZA1mLK5R5Am25ArfXFmqgNwjZgnfk59\" }";
            Console.WriteLine(post("https://ripple.com/tools/api/", postData2));*/
        }


        private static string post(string url, string postData)
        {
            var webRequest = (HttpWebRequest)WebRequest.Create(url);
            webRequest.ContentType = "application/json";
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
                        return text;
                    }
                }
            }
        }
    }
}
