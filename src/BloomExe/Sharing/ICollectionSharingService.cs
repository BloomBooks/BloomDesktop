using System.Collections.Generic;

namespace Bloom.Sharing
{
    /// <summary>
    /// The operations behind the Share dialog, for the one collection currently open. The
    /// eventual implementation talks to the cloud (Supabase) backend, whose RPCs enforce the same
    /// rules server-side; until that is deployed, LocalFileCollectionSharingService keeps the
    /// record in a file in the collection folder. Every method that changes anything takes the
    /// email of the signed-in person doing it, and throws SharingNotAllowedException if the rules
    /// don't let them.
    /// </summary>
    public interface ICollectionSharingService
    {
        /// <summary>The collection's sharing record, or null if it has not been shared.</summary>
        CollectionSharingRecord GetRecord();

        /// <summary>
        /// Share the collection for the first time, with the given person as its admin (seen
        /// now), together with the people a Team Collection's history shows have worked in it
        /// (in their history roles, last seen at their last recorded action; the admin is left
        /// out of these, being there already) and the first invitations, either of which may be
        /// none. All or none: if any invitation is bad, it throws and the collection stays
        /// unshared. Deciding who may do this is the caller's job, since before a collection is
        /// shared the only authority is the local one (e.g. the old Team Collection's admin list).
        /// </summary>
        void StartSharing(
            string adminEmail,
            string adminName,
            IEnumerable<SharingInvitation> invitations,
            IEnumerable<TeamCollectionHistoryMember> historyMembers
        );

        /// <summary>
        /// Invite people (by an admin), all or none: throws, changing nothing, if any of them
        /// already has access or appears twice.
        /// </summary>
        void Invite(string byEmail, IEnumerable<SharingInvitation> invitations);

        /// <summary>Change someone else's role (by an admin); nobody may change their own.</summary>
        void SetRole(string byEmail, string email, SharingRole role);

        /// <summary>Take away someone else's access (by an admin); nobody may remove themselves.</summary>
        void Remove(string byEmail, string email);

        /// <summary>
        /// Note that this signed-in person is using the collection now: their "last seen" time
        /// and name are updated. Does nothing if they are not a member.
        /// </summary>
        void RecordVisit(string email, string name);
    }
}
