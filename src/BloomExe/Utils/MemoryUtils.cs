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
			return GetPrivateBytes() > 750000;
		}

		/// <summary>
		/// Significance: This counter indicates the current number of bytes allocated to this process that cannot be shared with
		/// other processes. This counter has been useful for identifying memory leaks.
		/// </summary>
		/// <returns></returns>
		public static long GetPrivateBytes()
		{
			using (var perfCounter = new PerformanceCounter("Process", "Private Bytes",
				Process.GetCurrentProcess().ProcessName))
			{
				return perfCounter.RawValue / 1024;
			}
		}
	}
}
