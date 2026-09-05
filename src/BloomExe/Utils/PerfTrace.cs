using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;
using SIL.IO;

namespace Bloom.Utils
{
    /// <summary>
    /// A very cheap opt-in trace of when things happen, for performance investigations.
    /// It is enabled only when the environment variable BLOOM_PERF_TRACE names a file to write to;
    /// otherwise every call here is a no-op. Each line is
    /// "milliseconds-since-process-start TAB managed-thread-id TAB label".
    /// We flush after every line because Bloom is normally killed rather than closed at the end of
    /// an automated run, so anything still buffered would be lost.
    /// </summary>
    public static class PerfTrace
    {
        private static readonly Stopwatch _stopwatch = Stopwatch.StartNew();
        private static readonly object _lock = new object();
        private static readonly string _path = GetPathFromEnvironment();

        /// <summary>
        /// True if BLOOM_PERF_TRACE named a file, so that marks are actually written.
        /// Guard any label that costs something to compute with this.
        /// </summary>
        public static bool Enabled => _path != null;

        private static string GetPathFromEnvironment()
        {
            try
            {
                var path = Environment.GetEnvironmentVariable("BLOOM_PERF_TRACE");
                if (string.IsNullOrWhiteSpace(path))
                    return null;
                // Start each process's trace from scratch.
                RobustFile.WriteAllText(path, "");
                return path;
            }
            catch (Exception)
            {
                return null; // never let tracing break Bloom
            }
        }

        /// <summary>
        /// Write one line to the trace file (does nothing unless tracing is enabled).
        /// </summary>
        public static void Mark(string label)
        {
            if (_path == null)
                return;
            try
            {
                var line = string.Format(
                    CultureInfo.InvariantCulture,
                    "{0}\t{1}\t{2}{3}",
                    _stopwatch.Elapsed.TotalMilliseconds.ToString(
                        "F1",
                        CultureInfo.InvariantCulture
                    ),
                    Thread.CurrentThread.ManagedThreadId,
                    label,
                    Environment.NewLine
                );
                lock (_lock)
                {
                    RobustFile.AppendAllText(_path, line);
                }
            }
            catch (Exception)
            {
                // Tracing must never affect what Bloom does.
            }
        }

        /// <summary>
        /// Mark the start of something and, when the returned object is disposed, its end and how
        /// long it took. Safe (and free) to use when tracing is disabled.
        /// </summary>
        public static IDisposable Measure(string label)
        {
            if (_path == null)
                return DisabledMeasurement.Singleton;
            Mark(label + " start");
            return new Measurement(label);
        }

        private class DisabledMeasurement : IDisposable
        {
            public static readonly DisabledMeasurement Singleton = new DisabledMeasurement();

            public void Dispose() { }
        }

        private class Measurement : IDisposable
        {
            private readonly string _label;
            private readonly double _startMs;

            public Measurement(string label)
            {
                _label = label;
                _startMs = _stopwatch.Elapsed.TotalMilliseconds;
            }

            public void Dispose()
            {
                var elapsed = _stopwatch.Elapsed.TotalMilliseconds - _startMs;
                Mark(
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "{0} end ({1:F1} ms)",
                        _label,
                        elapsed
                    )
                );
            }
        }
    }
}
