using System;
using System.Threading;

namespace Bloom.TeamCollection
{
    /// <summary>
    /// Periodically re-checks that we can still reach the Team Collection repo, and disconnects
    /// if we can't. See BL-16729.
    ///
    /// The file system watchers tell us at once when the shared folder is yanked away, but they
    /// cannot tell us that Dropbox has stopped syncing: the folder is still there, we simply
    /// stop receiving other people's work. Before this, CheckConnection ran only when the user
    /// did something (check out, check in, delete), so somebody who was just reading and editing
    /// their own checked-out book could go the whole session without finding out.
    ///
    /// Owned by TeamCollection.StartMonitoring/StopMonitoring, which gets the lifecycle right
    /// for free: no heartbeat during SyncAtStartup (monitoring is deliberately off then), none
    /// on a DisconnectedTeamCollection (whose Start/StopMonitoring are no-ops), and it stops
    /// when we disconnect or dispose.
    /// </summary>
    internal sealed class ConnectionHeartbeat : IDisposable
    {
        /// <summary>
        /// How long between checks when everything is fine. Long enough that the cost (which for
        /// a Dropbox repo includes an HTTP HEAD to dropbox.com) is negligible, short enough that
        /// the user finds out reasonably soon that they have stopped seeing their teammates' work.
        /// Not const, and internal, so tests can shorten it.
        /// </summary>
        internal static int IntervalMs = 60 * 1000;

        /// <summary>
        /// How soon we look again after a check fails, to see whether it was just a blip.
        /// Must be longer than DropboxUtils' 10-second cache of the dropbox.com probe, or the
        /// second look would just return the first one's answer and confirm nothing.
        /// </summary>
        internal const int kConfirmIntervalMs = 15 * 1000;

        private readonly TeamCollection _teamCollection;
        private readonly ConnectionFailureTracker _tracker = new ConnectionFailureTracker();
        private Timer _timer;
        private volatile bool _disposed;

        public ConnectionHeartbeat(TeamCollection teamCollection)
        {
            _teamCollection = teamCollection;
        }

        /// <summary>
        /// Begin checking. Does nothing under unit tests, which must not be left with live
        /// threadpool timers checking real folders and the real network.
        /// </summary>
        internal void Start()
        {
            if (Program.RunningUnitTests)
                return;
            // A one-shot timer that re-arms itself at the end of each tick (rather than a
            // repeating one) makes overlapping ticks structurally impossible, so a probe that
            // blocks for forty seconds on a dead share cannot pile up behind itself.
            _timer = new Timer(Tick, null, IntervalMs, Timeout.Infinite);
        }

        /// <summary>
        /// Runs on a threadpool thread. Internal so tests can drive the policy directly, with
        /// no timer involved.
        /// </summary>
        internal void Tick(object unused)
        {
            var delayUntilNextTick = IntervalMs;
            try
            {
                if (!OkToCheckNow())
                {
                    // Not the live collection, not watching just now (e.g. during a sync), or
                    // busy writing to the repo. Anything we noticed before such a gap is no
                    // longer part of a "consecutive" run, and an in-flight write reports its
                    // own failures, so disconnecting out from under it would be disruptive.
                    _tracker.Reset();
                }
                else
                {
                    // Deliberately the quiet overload: History messages are not de-duplicated,
                    // so a probe that wrote them would fill log.txt and raise a status-changed
                    // event on every tick of a perfectly healthy session.
                    var problem = _teamCollection.CheckConnection(writeHistoryMessages: false);
                    // The probe can block for several seconds (DropboxUtils allows 5 for its
                    // request to dropbox.com), which is long enough for a check-in or a sync to
                    // have started meanwhile. Re-ask before acting on what we found, or we
                    // would disconnect in the middle of one.
                    if (!OkToCheckNow())
                    {
                        _tracker.Reset();
                    }
                    else if (_tracker.RecordResult(problem))
                    {
                        _teamCollection.ReportConnectionProblem(problem);
                    }
                    else if (problem != null)
                    {
                        delayUntilNextTick = kConfirmIntervalMs; // suspicious; confirm sooner
                    }
                }
            }
            catch (Exception ex)
            {
                // An exception here is not evidence that the repo is gone, so we don't
                // disconnect over it. This matches TeamCollectionManager.CheckConnection.
                NonFatalProblem.ReportSentryOnly(ex);
            }
            finally
            {
                if (!_disposed)
                {
                    try
                    {
                        _timer?.Change(delayUntilNextTick, Timeout.Infinite);
                    }
                    catch (ObjectDisposedException)
                    {
                        // Disposed while we were checking. Nothing to re-arm.
                    }
                }
            }
        }

        /// <summary>
        /// Whether this is a sensible moment to check the connection at all. Checked both
        /// before and after the probe, because the probe can block long enough for the answer
        /// to change.
        /// </summary>
        private bool OkToCheckNow()
        {
            return !_disposed
                && _teamCollection.IsMonitoring
                && _teamCollection.IsLiveCollection
                && !_teamCollection.IsWritingToRepo;
        }

        public void Dispose()
        {
            _disposed = true;
            _timer?.Dispose();
            _timer = null;
        }
    }
}
