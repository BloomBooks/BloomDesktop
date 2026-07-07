using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Bloom.Collection;
using Bloom.CollectionCreating;
using Bloom.web;
using SIL.IO;

namespace Bloom.TeamCollection.Cloud
{
    /// <summary>One row from `my_collections()`: a collection the signed-in user is approved for.</summary>
    public class CloudCollectionSummary
    {
        public string Id;
        public string Name;
    }

    /// <summary>
    /// Which of the six local-vs-remote situations <see cref="CloudJoinFlow.DetermineScenario"/>
    /// found for a proposed local collection folder name. Adapted from the four-boolean matrix
    /// (isExistingCollection/isAlreadyTcCollection/isCurrentCollection/isSameCollection) that used
    /// to live only inside <see cref="FolderTeamCollection.ShowJoinCollectionTeamDialog"/>, per the
    /// task 05 brief ("six-scenario matching logic moved from FolderTeamCollection").
    /// </summary>
    public enum JoinScenario
    {
        /// <summary>No local folder by this name exists yet -- the ordinary case.</summary>
        FreshJoin,

        /// <summary>A local folder by this name exists and is already linked to this exact cloud
        /// collection -- joining again is a safe, idempotent reconnect/refresh.</summary>
        AlreadyJoinedSameCollection,

        /// <summary>A local folder by this name exists and is linked to a DIFFERENT cloud
        /// collection -- a real name collision, needs a human decision.</summary>
        LinkedToDifferentCloudCollection,

        /// <summary>A local folder by this name exists and is linked to a legacy folder-based Team
        /// Collection -- a type mismatch, needs a human decision.</summary>
        LinkedToFolderTeamCollection,

        /// <summary>A local folder by this name exists, is not a Team Collection at all, but its
        /// CollectionId happens to already match this cloud collection's id (e.g. it's a local copy
        /// that was made before this collection went cloud) -- safe to link up and Receive.</summary>
        PlainCollectionSameGuid,

        /// <summary>A local folder by this name exists, is not a Team Collection, and its
        /// CollectionId does not match -- an unrelated collection with a colliding name, needs a
        /// human decision.</summary>
        PlainCollectionDifferentGuid,
    }

    /// <summary>Thrown by <see cref="CloudJoinFlow.JoinCollection"/> for any scenario that needs a
    /// human decision rather than proceeding automatically. Wiring an interactive resolution dialog
    /// (the cloud equivalent of <see cref="FolderTeamCollection.ShowJoinCollectionTeamDialog"/>'s
    /// React dialog) is a UI-layer follow-up outside this task's file ownership; callers today can
    /// at least show <see cref="ApplicationException.Message"/> and ask the user to pick a different
    /// local collection name.</summary>
    public class CloudJoinConflictException : ApplicationException
    {
        public JoinScenario Scenario { get; }
        public string LocalCollectionFolder { get; }

        public CloudJoinConflictException(
            JoinScenario scenario,
            string localCollectionFolder,
            string message
        )
            : base(message)
        {
            Scenario = scenario;
            LocalCollectionFolder = localCollectionFolder;
        }
    }

    /// <summary>
    /// Drives joining (or creating) a cloud Team Collection: lists the collections the signed-in
    /// user is approved for (`my_collections`), resolves the local-vs-remote scenario for a proposed
    /// local collection name, and performs the local collection creation + first Receive. See
    /// Design/CloudTeamCollections.md's UI-changes summary ("cloud create dialog is sign-in ->
    /// confirm immutable name -> initial Send (no folder chooser, no restart)") and CONTRACTS.md's
    /// create_collection/my_collections RPCs.
    ///
    /// Note on collection-level files: a cloud collection's `.bloomCollection` file travels with it
    /// in the "other" collection-file group (PutCollectionFiles uploads it as part of
    /// RootLevelCollectionFilesIn, exactly like a folder TC's zipped settings) -- so joining does
    /// NOT need to fabricate a fresh CollectionSettings/NewCollectionSettings the way
    /// NewCollectionWizard does; downloading the "other" group (via the ordinary
    /// CopyRepoCollectionFilesToLocal path) brings the real settings file down, the same way
    /// FolderTeamCollection's join flow extracts one from the repo's project-files zip.
    /// </summary>
    public class CloudJoinFlow
    {
        private readonly CloudCollectionClient _client;

        public CloudJoinFlow(CloudCollectionClient client)
        {
            _client = client;
        }

        /// <summary>Lists the collections the signed-in user is approved for (CONTRACTS.md's
        /// my_collections -- includes unclaimed-but-approved rows, per that RPC's own doc).</summary>
        public IReadOnlyList<CloudCollectionSummary> ListMyCollections()
        {
            return _client
                .MyCollections()
                .OfType<Newtonsoft.Json.Linq.JObject>()
                .Select(o => new CloudCollectionSummary
                {
                    Id = (string)o["id"],
                    Name = (string)o["name"],
                })
                .ToList();
        }

        /// <summary>Where a collection with this display name would live locally, mirroring
        /// <see cref="FolderTeamCollection.ShowJoinCollectionTeamDialog"/>'s own naming
        /// convention.</summary>
        public static string DetermineLocalCollectionFolder(string collectionName) =>
            Path.Combine(NewCollectionWizard.DefaultParentDirectoryForCollections, collectionName);

