using System;
using System.IO;
using System.Linq;
using Bloom.TeamCollection;
using Bloom.TeamCollection.Cloud;
using BloomTemp;
using BloomTests.DataBuilders;
using Moq;
using NUnit.Framework;
using SIL.IO;

namespace BloomTests.TeamCollection.Cloud
{
    /// <summary>
    /// [Explicit] live-infrastructure tests against the real local dev stack (Supabase Postgres/
    /// PostgREST + edge functions + MinIO), per task 05's brief ("NUnit live tests against the stack
    /// are strongly encouraged for the Send/Receive round trip; see CloudAuthTests' LiveDevProvider
    /// test for the pattern"). Requires:
    ///   1. `supabase start` (Postgres/PostgREST/GoTrue up).
    ///   2. `supabase functions serve --env-file server/dev/functions.env` (edge functions up --
    ///      NOT running by default; must be started separately, in the background).
    ///   3. BLOOM_CLOUDTC_ANON_KEY (and friends) exported per server/dev/README.md, so
    ///      CloudEnvironment.FromEnvironment() picks up the real local stack's anon key.
    /// Run manually: `dotnet test --filter FullyQualifiedName~CloudTeamCollectionLiveTests`.
    ///
    /// Uses a single dev account (alice) for both "sides" of the round trip -- two separate local
    /// collection folders and two separate CloudTeamCollection instances against the SAME cloud
    /// collection, simulating two Bloom instances on one machine (the manual test scenario in the
    /// task file's Acceptance section). Using a second account (bob) would additionally need a
    /// members_add call to approve bob for the freshly-created collection first; CONTRACTS.md's
    /// exact `members` RPC names are a guess in this task (see the final report), so that's left as
    /// a follow-up rather than guessed at here.
    /// </summary>
    [TestFixture]
    public class CloudTeamCollectionLiveTests
    {
        [Test]
        [Explicit(
            "Requires the local Supabase dev stack running, INCLUDING `supabase functions serve --env-file server/dev/functions.env` (not started by `supabase start` alone)."
        )]
        public void LiveStack_SendThenReceive_RoundTripsBookContentAndLock()
        {
            var environment = CloudEnvironment.FromEnvironment();
            var senderAuth = new CloudAuth(new DevCloudAuthProvider(environment));
            senderAuth.SignIn("alice@dev.local", "BloomDev123!");
            var senderClient = new CloudCollectionClient(environment, senderAuth);

            var collectionId = Guid.NewGuid().ToString();
            var collectionName = "Live round trip " + collectionId.Substring(0, 8);
            senderClient.CreateCollection(collectionId, collectionName);

            TeamCollectionManager.ForceCurrentUserForTests("alice@dev.local");
            var mockManager = new Mock<ITeamCollectionManager>();

            using var senderFolder = new TemporaryFolder("CloudLive_Sender");
            var sender = new CloudTeamCollection(
                mockManager.Object,
                senderFolder.FolderPath,
                collectionId,
                environment: environment,
                auth: senderAuth,
                client: senderClient
            );

            var bookFolderPath = new BookFolderBuilder()
                .WithRootFolder(senderFolder.FolderPath)
                .WithTitle("Live book")
                .WithHtm("<html><body>hello from the live round-trip test</body></html>")
                .Build()
                .BuiltBookFolderPath;

            // Act: Send (checked in, so the lock is released -- a teammate should see it immediately).
            var sentStatus = sender.PutBook(bookFolderPath, checkin: true);

            Assert.That(sentStatus.checksum, Is.Not.Null.And.Not.Empty);
            Assert.That(
                sentStatus.lockedBy,
                Is.Null.Or.Empty,
                "checked-in Send should leave the book unlocked"
            );

            // Act: Receive, from a second local folder / CloudTeamCollection instance (simulating a
            // second machine, same account -- see class doc for why not a second account).
            using var receiverFolder = new TemporaryFolder("CloudLive_Receiver");
            var receiverAuth = new CloudAuth(new DevCloudAuthProvider(environment));
            receiverAuth.SignIn("alice@dev.local", "BloomDev123!");
            var receiverClient = new CloudCollectionClient(environment, receiverAuth);
            var receiver = new CloudTeamCollection(
                mockManager.Object,
                receiverFolder.FolderPath,
                collectionId,
                environment: environment,
                auth: receiverAuth,
                client: receiverClient
            );

            receiver.CopyAllBooksFromRepoToLocalFolder(receiverFolder.FolderPath);

            var receivedHtmlPath = Path.Combine(
                receiverFolder.FolderPath,
                "Live book",
                "Live book.htm"
            );
            Assert.That(
                RobustFile.Exists(receivedHtmlPath),
                Is.True,
                "the Received book folder should contain the .htm file"
            );
            Assert.That(
                RobustFile.ReadAllText(receivedHtmlPath),
                Does.Contain("hello from the live round-trip test")
            );
            Assert.That(
                receiver.GetChecksum("Live book"),
                Is.EqualTo(sentStatus.checksum),
                "Received checksum should match what was Sent"
            );

            // Act + Assert: lock round trip -- checkout on the receiver side should be visible from
            // the sender side after a fresh hydrate.
            var checkedOut = receiver.AttemptLock("Live book");
            Assert.That(checkedOut, Is.True);
            sender.HydrateFromServer();
            Assert.That(sender.WhoHasBookLocked("Live book"), Is.EqualTo("alice@dev.local"));

            receiver.UnlockBook("Live book");
        }

