using System;
using System.IO;
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
    }
}
