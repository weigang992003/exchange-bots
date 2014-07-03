
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
    }
}
