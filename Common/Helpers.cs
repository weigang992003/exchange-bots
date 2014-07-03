using System;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Text;


namespace Common
{
    public static class Helpers
    {
        public static int SuggestInterval(float madnessCoef, int minInterval = 2000, int maxInterval = 11000)
        {
            if (madnessCoef <= 0.0f)
                return maxInterval;
            if (madnessCoef >= 1.0f)
                return minInterval;

            return (int)(minInterval + ((1.0f - madnessCoef) * (maxInterval - minInterval)));
        }

        public static double SuggestWallVolume(float madnessCoef, double minVolume, double maxVolue)
        {
            if (madnessCoef <= 0.0f)
                return minVolume;
            if (madnessCoef >= 1.0f)
                return maxVolue;

            return (minVolume + (madnessCoef * (maxVolue - minVolume)));
        }

        public static T DeserializeJSON<T>(string json)
        {
            using (var ms = new MemoryStream(Encoding.Unicode.GetBytes(json)))
            {
                var deserializer = new DataContractJsonSerializer(typeof(T));
                try
                {
                    return (T)deserializer.ReadObject(ms);
                }
                catch (Exception e)
                {
                    throw new Exception("JSON deserialization problem. The input string was:" + Environment.NewLine + json, e);
                }
            }
        }
    }
}
