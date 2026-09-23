using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;

namespace Bloom.Sharing
{
    /// <summary>
    /// What a person may do in a shared collection. Serialized as lowercase strings
    /// ("admin", "editor"), which is also what the front end (sharingApi.ts) uses.
    /// </summary>
    [JsonConverter(typeof(StringEnumConverter), typeof(CamelCaseNamingStrategy))]
    public enum SharingRole
    {
        /// <summary>An editor who may also change collection settings and manage sharing.</summary>
        Admin,

        /// <summary>May add, remove, and edit books.</summary>
        Editor,
    }

    /// <summary>
    /// Whether a person has taken up their invitation. Serialized as lowercase strings.
    /// </summary>
    [JsonConverter(typeof(StringEnumConverter), typeof(CamelCaseNamingStrategy))]
    public enum SharingMemberStatus
    {
        /// <summary>Invited by email, but nobody has yet signed in with that email.</summary>
        Invited,

        /// <summary>Someone has signed in with this email and used the collection.</summary>
        Active,
    }

    /// <summary>
    /// One person with access to a shared collection.
    /// </summary>
    public class SharingMember
    {
        [JsonProperty("email")]
        public string Email;

        /// <summary>The person's name, when we know it; the UI falls back to the email.</summary>
        [JsonProperty("name")]
        public string Name;

        [JsonProperty("role")]
        public SharingRole Role;

        [JsonProperty("status")]
        public SharingMemberStatus Status;

        /// <summary>When the invitation was made (UTC).</summary>
        [JsonProperty("invitedAt")]
        public DateTime InvitedAt;

        /// <summary>The email of the admin who made the invitation.</summary>
        [JsonProperty("invitedBy")]
        public string InvitedBy;

        /// <summary>When this person last opened the collection (UTC); null if never.</summary>
        [JsonProperty("lastSeen")]
        public DateTime? LastSeen;
    }

    /// <summary>
    /// The sharing record of one collection: who may use it and in what role. Once the cloud
    /// backend is live this is the server's record of the collection; until then
    /// LocalFileSharingStore keeps it in a file in the collection folder.
    /// </summary>
    public class CollectionSharingRecord
    {
        [JsonProperty("collectionId")]
        public string CollectionId;

        [JsonProperty("collectionName")]
        public string CollectionName;

        /// <summary>When sharing was first set up (UTC).</summary>
        [JsonProperty("createdAt")]
        public DateTime CreatedAt;

        [JsonProperty("members")]
        public List<SharingMember> Members = new List<SharingMember>();

        /// <summary>
        /// Emails an admin chose not to invite when offered the people found in the old Team
        /// Collection's history, so we stop offering them.
        /// </summary>
        [JsonProperty("dismissedSuggestions")]
        public List<string> DismissedSuggestions = new List<string>();
    }

    /// <summary>
    /// A request to invite one person, as the Share dialog sends it.
    /// </summary>
    public class SharingInvitation
    {
        [JsonProperty("email")]
        public string Email;

        [JsonProperty("role")]
        public SharingRole Role;
    }

    /// <summary>
    /// Someone who has worked in the current (folder) Team Collection, found in its history,
    /// whom an admin may want to invite to the shared collection.
    /// </summary>
    public class SharingSuggestion
    {
        [JsonProperty("email")]
        public string Email;

        [JsonProperty("name")]
        public string Name;

        /// <summary>Admin if the old Team Collection listed them as an administrator.</summary>
        [JsonProperty("role")]
        public SharingRole Role;

        /// <summary>The time of their most recent recorded action (UTC).</summary>
        [JsonProperty("lastActivity")]
        public DateTime LastActivity;
    }
}
