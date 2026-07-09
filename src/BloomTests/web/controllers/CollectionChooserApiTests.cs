using System.Collections.Generic;
using System.Linq;
using Bloom.TeamCollection.Cloud;
using Bloom.web.controllers;
using NUnit.Framework;

namespace BloomTests.web.controllers
{
    /// <summary>
    /// Tests for CollectionChooserApi's pure join-card matching logic (dogfood batch 1, item 6):
    /// ComputeJoinCards decides, given the cloud collections the signed-in user belongs to and the
    /// set of cloud collection ids that already have a local copy (as gathered by
    /// GetLocalCloudCollectionIds, which reads TeamCollectionLink.txt files -- not exercised here
    /// since that half needs real files on disk), which of those cloud collections should get a
    /// "join card" in the collection chooser. Follows SharingApiTests' pattern of testing internal
    /// static pure logic directly, no live server or filesystem required.
    /// </summary>
    [TestFixture]
    public class CollectionChooserApiTests
    {
        [Test]
        public void ComputeJoinCards_NoCloudCollections_ReturnsEmpty()
        {
            var result = CollectionChooserApi.ComputeJoinCards(
                new List<CloudCollectionSummary>(),
                new HashSet<string>()
            );

            Assert.That(result, Is.Empty);
        }

        [Test]
        public void ComputeJoinCards_CollectionWithNoLocalCopy_GetsAJoinCard()
        {
            var summary = new CloudCollectionSummary { Id = "cloud-1", Name = "Sunshine Books" };

            var result = CollectionChooserApi.ComputeJoinCards(
                new List<CloudCollectionSummary> { summary },
                new HashSet<string>() // no local copies at all
            );

            Assert.That(result.Count, Is.EqualTo(1));
            dynamic card = result[0];
            Assert.That(card.collectionId, Is.EqualTo("cloud-1"));
            Assert.That(card.title, Is.EqualTo("Sunshine Books"));
        }

        [Test]
        public void ComputeJoinCards_CollectionAlreadyHasLocalCopy_NoJoinCard()
        {
            var summary = new CloudCollectionSummary { Id = "cloud-1", Name = "Sunshine Books" };

            var result = CollectionChooserApi.ComputeJoinCards(
                new List<CloudCollectionSummary> { summary },
                new HashSet<string> { "cloud-1" }
            );

            Assert.That(
                result,
                Is.Empty,
                "A cloud collection with a matching local TeamCollectionLink.txt id already has a "
                    + "local copy, so it should not also get a join card"
            );
        }

        [Test]
        public void ComputeJoinCards_MixOfJoinedAndUnjoined_OnlyUnjoinedGetCards()
        {
            var joined = new CloudCollectionSummary { Id = "cloud-joined", Name = "Already Have" };
            var unjoined1 = new CloudCollectionSummary { Id = "cloud-new-1", Name = "New One" };
            var unjoined2 = new CloudCollectionSummary { Id = "cloud-new-2", Name = "New Two" };

            var result = CollectionChooserApi.ComputeJoinCards(
                new List<CloudCollectionSummary> { joined, unjoined1, unjoined2 },
                new HashSet<string> { "cloud-joined", "some-other-unrelated-local-tc" }
            );

            var ids = result.Select(c => (string)c.collectionId).ToList();
            Assert.That(ids, Does.Not.Contain("cloud-joined"));
            Assert.That(ids, Does.Contain("cloud-new-1"));
            Assert.That(ids, Does.Contain("cloud-new-2"));
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
                new HashSet<string> { "some-unrelated-cloud-id" }
            );

            Assert.That(result.Count, Is.EqualTo(1));
            dynamic card = result[0];
            Assert.That(card.collectionId, Is.EqualTo("cloud-1"));
        }
    }
}
