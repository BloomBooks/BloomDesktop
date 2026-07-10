using System.IO;
using SIL.IO;

namespace Bloom.TeamCollection
{
    /// <summary>
    /// Durable, local-only record of which cloud account most recently confirmed membership
    /// while using a given local Team Collection folder on THIS machine. Added for batch item 9
    /// (account-switch behavior, Design/CloudTeamCollections/orchestration/DOGFOOD-BATCH-1.md):
    /// when a local cloud Team Collection is opened by a different account than whoever used it
    /// here before, the refusal message shown to a non-member needs to name "the last team
    /// member who edited this collection on this machine", and nothing in the existing local
    /// state (TeamCollectionLink.txt is a bare folder-path-or-cloud-URI; per-book BookStatus
    /// files lose their lockedBy once a book is checked back in) recorded that.
    ///
    /// Design choice (documented per the batch item's instruction to "prefer the least invasive
    /// durable record"): rather than extending TeamCollectionLink.txt's tightly-scoped, tested
    /// parse format, or CollectionSettings.Administrators (which already has a known,
    /// separately-tracked identity bug -- it stores the registration email, not the signed-in
    /// cloud email, see the batch file's "Also queued from dogfooding" note), this is a tiny new
    /// sidecar file living next to TeamCollectionLink.txt. We deliberately record more than just
    /// the ORIGINAL joiner: every successful membership confirmation
    /// (CloudTeamCollection.CheckConnection) overwrites this file with the CURRENT user, so it
    /// doubles as "who joined" (for a collection nobody has reopened since) and "who was last
    /// confirmed here" (the general case) with a single mechanism. This is an approximation --
    /// it records the last account CONFIRMED as a member here, not literally "last edited a
    /// book" -- but it is the best locally-discoverable signal without new content-edit
    /// plumbing, and degrades gracefully to "unknown" (this file simply won't exist yet) for
    /// collections joined before this feature shipped, self-healing the first time this code
    /// runs afterward with a member signed in.
    /// </summary>
    public static class TeamCollectionLastKnownUser
    {
        public const string FileName = "TeamCollectionLastKnownUser.txt";

        /// <summary>Path to the sidecar file for the given local collection folder.</summary>
        public static string GetPath(string localCollectionFolder)
        {
            return Path.Combine(localCollectionFolder, FileName);
        }

        /// <summary>
        /// The email of the last cloud account confirmed to be a member while using this
        /// collection on this machine, or null if never recorded.
        /// </summary>
        public static string Read(string localCollectionFolder)
        {
            var path = GetPath(localCollectionFolder);
            if (!RobustFile.Exists(path))
                return null;
            var text = RobustFile.ReadAllText(path).Trim();
            return string.IsNullOrEmpty(text) ? null : text;
        }

        /// <summary>
        /// Records <paramref name="email"/> as the last confirmed member to use this collection
        /// on this machine. Safe/cheap to call on every successful connection check; does
        /// nothing if <paramref name="email"/> is null/empty (so a caller can pass through an
        /// unresolved identity without special-casing it), and does NOT rewrite the file when
        /// the recorded value is already current. That last point is load-bearing, not an
        /// optimization: CheckConnection runs from the UI thread's idle-time collection-file
        /// sync, and an unconditional write here retriggers the local FileSystemWatcher that
        /// SCHEDULES that sync -- an infinite idle-loop of network calls that starved the UI
        /// thread (found live, 10 Jul 2026: every checkInCurrentBook request timed out because
        /// the UI thread never got free). TeamCollection.OnChanged also now ignores this file
        /// by name; both guards are deliberate (either alone would break the loop, but the
        /// watcher exclusion also stops the file from being treated as a syncable collection
        /// file, and this check also protects any future non-watcher caller).
        /// </summary>
        public static void Record(string localCollectionFolder, string email)
        {
            if (string.IsNullOrEmpty(email))
                return;
            var trimmed = email.Trim();
            if (Read(localCollectionFolder) == trimmed)
                return;
            RobustFile.WriteAllText(GetPath(localCollectionFolder), trimmed);
        }
    }
}
