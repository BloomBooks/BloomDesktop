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
            debugListenersClone = new TraceListener[Trace.Listeners.Count];
            Trace.Listeners.CopyTo(debugListenersClone, 0);
            Trace.Listeners.Clear();
        }

        /// <summary>
        /// Re-enables the Debug listeners such as that which creates the modal dialog for Debug.Assert
        /// </summary>
        public static void EnableDebugListeners()
        {
            foreach (var listener in debugListenersClone)
            {
                Trace.Listeners.Add(listener);
            }
        }
    }
}
