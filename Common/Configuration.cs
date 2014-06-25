using System;
using System.Collections.Generic;
using System.IO;


namespace Common
{
    public static class Configuration
    {
        private static Dictionary<string, string> _values;

        public static string Strategy { get { return GetValue("STRATEGY"); } }
        public static string AccessKey { get { return GetValue("ACCESS_KEY"); } }
        public static string SecretKey { get { return GetValue("SECRET_KEY"); } }


        /// <summary>Read configuration file in form "key=value" per line, case insensitive. Lines not having this pattern are ignored.</summary>
        public static void Load(string fullPath)
        {
            _values = new Dictionary<string, string>();

            string line;
            var file = new StreamReader(fullPath);
            while ((line = file.ReadLine()) != null)
            {
                if (!String.IsNullOrEmpty(line) && line.Contains("="))
                {
                    var index = line.IndexOf('=');
                    var key = line.Substring(0, index);
                    var value = line.Substring(index + 1);
                    _values.Add(key.ToUpper(), value);
                }
            }
        }


        public static string GetValue(string key)
        {
            key = key.ToUpper();
            if (!_values.ContainsKey(key))
                return null;
            return _values[key];
        }
    }
}
