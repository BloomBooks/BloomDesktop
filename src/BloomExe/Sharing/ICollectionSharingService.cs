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
        /// Share the collection for the first time, with the given person as its only (active)
        /// admin. Deciding who may do this is the caller's job, since before a collection is
        /// shared the only authority is the local one (e.g. the old Team Collection's admin list).
        /// </summary>
        void StartSharing(string adminEmail, string adminName);

        /// <summary>Invite someone (by an admin). Throws if they already have access.</summary>
        void Invite(string byEmail, string email, SharingRole role);

        /// <summary>Change someone's role (by an admin). The last admin cannot be demoted.</summary>
        void SetRole(string byEmail, string email, SharingRole role);

        /// <summary>Take away someone's access (by an admin). The last admin cannot be removed.</summary>
        void Remove(string byEmail, string email);

        /// <summary>
        /// Stop offering these people (found in the old Team Collection's history) as people
        /// to invite (by an admin).
        /// </summary>
        void DismissSuggestions(string byEmail, IEnumerable<string> emails);

        /// <summary>
        /// Note that this signed-in person is using the collection now: a member who was only
        /// invited becomes active, and their "last seen" time and name are updated. Does nothing
        /// if they are not a member.
        /// </summary>
        void RecordVisit(string email, string name);
    }
}
