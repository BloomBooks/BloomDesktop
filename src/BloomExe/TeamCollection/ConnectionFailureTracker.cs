namespace Bloom.TeamCollection
{
    /// <summary>
    /// Decides when a run of failed connection checks has gone on long enough to believe.
    /// Kept as a separate, pure class (no IO, no timers, no threads) so the policy can be
    /// unit tested exhaustively; ConnectionHeartbeat supplies the timing. See BL-16729.
    ///
    /// The point of waiting for a second failure is that the things CheckConnection looks at
    /// can lie: one dropped packet fails the probe to dropbox.com, a Wi-Fi roam briefly makes
    /// NetworkInterface.GetIsNetworkAvailable() false, a flaky SMB share can answer "no such
    /// folder" for a moment. Wrongly disconnecting a collection that is working is the worst
    /// outcome available to us, because there is no automatic way back: the user has to
    /// Reload Collection. Waiting fifteen seconds to be sure is cheap by comparison.
    /// </summary>
    internal class ConnectionFailureTracker
    {
        /// <summary>
        /// How many checks in a row must report the same problem before we act on it.
        /// </summary>
        internal const int kRequiredConsecutiveFailures = 2;

        private string _lastFailureL10nId;
        private int _consecutiveFailures;

        /// <summary>
        /// Feed in the result of one connection check. Returns true when we have now seen
        /// enough consecutive failures of the same kind to conclude we really are disconnected.
        /// </summary>
        /// <param name="problemOrNull">What CheckConnection returned: null means all is well.</param>
        public bool RecordResult(TeamCollectionMessage problemOrNull)
        {
            if (problemOrNull == null)
            {
                Reset();
                return false;
            }
            if (problemOrNull.L10NId != _lastFailureL10nId)
            {
                // A different problem from last time. "No network" followed by "repo missing"
                // is two transients, not one sustained outage, so start counting again.
                _lastFailureL10nId = problemOrNull.L10NId;
                _consecutiveFailures = 0;
            }
            return ++_consecutiveFailures >= kRequiredConsecutiveFailures;
        }

        /// <summary>
        /// Forget any run of failures, e.g. because we stopped checking for a while.
        /// </summary>
        public void Reset()
        {
            _consecutiveFailures = 0;
            _lastFailureL10nId = null;
        }
    }
}
