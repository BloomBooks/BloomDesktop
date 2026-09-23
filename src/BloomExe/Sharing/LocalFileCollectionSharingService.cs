using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using SIL.IO;

namespace Bloom.Sharing
{
    /// <summary>
    /// Thrown when someone tries a sharing change the rules don't allow (a non-admin managing
    /// sharing, removing the last admin, inviting someone twice...). The UI is meant to prevent
    /// all of these, so reaching one is a bug.
    /// </summary>
    public class SharingNotAllowedException : ApplicationException
    {
        public SharingNotAllowedException(string message)
            : base(message) { }
    }

    /// <summary>
    /// A stand-in for the cloud sharing backend, until that is deployed: keeps the collection's
    /// sharing record in a JSON file in the collection folder, and enforces the rules the
    /// backend's RPCs will enforce. Nobody but the people using this computer ever sees the file,
    /// so invitations go nowhere; it just lets the Share dialog be built and used. (A folder Team
    /// Collection does not sync this file; see TeamCollection.RootLevelCollectionFilesIn.)
    /// </summary>
    public class LocalFileCollectionSharingService : ICollectionSharingService
    {
        /// <summary>The name of the file, in the collection folder, that holds the record.</summary>
        public const string kFileName = "sharing.local.json";

        private readonly string _filePath;
        private readonly string _collectionId;
        private readonly string _collectionName;
        private readonly Func<DateTime> _utcNow;

        // API requests can arrive on several threads; this keeps each read-modify-write whole.
        private readonly object _lock = new object();

        /// <summary>
        /// Create the service for the collection in the given folder. utcNow lets tests control
        /// the clock; by default it is DateTime.UtcNow.
        /// </summary>
        public LocalFileCollectionSharingService(
            string collectionFolder,
            string collectionId,
            string collectionName,
            Func<DateTime> utcNow = null
        )
        {
            _filePath = Path.Combine(collectionFolder, kFileName);
            _collectionId = collectionId;
            _collectionName = collectionName;
            _utcNow = utcNow ?? (() => DateTime.UtcNow);
        }

        /// <inheritdoc/>
        public CollectionSharingRecord GetRecord()
        {
            lock (_lock)
            {
                return Read();
            }
        }

        /// <inheritdoc/>
        public void StartSharing(string adminEmail, string adminName)
        {
            lock (_lock)
            {
                if (Read() != null)
                    throw new SharingNotAllowedException("This collection is already shared.");
                var now = _utcNow();
                Write(
                    new CollectionSharingRecord
                    {
                        CollectionId = _collectionId,
                        CollectionName = _collectionName,
                        CreatedAt = now,
                        Members = new List<SharingMember>
                        {
                            new SharingMember
                            {
                                Email = adminEmail,
                                Name = adminName,
                                Role = SharingRole.Admin,
                                Status = SharingMemberStatus.Active,
                                InvitedAt = now,
                                InvitedBy = adminEmail,
                                LastSeen = now,
                            },
                        },
                    }
                );
            }
        }

        /// <inheritdoc/>
        public void Invite(string byEmail, IEnumerable<SharingInvitation> invitations)
        {
            var list = invitations.ToList();
            Change(
                byEmail,
                record =>
                {
                    // Check them all before changing anything, so a bad one leaves the record
                    // as it was rather than half-updated.
                    for (var i = 0; i < list.Count; i++)
                    {
                        var email = list[i].Email;
                        if (FindMember(record, email) != null)
                            throw new SharingNotAllowedException($"{email} already has access.");
                        if (list.Skip(i + 1).Any(other => SameEmail(other.Email, email)))
                            throw new SharingNotAllowedException(
                                $"{email} is in the list more than once."
                            );
                    }
                    foreach (var invitation in list)
                    {
                        record.Members.Add(
                            new SharingMember
                            {
                                Email = invitation.Email.Trim(),
                                Role = invitation.Role,
                                Status = SharingMemberStatus.Invited,
                                InvitedAt = _utcNow(),
                                InvitedBy = byEmail,
                            }
                        );
                        // Inviting someone we were suggesting answers the suggestion.
                        record.DismissedSuggestions.RemoveAll(e => SameEmail(e, invitation.Email));
                    }
                }
            );
        }

        /// <inheritdoc/>
        public void SetRole(string byEmail, string email, SharingRole role)
        {
            Change(
                byEmail,
                record =>
                {
                    var member = RequireMember(record, email);
                    if (role != SharingRole.Admin)
                        RequireAnotherAdmin(record, member);
                    member.Role = role;
                }
            );
        }

        /// <inheritdoc/>
        public void Remove(string byEmail, string email)
        {
            Change(
                byEmail,
                record =>
                {
                    var member = RequireMember(record, email);
                    RequireAnotherAdmin(record, member);
                    record.Members.Remove(member);
                }
            );
        }

        /// <inheritdoc/>
        public void DismissSuggestions(string byEmail, IEnumerable<string> emails)
        {
            Change(
                byEmail,
                record =>
                {
                    foreach (var email in emails)
                    {
                        if (!record.DismissedSuggestions.Any(e => SameEmail(e, email)))
                            record.DismissedSuggestions.Add(email);
                    }
                }
            );
        }

        /// <inheritdoc/>
        public void RecordVisit(string email, string name)
        {
            lock (_lock)
            {
                var record = Read();
                var member = record == null ? null : FindMember(record, email);
                if (member == null)
                    return;
                member.Status = SharingMemberStatus.Active;
                member.LastSeen = _utcNow();
                if (!string.IsNullOrWhiteSpace(name))
                    member.Name = name;
                Write(record);
            }
        }

        // Apply a change that only an admin of an already-shared collection may make.
        private void Change(string byEmail, Action<CollectionSharingRecord> change)
        {
            lock (_lock)
            {
                var record = Read();
                if (record == null)
                    throw new SharingNotAllowedException("This collection is not shared.");
                if (FindMember(record, byEmail)?.Role != SharingRole.Admin)
                    throw new SharingNotAllowedException(
                        $"{byEmail} is not an admin of this collection."
                    );
                change(record);
                Write(record);
            }
        }

        private static SharingMember RequireMember(CollectionSharingRecord record, string email)
        {
            return FindMember(record, email)
                ?? throw new SharingNotAllowedException($"{email} is not a member.");
        }

        // A collection must always have an admin, so the member about to stop being one (by
        // demotion or removal) must not be the only one.
        private static void RequireAnotherAdmin(
            CollectionSharingRecord record,
            SharingMember member
        )
        {
            if (
                member.Role == SharingRole.Admin
                && record.Members.Count(m => m.Role == SharingRole.Admin) == 1
            )
                throw new SharingNotAllowedException(
                    "A shared collection must always have at least one admin."
                );
        }

        private static SharingMember FindMember(CollectionSharingRecord record, string email)
        {
            return record.Members.FirstOrDefault(m => SameEmail(m.Email, email));
        }

        private static bool SameEmail(string a, string b)
        {
            return string.Equals(a?.Trim(), b?.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        private CollectionSharingRecord Read()
        {
            if (!RobustFile.Exists(_filePath))
                return null;
            return JsonConvert.DeserializeObject<CollectionSharingRecord>(
                RobustFile.ReadAllText(_filePath)
            );
        }

        private void Write(CollectionSharingRecord record)
        {
            RobustFile.WriteAllText(
                _filePath,
                JsonConvert.SerializeObject(record, Formatting.Indented)
            );
        }
    }
}
