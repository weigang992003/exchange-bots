using System;
using System.Collections.Generic;
using System.Linq;


namespace Common
{
    public static class Extensions
    {
        /// <summary>Compare this <see cref="Double"/> to another, with tolerance of <paramref name="delta"/></summary>
        public static bool eq(this double value, double other, double delta = 0.0000001)
        {
            return Math.Abs(value - other) < delta;
        }

        /// <summary>Retuns given number of elements at the end of sequence</summary>
        public static IEnumerable<T> TakeLast<T>(this IEnumerable<T> source, int count)
        {
            var realCount = source.Count();
            if (realCount < count)
                throw new ArgumentException("Not enough elements");

            return source.Skip(realCount - count);
        }
    }
}