        // Replicates the first two-instance smoke test's check-in failure (7 Jul 2026): an
        // UPDATE Send of an existing book, performed by a FRESH CloudTeamCollection whose cache
        // was hydrated from the server (so cached books have no per-file Manifest) -- i.e.
        // exactly what happens when a user reopens a cloud collection and checks in an edit.
        // The original round-trip test above only exercises the first-ever Send of a new book.
        [Test]
        [Explicit(
            "Requires the local Supabase dev stack running, INCLUDING `supabase functions serve --env-file server/dev/functions.env` (not started by `supabase start` alone)."
        )]
        public void LiveStack_UpdateSendAfterFreshHydrate_Succeeds()
        {
            var environment = CloudEnvironment.FromEnvironment();
            var auth = new CloudAuth(new DevCloudAuthProvider(environment));
            auth.SignIn("alice@dev.local", "BloomDev123!");
            var client = new CloudCollectionClient(environment, auth);

            var collectionId = Guid.NewGuid().ToString();
            client.CreateCollection(
                collectionId,
                "Live update send " + collectionId.Substring(0, 8)
            );

            TeamCollectionManager.ForceCurrentUserForTests("alice@dev.local");
            var mockManager = new Mock<ITeamCollectionManager>();

            using var folder = new TemporaryFolder("CloudLive_UpdateSend");
            var firstSession = new CloudTeamCollection(
                mockManager.Object,
                folder.FolderPath,
                collectionId,
                environment: environment,
                auth: auth,
                client: client
            );
            var bookFolderPath = new BookFolderBuilder()
                .WithRootFolder(folder.FolderPath)
                .WithTitle("Update book")
                .WithHtm("<html><body>version one</body></html>")
                .Build()
                .BuiltBookFolderPath;
            firstSession.PutBook(bookFolderPath, checkin: true);

            // A brand-new instance over the same local folder: rebuilds its cache from the
            // server (books have version rows but NO per-file manifests), like reopening Bloom.
            var secondSession = new CloudTeamCollection(
                mockManager.Object,
                folder.FolderPath,
                collectionId,
                environment: environment,
                auth: auth,
                client: client
            );
            secondSession.HydrateFromServer();

            Assert.That(secondSession.AttemptLock("Update book"), Is.True, "sanity: checkout");
            Assert.That(
                secondSession.OkToCheckIn("Update book"),
                Is.True,
                "OkToCheckIn must recognize the signed-in account's own checkout "
                    + "(it compared against the registration email in the smoke-test failure)"
            );
            var htmPath = Path.Combine(bookFolderPath, "Update book.htm");
            RobustFile.WriteAllText(htmPath, "<html><body>version two</body></html>");

            var status = secondSession.PutBook(bookFolderPath, checkin: true);

            Assert.That(status.lockedBy, Is.Null.Or.Empty, "check-in should release the lock");
            var manifest = secondSession.Client.GetBookManifest(
                secondSession.TryGetBookIdForTests("Update book")
            );
            Assert.That((long)manifest["seq"], Is.EqualTo(2), "the update should be version 2");
            // The manifest must contain the FULL file list, not just what changed: the
            // send-only-changed-paths client bug committed a version whose manifest was
            // EMPTY (nothing had changed vs the stale local diff) in the smoke test, and
            // this assertion is what would have caught it.
            var files = (Newtonsoft.Json.Linq.JArray)manifest["files"];
            Assert.That(
                files.Count,
                Is.GreaterThanOrEqualTo(2),
                "v2's manifest must carry the unchanged files too, not just the edited one"
            );
            Assert.That(files.Select(f => (string)f["path"]), Does.Contain("Update book.htm"));
        }
    }
}
