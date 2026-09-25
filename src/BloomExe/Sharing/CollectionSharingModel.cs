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

        /// <summary>When an admin gave this person access (UTC).</summary>
        [JsonProperty("invitedAt")]
        public DateTime InvitedAt;

        /// <summary>The email of the admin who gave this person access.</summary>
        [JsonProperty("invitedBy")]
        public string InvitedBy;

        /// <summary>
        /// The last time we know this person used the collection (UTC): when they last opened it
        /// while signed in, or, for someone who was given access because the old Team
        /// Collection's history shows them working in it, their last action recorded there.
        /// Null if we have no record of their ever using it; the UI then shows when they were
        /// invited instead.
        /// </summary>
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
    /// Someone the current (folder) Team Collection's history shows has worked in it. When such
    /// a collection starts being shared, each of them is given access along with the admin who
    /// shares it.
    /// </summary>
    public class TeamCollectionHistoryMember
    {
        public string Email;

        /// <summary>The name recorded with their most recent action that has one, if any.</summary>
        public string Name;

        /// <summary>Admin if the old Team Collection listed them as an administrator.</summary>
        public SharingRole Role;

        /// <summary>The time of their most recent recorded action (UTC).</summary>
        public DateTime LastActivity;
    }
}
