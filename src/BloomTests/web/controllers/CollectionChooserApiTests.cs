using System.Collections.Generic;
using System.Linq;
using Bloom.TeamCollection.Cloud;
using Bloom.web.controllers;
using NUnit.Framework;

namespace BloomTests.web.controllers
{
    /// <summary>
    /// Tests for CollectionChooserApi's pure join-card matching logic (dogfood batch 1, item 6;
    /// identity-aware since bug #11, John's ruling 13 Jul 2026): ComputeJoinCards decides, given
    /// the cloud collections the signed-in user belongs to, the local cloud copies on this
    /// machine (cloud id + last-known user, as gathered by GetLocalCloudCopies -- not exercised
    /// here since that half needs real files on disk), and the signed-in email, which of those
    /// cloud collections should get a "join card" in the collection chooser. A local copy only
    /// suppresses a card when it was most recently used by the SIGNED-IN account -- another
    /// account's copy (or one whose user was never recorded) must not hide this account's
    /// invitation. Follows SharingApiTests' pattern of testing internal static pure logic
    /// directly, no live server or filesystem required.
    /// </summary>
    [TestFixture]
    public class CollectionChooserApiTests
    {
        private const string kSignedInEmail = "bob@example.com";

        private static List<(string cloudCollectionId, string lastKnownUser)> NoLocalCopies() =>
            new List<(string cloudCollectionId, string lastKnownUser)>();

        [Test]
        public void ComputeJoinCards_NoCloudCollections_ReturnsEmpty()
        {
            var result = CollectionChooserApi.ComputeJoinCards(
                new List<CloudCollectionSummary>(),
                NoLocalCopies(),
                kSignedInEmail
            );

            Assert.That(result, Is.Empty);
        }

        [Test]
        public void ComputeJoinCards_CollectionWithNoLocalCopy_GetsAJoinCard()
        {
            var summary = new CloudCollectionSummary { Id = "cloud-1", Name = "Sunshine Books" };

            var result = CollectionChooserApi.ComputeJoinCards(
                new List<CloudCollectionSummary> { summary },
                NoLocalCopies(),
                kSignedInEmail
            );

            Assert.That(result.Count, Is.EqualTo(1));
            dynamic card = result[0];
            Assert.That(card.collectionId, Is.EqualTo("cloud-1"));
            Assert.That(card.title, Is.EqualTo("Sunshine Books"));
        }

        [Test]
        public void ComputeJoinCards_LocalCopyLastUsedByThisAccount_NoJoinCard()
        {
            var summary = new CloudCollectionSummary { Id = "cloud-1", Name = "Sunshine Books" };

            var result = CollectionChooserApi.ComputeJoinCards(
                new List<CloudCollectionSummary> { summary },
                new List<(string, string)> { ("cloud-1", kSignedInEmail) },
                kSignedInEmail
            );

            Assert.That(
                result,
                Is.Empty,
                "A local copy this same account most recently used means the collection is "
                    + "already joined here, so it should not also get a join card"
            );
        }

        [Test]
        public void ComputeJoinCards_LocalCopyLastUsedByThisAccountDifferentCase_NoJoinCard()
        {
            var summary = new CloudCollectionSummary { Id = "cloud-1", Name = "Sunshine Books" };

            var result = CollectionChooserApi.ComputeJoinCards(
                new List<CloudCollectionSummary> { summary },
                new List<(string, string)> { ("cloud-1", "BOB@Example.COM") },
                kSignedInEmail
            );

            Assert.That(result, Is.Empty, "email matching must be case-insensitive");
        }

        [Test]
        public void ComputeJoinCards_LocalCopyBelongsToAnotherAccount_StillGetsJoinCard()
        {
            // Bug #11 (found live, 13 Jul 2026): Bob, an invited member, got no join card
            // because ALICE's local copy of the collection was on this machine's chooser list.
            // Another account's copy must not hide this account's invitation.
            var summary = new CloudCollectionSummary { Id = "cloud-1", Name = "Sunshine Books" };

            var result = CollectionChooserApi.ComputeJoinCards(
                new List<CloudCollectionSummary> { summary },
                new List<(string, string)> { ("cloud-1", "alice@example.com") },
                kSignedInEmail
            );

            Assert.That(result.Count, Is.EqualTo(1));
            dynamic card = result[0];
            Assert.That(card.collectionId, Is.EqualTo("cloud-1"));
        }

        [Test]
        public void ComputeJoinCards_LocalCopyWithUnknownLastUser_StillGetsJoinCard()
        {
            // John's ruling: suppress only when the copy is KNOWN to be this account's. A copy
            // with no recorded last-known user (e.g. a manually copied folder) shows the card;
            // CloudJoinFlow's scenario matching handles any merge/conflict at actual join time.
            var summary = new CloudCollectionSummary { Id = "cloud-1", Name = "Sunshine Books" };

            var result = CollectionChooserApi.ComputeJoinCards(
                new List<CloudCollectionSummary> { summary },
                new List<(string, string)> { ("cloud-1", null) },
                kSignedInEmail
            );

            Assert.That(result.Count, Is.EqualTo(1));
        }

        [Test]
        public void ComputeJoinCards_MixOfOwnOtherAndUnjoined_OnlyOwnCopySuppresses()
        {
            var mine = new CloudCollectionSummary { Id = "cloud-mine", Name = "Already Have" };
            var alices = new CloudCollectionSummary { Id = "cloud-alices", Name = "Alices Copy" };
            var unjoined = new CloudCollectionSummary { Id = "cloud-new", Name = "New One" };

            var result = CollectionChooserApi.ComputeJoinCards(
                new List<CloudCollectionSummary> { mine, alices, unjoined },
                new List<(string, string)>
                {
                    ("cloud-mine", kSignedInEmail),
                    ("cloud-alices", "alice@example.com"),
                    ("some-other-unrelated-local-tc", kSignedInEmail),
                },
                kSignedInEmail
            );

            var ids = result.Select(c => (string)c.collectionId).ToList();
            Assert.That(ids, Does.Not.Contain("cloud-mine"));
            Assert.That(ids, Does.Contain("cloud-alices"));
            Assert.That(ids, Does.Contain("cloud-new"));
            Assert.That(result.Count, Is.EqualTo(2));
        }

        [Test]
        public void ComputeJoinCards_SameNameDifferentLocalFolder_StillGetsJoinCardBySameIdRule()
        {
            // Per the batch decision: matching is by cloud id ONLY. A local folder that happens to
            // share the cloud collection's display name but is NOT itself linked to that cloud id
            // (e.g. it's an unrelated plain collection, or a folder TC) does not suppress the join
            // card -- CloudJoinFlow's own scenario matching (merge-or-conflict) applies only once
            // the user actually tries to join.
            var summary = new CloudCollectionSummary { Id = "cloud-1", Name = "Sunshine Books" };

            var result = CollectionChooserApi.ComputeJoinCards(
                new List<CloudCollectionSummary> { summary },
                new List<(string, string)> { ("some-unrelated-cloud-id", kSignedInEmail) },
                kSignedInEmail
            );

            Assert.That(result.Count, Is.EqualTo(1));
            dynamic card = result[0];
            Assert.That(card.collectionId, Is.EqualTo("cloud-1"));
        }
    }
}
