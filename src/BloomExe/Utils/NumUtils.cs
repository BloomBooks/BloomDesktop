using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Bloom.Utils
{
	internal static class NumUtils
	{
		/// <summary>
		/// Checks if two doubles are approximately equal to each other (within delta).
		/// (Remember, .Equals() doesn't do good number comparison for floating point types!)
		/// </summary>
		/// <returns>Returns true if the two numbers are within <paramref name="delta"/> of each other.</returns>
		public static bool ApproximatelyEquals(this double num, double other, double delta)
		{
			return Math.Abs(num - other) < delta;
		}
	}
}
