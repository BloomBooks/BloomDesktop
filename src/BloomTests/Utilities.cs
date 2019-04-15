using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;

namespace BloomTests
{
	public static class Utilities
	{
		private static TraceListener[] debugListenersClone;

		/// <summary>
		/// Disables Debug listeners such as that which creates the modal dialog for Debug.Assert
		/// </summary>
		public static void DisableDebugListeners()
		{
			debugListenersClone = new TraceListener[Debug.Listeners.Count];
			Debug.Listeners.CopyTo(debugListenersClone, 0);
			Debug.Listeners.Clear();
		}

		/// <summary>
		/// Re-enables the Debug listeners such as that which creates the modal dialog for Debug.Assert
		/// </summary>
		public static void EnableDebugListeners()
		{
			Debug.Listeners.AddRange(debugListenersClone);
		}
	}
}
