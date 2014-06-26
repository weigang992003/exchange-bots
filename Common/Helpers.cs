

namespace Common
{
    public static class Helpers
    {
        public static int SuggestInterval(float madnessCoef)
        {
            const int MIN_INTERVAL = 2000;
            const int MAX_INTERVAL = 11000;

            if (madnessCoef <= 0.0f)
                return MAX_INTERVAL;
            if (madnessCoef >= 1.0f)
                return MIN_INTERVAL;

            return (int)(MIN_INTERVAL + ((1.0f - madnessCoef) * (MAX_INTERVAL - MIN_INTERVAL)));
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
