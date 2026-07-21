using System;
using System.Threading;
using Newtonsoft.Json.Linq;
using SIL.Reporting;

namespace Bloom.TeamCollection.Cloud
{
    /// <summary>
    /// Polls `get_changes` for one cloud collection, per Design/CloudTeamCollections.md's "polling
    /// first; realtime is a later wave" and the task 05 brief ("polling only (get_changes, 60s +
    /// on-activation); event-id self-echo suppression via last-seen cursor; catch-up-then-trust on
    /// reconnect"). Owned and driven by <see cref="CloudTeamCollection"/>, which supplies the
    /// starting cursor and interprets each batch of changes -- this class has no knowledge of
    /// TeamCollection's event model, it just fetches on a schedule and hands back the raw JSON.
    ///
    /// Self-echo suppression and catch-up-then-trust both fall out of the same mechanism: the
    /// cursor (`last_seen_event_id`) only ever advances to the server's own reported max_event_id,
    /// so a poll immediately following our own write already reflects that write (no separate event
    /// to "notice" and suppress), and a poll after being offline for a while asks for everything
    /// since the last cursor we actually incorporated -- there is no separate "reconnect" mode.
    /// </summary>
    public class CloudCollectionMonitor : IDisposable
    {
        public static readonly TimeSpan DefaultPollInterval = TimeSpan.FromSeconds(60);

        private readonly CloudCollectionClient _client;
        private readonly string _collectionId;
        private readonly Action<JObject> _onChanges;
        private readonly Action<Exception> _onError;
        private readonly TimeSpan _pollInterval;
        private readonly object _gate = new object();
        private long _lastSeenEventId;
        private Timer _timer;
        private bool _pollInProgress;
        private bool _disposed;

        /// <summary>
        /// True while a poll is actually in flight. Internal, for tests that need to know whether a
        /// triggered poll has settled before asserting on its effects.
        /// </summary>
        internal bool PollInProgressForTests
        {
            get
            {
                lock (_gate)
                    return _pollInProgress;
            }
        }

        public long LastSeenEventId => Interlocked.Read(ref _lastSeenEventId);

        public CloudCollectionMonitor(
            CloudCollectionClient client,
            string collectionId,
            long initialLastSeenEventId,
            Action<JObject> onChanges,
            Action<Exception> onError = null,
            TimeSpan? pollInterval = null
        )
        {
            _client = client;
            _collectionId = collectionId;
            _lastSeenEventId = initialLastSeenEventId;
            _onChanges = onChanges;
            _onError = onError;
            _pollInterval = pollInterval ?? DefaultPollInterval;
        }

        /// <summary>
        /// Starts the periodic poll timer. Does not itself poll synchronously -- call
        /// <see cref="PollNow"/> first if an immediate poll is wanted (e.g. right after joining a
        /// collection, before the first 60s tick).
        /// </summary>
        public void Start()
        {
            lock (_gate)
            {
                if (_timer != null || _disposed)
                    return;
                _timer = new Timer(_ => PollNow(), null, _pollInterval, _pollInterval);
            }
        }

        /// <summary>
        /// Runs one poll cycle immediately: used by the periodic timer, and available for
        /// "on-activation" callers (Bloom regaining focus, or a user-initiated "Receive Updates").
        /// Safe to call re-entrantly -- a poll already in flight is not duplicated, it just returns
        /// without doing anything (the in-flight poll will pick up the same ground on its own next
        /// run, or the next explicit call after it finishes will).
        /// </summary>
        public void PollNow()
        {
            lock (_gate)
            {
                if (_pollInProgress || _disposed)
                    return;
                _pollInProgress = true;
            }

            try
            {
                var since = LastSeenEventId;
                var changes = _client.GetChanges(_collectionId, since);
                if (changes == null)
                    return;

                var maxEventId = (long?)changes["max_event_id"];
                if (maxEventId.HasValue && maxEventId.Value > since)
                    Interlocked.Exchange(ref _lastSeenEventId, maxEventId.Value);

                _onChanges?.Invoke(changes);
            }
            catch (Exception e)
            {
                if (_onError != null)
                    _onError(e);
                else
                    NonFatalProblem.ReportSentryOnly(e, "CloudCollectionMonitor.PollNow");
            }
            finally
            {
                lock (_gate)
                    _pollInProgress = false;
            }
        }

        /// <summary>Terminal: stops the timer and permanently disables Start/PollNow (the
        /// monitor can never poll again). There is deliberately no separate pausable Stop();
        /// the one owner (CloudTeamCollection.StopMonitoring) only ever tears down.</summary>
        public void Dispose()
        {
            lock (_gate)
            {
                _disposed = true;
                _timer?.Dispose();
                _timer = null;
            }
        }
    }
}
