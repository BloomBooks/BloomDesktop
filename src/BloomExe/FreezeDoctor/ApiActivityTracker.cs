using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using System.Threading;

namespace Bloom.FreezeDoctor
{
    /// <summary>
    /// Keeps track of which API requests are in flight and for how long, so that a Freeze Doctor report can
    /// say what Bloom was actually *doing* when it stopped responding.
    ///
    /// This is the single most valuable thing Bloom can tell the Doctor. A stack trace says the UI thread is
    /// in `Monitor.Wait`; this says "POST /bloom/api/publish/epub/save has been running for 47 seconds",
    /// which is usually the whole answer. BL-16697 — the freeze this project exists for — is exactly the
    /// shape of problem it should identify.
    ///
    /// **It sits in the hot path, so it is written to be unable to hurt.** Every request in Bloom passes
    /// through here, so: one dictionary insert and one removal per request, no locks, no allocation beyond a
    /// single small entry, and nothing that can throw. If it somehow does throw, the caller's `using` still
    /// removes the entry and the request proceeds regardless.
    /// </summary>
    public static class ApiActivityTracker
    {
        /// <summary>
        /// In-flight requests, keyed by a ticket number. A ConcurrentDictionary rather than a lock, because
        /// this is touched by every server worker thread at once and a lock here would be a new way to
        /// deadlock the very thing we are trying to diagnose.
        /// </summary>
        private static readonly ConcurrentDictionary<long, InFlightRequest> _inFlight = new();

        private static long _nextTicket;

        /// <summary>
        /// How long a request has to have been running before we consider it worth naming. Below this it is
        /// just normal traffic.
        /// </summary>
        private static readonly TimeSpan InterestingDuration = TimeSpan.FromSeconds(2);

        /// <summary>One request that has started and not yet finished.</summary>
        private readonly struct InFlightRequest
        {
            public InFlightRequest(string path, long startedAtMs, int threadId)
            {
                Path = path;
                StartedAtMs = startedAtMs;
                ThreadId = threadId;
            }

            public string Path { get; }

            /// <summary>From <see cref="Environment.TickCount64"/>, so it needs no wall clock.</summary>
            public long StartedAtMs { get; }

            public int ThreadId { get; }
        }

        /// <summary>
        /// Records that a request has started. Dispose the result when it finishes — a `using` is the point,
        /// because it means an exception or an early return cannot leave a phantom request in the table
        /// making a healthy Bloom look stuck.
        /// </summary>
        public static IDisposable Begin(string path)
        {
            try
            {
                var ticket = Interlocked.Increment(ref _nextTicket);
                _inFlight[ticket] = new InFlightRequest(
                    path,
                    Environment.TickCount64,
                    Thread.CurrentThread.ManagedThreadId
                );
                return new Scope(ticket);
            }
            catch (Exception)
            {
                // Never let bookkeeping fail a request.
                return NoScope.Instance;
            }
        }

        /// <summary>
        /// Describes what Bloom is doing, for the Doctor's activity line: the longest-running request, how
        /// long it has been going, and how many others are in flight. Returns null when nothing has been
        /// running long enough to be interesting.
        ///
        /// Called from Bloom's watchdog thread rather than from the request path, so the cost of scanning
        /// falls on the once-a-second thread rather than on every request.
        /// </summary>
        public static string DescribeCurrentActivity()
        {
            try
            {
                if (_inFlight.IsEmpty)
                    return null;

                var now = Environment.TickCount64;
                var snapshot = _inFlight.Values.ToArray();
                if (snapshot.Length == 0)
                    return null;

                var oldest = snapshot[0];
                for (var i = 1; i < snapshot.Length; i++)
                {
                    if (snapshot[i].StartedAtMs < oldest.StartedAtMs)
                        oldest = snapshot[i];
                }

                var elapsed = TimeSpan.FromMilliseconds(Math.Max(0, now - oldest.StartedAtMs));
                if (elapsed < InterestingDuration)
                    return null;

                var others = snapshot.Length > 1 ? $" ({snapshot.Length} requests in flight)" : "";
                return $"api {oldest.Path} running {Describe(elapsed)} on thread {oldest.ThreadId}{others}";
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// Every request that has been in flight longer than <paramref name="longerThan"/>, **longest-running
        /// first** (which is oldest-started first), as text for a report — the one that has been stuck the
        /// longest is the one a reader wants at the top. Separate from <see cref="DescribeCurrentActivity"/> because a report can afford
        /// several lines where the activity field has room for one.
        /// </summary>
        public static string[] DescribeStuckRequests(TimeSpan longerThan)
        {
            try
            {
                var now = Environment.TickCount64;
                return _inFlight
                    .Values.Where(r =>
                        TimeSpan.FromMilliseconds(Math.Max(0, now - r.StartedAtMs)) > longerThan
                    )
                    .OrderBy(r => r.StartedAtMs)
                    .Select(r =>
                        $"{r.Path} — running {Describe(TimeSpan.FromMilliseconds(Math.Max(0, now - r.StartedAtMs)))} on thread {r.ThreadId}"
                    )
                    .ToArray();
            }
            catch (Exception)
            {
                return new string[0];
            }
        }

        private static string Describe(TimeSpan elapsed) =>
            elapsed.TotalSeconds < 90
                ? $"{elapsed.TotalSeconds:F0}s"
                : $"{elapsed.TotalMinutes:F1} minutes";

        /// <summary>Removes one request from the table, once, however the request ended.</summary>
        private sealed class Scope : IDisposable
        {
            private long _ticket;

            public Scope(long ticket)
            {
                _ticket = ticket;
            }

            public void Dispose()
            {
                // Taking the ticket away atomically makes a second Dispose harmless rather than removing
                // some later request's entry.
                var ticket = Interlocked.Exchange(ref _ticket, 0);
                if (ticket != 0)
                    _inFlight.TryRemove(ticket, out _);
            }
        }

        /// <summary>Handed out when we could not record the request, so callers need no special case.</summary>
        private sealed class NoScope : IDisposable
        {
            public static readonly NoScope Instance = new NoScope();

            public void Dispose() { }
        }
    }
}
