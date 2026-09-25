using System;
using System.Collections.Generic;
using System.Linq;
using Bloom.History;
using Bloom.Utils;

namespace Bloom.Sharing
{
    /// <summary>
    /// Works out who should have access when a (folder) Team Collection starts being shared:
    /// everyone its history shows has worked in it. The admin sharing it can remove anyone who
    /// should not be there.
    /// </summary>
    public static class TeamCollectionHistoryMembers
    {
        /// <summary>
        /// The people recorded in the given history events, most recently active first. Someone
        /// the old Team Collection listed as an administrator gets the Admin role, anyone else
        /// Editor. Events without a usable email (older Blooms did not always record one) are
        /// ignored.
        /// </summary>
        public static List<TeamCollectionHistoryMember> Find(
            IEnumerable<HistoryEvent> events,
            IEnumerable<string> administrators
        )
        {
            var admins = new HashSet<string>(
                (administrators ?? Enumerable.Empty<string>()).Select(a => a.Trim()),
                StringComparer.OrdinalIgnoreCase
            );

            return events
                .Where(e => !string.IsNullOrWhiteSpace(e.UserId))
                .Where(e => MiscUtils.IsValidEmail(e.UserId.Trim()))
                .GroupBy(e => e.UserId.Trim(), StringComparer.OrdinalIgnoreCase)
                .Select(g =>
                {
                    var latest = g.OrderByDescending(e => e.When).First();
                    // The name recorded with the most recent event that has one.
                    var name = g.OrderByDescending(e => e.When)
                        .Select(e => e.UserName)
                        .FirstOrDefault(n => !string.IsNullOrWhiteSpace(n));
                    return new TeamCollectionHistoryMember
                    {
                        Email = latest.UserId.Trim(),
                        Name = name,
                        Role = admins.Contains(g.Key) ? SharingRole.Admin : SharingRole.Editor,
                        // History dates come back with Kind=Unspecified but are stored as UTC
                        // (see HistoryEvent.When); say so, so they serialize as UTC.
                        LastActivity = DateTime.SpecifyKind(latest.When, DateTimeKind.Utc),
                    };
                })
                .OrderByDescending(m => m.LastActivity)
                .ToList();
        }

        /// <summary>
        /// Called when a signed-in person who may manage sharing (see SharingApi.CanManage) looks
        /// at the sharing of a collection that is not shared yet. If it is a Team Collection,
        /// share it now, with that person as admin and everyone its history shows has worked in
        /// it as members, in one write. An ordinary collection is left alone: it starts being
        /// shared with the first invitation. getEvents is only called for a Team Collection,
        /// since reading the history is not free.
        /// </summary>
        public static void StartSharingIfTeamCollection(
            ICollectionSharingService service,
            bool isTeamCollection,
            string adminEmail,
            string adminName,
            Func<IEnumerable<HistoryEvent>> getEvents,
            IEnumerable<string> administrators
        )
        {
            if (!isTeamCollection || service.GetRecord() != null)
                return;
            service.StartSharing(
                adminEmail,
                adminName,
                new SharingInvitation[0],
                Find(getEvents(), administrators)
            );
        }
    }
}