        /// <summary>
        /// Classifies the local-vs-remote situation for joining <paramref name="collectionId"/>
        /// under the local name <paramref name="collectionName"/> would produce, without changing
        /// anything on disk.
        /// </summary>
        public JoinScenario DetermineScenario(
            string collectionId,
            string collectionName,
            out string localCollectionFolder
        )
        {
            localCollectionFolder = DetermineLocalCollectionFolder(collectionName);
            if (!Directory.Exists(localCollectionFolder))
                return JoinScenario.FreshJoin;

            var tcLinkPath = TeamCollectionManager.GetTcLinkPathFromLcPath(localCollectionFolder);
            if (RobustFile.Exists(tcLinkPath))
            {
                var link = TeamCollectionLink.FromFile(tcLinkPath);
                if (link != null && link.IsCloud)
                    return link.CloudCollectionId == collectionId
                        ? JoinScenario.AlreadyJoinedSameCollection
                        : JoinScenario.LinkedToDifferentCloudCollection;
                return JoinScenario.LinkedToFolderTeamCollection;
            }

            var localGuid = CollectionSettings.CollectionIdFromCollectionFolder(
                localCollectionFolder
            );
            return localGuid == collectionId
                ? JoinScenario.PlainCollectionSameGuid
                : JoinScenario.PlainCollectionDifferentGuid;
        }

        /// <summary>
        /// Joins <paramref name="collectionId"/>: creates (or reuses) the local collection folder,
        /// writes TeamCollectionLink.txt, downloads the collection-level files (which include the
        /// .bloomCollection file itself) and every book. Throws
        /// <see cref="CloudJoinConflictException"/> for any scenario needing a human decision.
        /// </summary>
        public CloudTeamCollection JoinCollection(
            string collectionId,
            string collectionName,
            ITeamCollectionManager manager,
            BookCollectionHolder bookCollectionHolder = null,
            IWebSocketProgress progress = null
        )
        {
            // An approved-but-unclaimed member (admin added their EMAIL; members.user_id is
            // still NULL) can SEE the collection via my_collections (email match) but is not
            // yet a member for RLS purposes: every member-gated RPC the join needs fails with
            // not_a_member until claim_memberships() stamps their user_id. Claiming here is
            // idempotent and covers the by-design first-contact moment (CONTRACTS.md: claiming
            // requires a verified email, which sign-in has already established).
            // Found by the first live two-instance smoke test, 7 Jul 2026.
            _client.ClaimMemberships();

            var scenario = DetermineScenario(
                collectionId,
                collectionName,
                out var localCollectionFolder
            );
            switch (scenario)
            {
                case JoinScenario.FreshJoin:
                case JoinScenario.PlainCollectionSameGuid:
                    Directory.CreateDirectory(localCollectionFolder);
                    break;
                case JoinScenario.AlreadyJoinedSameCollection:
                    break; // just reconnect/refresh below -- idempotent.
                case JoinScenario.LinkedToDifferentCloudCollection:
                    throw new CloudJoinConflictException(
                        scenario,
                        localCollectionFolder,
                        $"There is already a different Team Collection called \"{collectionName}\" on this computer. Please choose a different name, or remove the existing one first."
                    );
                case JoinScenario.LinkedToFolderTeamCollection:
                    throw new CloudJoinConflictException(
                        scenario,
                        localCollectionFolder,
                        $"\"{collectionName}\" is already linked to a different (folder-based) Team Collection on this computer. Please choose a different name."
                    );
                case JoinScenario.PlainCollectionDifferentGuid:
                    throw new CloudJoinConflictException(
                        scenario,
                        localCollectionFolder,
                        $"There is already a collection called \"{collectionName}\" on this computer. Please choose a different name."
                    );
            }

            progress?.MessageWithParams(
                "JoiningCloudCollection",
                "",
                "Joining \"{0}\"...",
                ProgressKind.Progress,
                collectionName
            );

            var linkPath = TeamCollectionManager.GetTcLinkPathFromLcPath(localCollectionFolder);
            if (!RobustFile.Exists(linkPath))
                TeamCollectionLink.ForCloud(collectionId).WriteToFile(linkPath);

            var cloudTc = new CloudTeamCollection(
                manager,
                localCollectionFolder,
                collectionId,
                bookCollectionHolder: bookCollectionHolder
            );
            cloudTc.HydrateFromServer();
            cloudTc.CopyRepoCollectionFilesToLocal(localCollectionFolder);
            cloudTc.CopyAllBooksFromRepoToLocalFolder(localCollectionFolder);
            return cloudTc;
        }

        /// <summary>
        /// Creates a brand-new cloud collection (create_collection -- caller becomes its sole
        /// claimed admin) and immediately joins it locally. Matches the design doc's cloud-create
        /// flow: "sign-in -> confirm immutable name -> initial Send (no folder chooser, no
        /// restart)" -- the "initial Send" itself (pushing an existing local collection's books up)
        /// is done by the caller via the ordinary SynchronizeBooksFromLocalToRepo/PutBook path once
        /// this method returns a working CloudTeamCollection, exactly as
        /// FolderTeamCollection.SetupTeamCollection does for a folder TC.
        /// </summary>
        public CloudTeamCollection CreateAndJoinCollection(
            string collectionName,
            ITeamCollectionManager manager,
            BookCollectionHolder bookCollectionHolder = null
        )
        {
            var collectionId = Guid.NewGuid().ToString();
            _client.CreateCollection(collectionId, collectionName);
            return JoinCollection(collectionId, collectionName, manager, bookCollectionHolder);
        }
    }
}
