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
			// before memory leaks start to mount up.
			return GetPrivateBytes() > 750000000;
		}

		/// <summary>
		/// Significance: This counter indicates the current number of bytes allocated to this process that cannot be shared with
		/// other processes. This counter has been useful for identifying memory leaks.
		/// </summary>
		/// <remarks>We've had other versions of this method which, confusingly, returned results in KB. This one actually answers bytes.</remarks>
		/// <returns></returns>
		public static long GetPrivateBytes()
		{
			using (var perfCounter = new PerformanceCounter("Process", "Private Bytes",
				Process.GetCurrentProcess().ProcessName))
			{
				return perfCounter.RawValue;
			}
		}
	}
}
