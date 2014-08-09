using System;
using System.Collections.Generic;
using System.Linq;


namespace Common
{
    public static class Extensions
    {
        /// <summary>Compare this <see cref="Double"/> to another, with tolerance of 0.00001</summary>
        public static bool eq(this double value, double other)
        {
            return Math.Abs(value - other) < 0.0000001;
        }

        /// <summary>Retuns given number of elements at the end of sequence</summary>
        public static IEnumerable<T> TakeLast<T>(this IEnumerable<T> source, int count)
        {
            if (source.Count() < count)
                throw new ArgumentException("Not enough elements");

            return source.Skip(source.Count() - count);
        }
    }
}
