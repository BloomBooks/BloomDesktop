using System;
using System.Collections.Generic;
using Bloom.History;

namespace Bloom.Sharing
{
    /// <summary>
    /// Starting to share a collection that is not shared yet, which the admin does with the Share
    /// dialog's Start sharing button (POST sharing/start), and the preview of who will have access
    /// that the dialog shows before that (GET sharing/state). Nothing is saved until Start: the
    /// preview only reads. The steps of starting follow Design/CloudTeamCollections.md, section 5;
    /// those that need the cloud backend, or the freeze setting of PR #8414, are separate methods
    /// here that do not do anything yet.
    /// </summary>
    public class CollectionSharingStarter
    {
        /// <summary>
        /// The Bloom version a collection requires once it is a cloud collection, and the one the
        /// old folder Team Collection requires once it is frozen; older Blooms can't work with
        /// either.
        /// </summary>
        public const string kCloudCollectionMinimumBloomVersion = "6.6";

        private readonly ICollectionSharingService _service;
        private readonly bool _isTeamCollection;
        private readonly Func<IEnumerable<HistoryEvent>> _getEvents;
        private readonly IEnumerable<string> _administrators;
        private readonly Func<DateTime> _utcNow;

        /// <summary>
        /// isTeamCollection says whether the collection is a folder Team Collection, whose history
        /// (from getEvents, only called for one, since reading it is not free) and administrators
        /// list decide who else starts with access. utcNow lets tests control the clock; by
        /// default it is DateTime.UtcNow.
        /// </summary>
        public CollectionSharingStarter(
            ICollectionSharingService service,
            bool isTeamCollection,
            Func<IEnumerable<HistoryEvent>> getEvents,
            IEnumerable<string> administrators,
            Func<DateTime> utcNow = null
        )
        {
            _service = service;
            _isTeamCollection = isTeamCollection;
            _getEvents = getEvents;
            _administrators = administrators;
            _utcNow = utcNow ?? (() => DateTime.UtcNow);
        }

        /// <summary>
        /// Who would have access if the given admin started sharing now: them, as Admin, and, for
        /// a Team Collection, everyone its history shows has worked in it. Saves nothing.
        /// </summary>
        public List<SharingMember> PreviewMembers(string adminEmail, string adminName)
        {
            return CollectionSharingRecord.StartingMembers(
                adminEmail,
                adminName,
                HistoryMembers(),
                _utcNow()
            );
        }

        /// <summary>
        /// Start sharing the collection, with the given admin and (for a Team Collection) the
        /// people its history shows, all or none. mayManage is the caller's decision that this
        /// person may (SharingApi.CanManage); if not, or if the collection is already shared (so
        /// pressing Start twice, say in two dialogs, can't start it twice), this throws
        /// SharingNotAllowedException and changes nothing.
        /// </summary>
        public void Start(string adminEmail, string adminName, bool mayManage)
        {
            if (!mayManage)
                throw new SharingNotAllowedException(
                    "Only an administrator of this collection can share it."
                );
            if (_service.GetRecord() != null)
                throw new SharingNotAllowedException("This collection is already shared.");
            var historyMembers = HistoryMembers();
            if (_isTeamCollection)
                FreezeOldTeamCollection();
            RequireNewerBloomForCloudCollection();
            // Creates the cloud collection, with its members; see ICollectionSharingService.
            _service.StartSharing(adminEmail, adminName, historyMembers);
            StartInitialUpload();
        }

        private IEnumerable<TeamCollectionHistoryMember> HistoryMembers()
        {
            if (!_isTeamCollection)
                return new TeamCollectionHistoryMember[0];
            return TeamCollectionHistoryMembers.Find(_getEvents(), _administrators);
        }

        // Step 1 of section 5, for a folder Team Collection only: freeze the old Team Collection
        // for everyone by setting, in the old shared folder's collection settings (the
        // .bloomCollection in Other/Other Collection Files.zip), AllowSharedFolderChanges=False
        // (so a 6.6 Bloom still on the old system writes nothing to the shared folder) and
        // MinimumBloomVersion=kCloudCollectionMinimumBloomVersion (so 6.4 and 6.5 are shut out).
        // Not done yet: AllowSharedFolderChanges comes with PR #8414 (BL-16928), and freezing
        // the collection is only right once the cloud collection that replaces it is real.
        private void FreezeOldTeamCollection() { }

        // Section 5, for every collection that becomes a cloud collection: set
        // CollectionSettings.MinimumBloomVersion to kCloudCollectionMinimumBloomVersion in the
        // collection's own settings before they are uploaded, so every member's copy downloaded
        // from the cloud shuts out older Blooms (which would otherwise open it as an ordinary
        // collection and change books behind the cloud's back). Not done yet, deliberately:
        // until the cloud collection really exists, it would only lock this collection away
        // from older Blooms on this computer, and for a Team Collection, whose settings are
        // pushed to the shared folder when they are saved, it would lock every teammate still
        // on 6.5 out of a collection that has not actually moved anywhere.
        private void RequireNewerBloomForCloudCollection() { }

        // Step 2 of section 5: start sending, in the background, one ordinary first check-in
        // per book the server doesn't have yet (for a Team Collection, the old shared folder's
        // checked-in version of books others have checked out, with their Migration Keys and
        // placeholder locks), then clear the initial-upload flag. Needs the cloud backend and
        // the CloudTeamCollection client (#8052); until then there is nothing to upload to.
        private void StartInitialUpload() { }
    }
}
