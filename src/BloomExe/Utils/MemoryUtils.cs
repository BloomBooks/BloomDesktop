using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Bloom.Utils
{
	class MemoryUtils
	{
		/// <summary>
		/// A crude way of measuring when we might be short enough of memory to need a full reload.
		/// </summary>
		/// <returns></returns>
		public static bool SystemIsShortOfMemory()
		{
			// A rather arbitrary limit of 750M...a bit more than Bloom typically uses for a large book
			// before memory leaks start to mount up.  A larger value is wanted for 64-bit processes
			// since they can start out above the 750M level.
			var triggerLevel = Environment.Is64BitProcess ? 2000000000L : 750000000;
			return GetPrivateBytes() > triggerLevel;
		}

		/// <summary>
		/// Significance: This value indicates the current number of bytes allocated to this process that cannot be shared with
		/// other processes. This value has been useful for identifying memory leaks.
		/// </summary>
		/// <remarks>We've had other versions of this method which, confusingly, returned results in KB. This one actually answers bytes.</remarks>
		public static long GetPrivateBytes()
		{
			// Using a PerformanceCounter does not work on Linux and gains nothing on Windows.
			// After using the PerformanceCounter once on Windows, it always returns the same
			// value as getting it directly from the Process property.
			using (var process = Process.GetCurrentProcess())
			{
				return process.PrivateMemorySize64;
			}
		}
	}
}
