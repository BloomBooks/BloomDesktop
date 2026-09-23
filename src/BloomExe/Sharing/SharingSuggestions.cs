using System;
using System.Collections.Generic;
using System.Linq;
using Bloom.History;
using Bloom.Utils;

namespace Bloom.Sharing
{
    /// <summary>
    /// Works out whom an admin sharing a (folder) Team Collection would probably want to invite:
    /// everyone its history shows has worked in it.
    /// </summary>
    public static class SharingSuggestions
    {
        /// <summary>
        /// The people recorded in the given history events, most recently active first, leaving
        /// out anyone who already has access or whom an admin has dismissed. Someone the old Team
        /// Collection listed as an administrator is suggested as an Admin, anyone else as an
        /// Editor. Events without a usable email (older Blooms did not always record one) are
        /// ignored.
        /// </summary>
        public static List<SharingSuggestion> Find(
            IEnumerable<HistoryEvent> events,
            IEnumerable<string> administrators,
            CollectionSharingRecord record
        )
        {
            var excluded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (record != null)
            {
                excluded.UnionWith(record.Members.Select(m => m.Email.Trim()));
                excluded.UnionWith(record.DismissedSuggestions.Select(e => e.Trim()));
            }
            var admins = new HashSet<string>(
                (administrators ?? Enumerable.Empty<string>()).Select(a => a.Trim()),
                StringComparer.OrdinalIgnoreCase
            );

            return events
                .Where(e => !string.IsNullOrWhiteSpace(e.UserId))
                .Where(e => MiscUtils.IsValidEmail(e.UserId.Trim()))
                .GroupBy(e => e.UserId.Trim(), StringComparer.OrdinalIgnoreCase)
                .Where(g => !excluded.Contains(g.Key))
                .Select(g =>
                {
                    var latest = g.OrderByDescending(e => e.When).First();
                    // The name recorded with the most recent event that has one.
                    var name = g.OrderByDescending(e => e.When)
                        .Select(e => e.UserName)
                        .FirstOrDefault(n => !string.IsNullOrWhiteSpace(n));
                    return new SharingSuggestion
                    {
                        Email = latest.UserId.Trim(),
                        Name = name,
                        Role = admins.Contains(g.Key) ? SharingRole.Admin : SharingRole.Editor,
                        // History dates come back with Kind=Unspecified but are stored as UTC
                        // (see HistoryEvent.When); say so, so they serialize as UTC.
                        LastActivity = DateTime.SpecifyKind(latest.When, DateTimeKind.Utc),
                    };
                })
                .OrderByDescending(s => s.LastActivity)
                .ToList();
        }
    }
}
