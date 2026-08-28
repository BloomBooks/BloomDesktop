using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
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
        /// True once Bloom has handled at least one API request. Which is to say: the UI is up and talking to
        /// the server, so Bloom has finished starting. Used to retire the "starting up" note, which would
        /// otherwise describe an idle Bloom hours later.
        /// </summary>
        internal static bool HasHandledARequest => Volatile.Read(ref _nextTicket) > 0;

        /// <summary>
        /// How long a request has to have been running before we consider it worth naming. Below this it is
        /// just normal traffic.
        /// </summary>
        private static readonly TimeSpan InterestingDuration = TimeSpan.FromSeconds(2);

        /// <summary>One request that has started and not yet finished.</summary>
        private readonly struct InFlightRequest
        {
            public InFlightRequest(
                string path,
                long startedAtMs,
                int osThreadId,
                string lockState = null
            )
            {
                Path = path;
                StartedAtMs = startedAtMs;
                OsThreadId = osThreadId;
                LockState = lockState;
            }

            public string Path { get; }

            /// <summary>From <see cref="Environment.TickCount64"/>, so it needs no wall clock.</summary>
            public long StartedAtMs { get; }

            /// <summary>
            /// The OS thread id, NOT the managed one. This is the number that joins a request to a stack:
            /// the Doctor walks stacks with ClrMD, which reports OS thread ids, so a managed id here would
            /// put two unrelated numbering spaces side by side in one report and invite a reader to match
            /// the wrong stack to the request that is stuck.
            ///
            /// It is the thread the request STARTED on. A request that has since awaited may be continuing
            /// somewhere else, so the join is exact for the requests that matter most — the ones stuck in a
            /// synchronous wait, which never left this thread — and a starting point for the rest.
            /// </summary>
            public int OsThreadId { get; }

            /// <summary>
            /// Which of the API's mutual-exclusion locks this request is waiting for or holding, or null for
            /// the requests that need none. Naming it is what turns "3 workers blocked" into a diagnosis:
            /// several requests waiting on the one lock a fourth is holding is the shape of an API deadlock,
            /// and Windows' wait-chain analysis cannot see it, since SemaphoreSlim is invisible there.
            /// </summary>
            public string LockState { get; }

            /// <summary>
            /// How long this request has been running, given a reading of the tick count. Clamped at zero:
            /// the tick count is monotonic so it should never go backwards, and a negative duration in a
            /// report would be a puzzle nobody needs.
            /// </summary>
            public TimeSpan ElapsedAt(long nowMs) =>
                TimeSpan.FromMilliseconds(Math.Max(0, nowMs - StartedAtMs));

            /// <summary>The same request, with its lock state replaced.</summary>
            public InFlightRequest With(string lockState) =>
                new InFlightRequest(Path, StartedAtMs, OsThreadId, lockState);
        }

        /// <summary>
        /// The OS thread id of the calling thread. A TEB read behind a thin P/Invoke, so it is cheap enough
        /// for the request path; see <see cref="InFlightRequest.OsThreadId"/> for why the managed id will
        /// not do.
        /// </summary>
        [DllImport("kernel32.dll")]
        private static extern int GetCurrentThreadId();

        /// <summary>
        /// Records that a request has started. Dispose the result when it finishes — a `using` is the point,
        /// because it means an exception or an early return cannot leave a phantom request in the table
        /// making a healthy Bloom look stuck.
        /// </summary>
        public static ApiActivityScope Begin(string path)
        {
            try
            {
                var ticket = Interlocked.Increment(ref _nextTicket);
                _inFlight[ticket] = new InFlightRequest(
                    path,
                    Environment.TickCount64,
                    GetCurrentThreadId()
                );
                return new ApiActivityScope(ticket);
            }
            catch (Exception)
            {
                // Never let bookkeeping fail a request. Ticket 0 is the "not recorded" scope, so the caller
                // needs no special case and every method on it is a no-op.
                return new ApiActivityScope(0);
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

                var elapsed = oldest.ElapsedAt(now);
                if (elapsed < InterestingDuration)
                    return null;

                var others = snapshot.Length > 1 ? $" ({snapshot.Length} requests in flight)" : "";
                return $"api {oldest.Path} running {Describe(elapsed)}{DescribeLock(oldest)} "
                    + $"on OS thread {oldest.OsThreadId}{others}";
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
                    .Values.Where(r => r.ElapsedAt(now) > longerThan)
                    .OrderBy(r => r.StartedAtMs)
                    .Select(r =>
                        $"{r.Path} — running {Describe(r.ElapsedAt(now))}{DescribeLock(r)} on OS "
                        + $"thread {r.OsThreadId}"
                    )
                    .ToArray();
            }
            catch (Exception)
            {
                return new string[0];
            }
        }

        /// <summary>The lock clause for a report line, or nothing for a request that needs no lock.</summary>
        private static string DescribeLock(InFlightRequest request) =>
            string.IsNullOrEmpty(request.LockState) ? "" : $", {request.LockState}";

        private static string Describe(TimeSpan elapsed) =>
            elapsed.TotalSeconds < 90
                ? $"{elapsed.TotalSeconds:F0}s"
                : $"{elapsed.TotalMinutes:F1} minutes";

        /// <summary>
        /// One tracked request: removes it from the table when disposed, and lets the handler say what the
        /// request is waiting for in the meantime.
        ///
        /// A ticket of 0 means the request was never recorded, so every method here does nothing — which is
        /// why the caller needs neither a null check nor a special case.
        /// </summary>
        public sealed class ApiActivityScope : IDisposable
        {
            private long _ticket;

            internal ApiActivityScope(long ticket)
            {
                _ticket = ticket;
            }

            /// <summary>
            /// Says this request is now waiting to acquire one of the API's mutual-exclusion locks. Called
            /// on the request's own thread, immediately before the wait.
            /// </summary>
            public void NoteWaitingForLock(string lockName) =>
                SetLockState("waiting for " + lockName);

            /// <summary>
            /// Says this request now HOLDS the lock. The pair matters more than either half: a report
            /// showing one request holding a lock and three waiting for it has named the deadlock.
            /// </summary>
            public void NoteHoldingLock(string lockName) => SetLockState("holding " + lockName);

            private void SetLockState(string state)
            {
                try
                {
                    var ticket = Volatile.Read(ref _ticket);
                    if (ticket == 0)
                        return;
                    // Replaced rather than mutated, because the entry is a struct. The read and the write
                    // are not one atomic step, but the only writer of this entry is the request's own
                    // thread, so there is nobody to race with.
                    if (_inFlight.TryGetValue(ticket, out var existing))
                        _inFlight[ticket] = existing.With(state);
                }
                catch (Exception)
                {
                    // Diagnostics must never break the request they describe.
                }
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
    }
}
