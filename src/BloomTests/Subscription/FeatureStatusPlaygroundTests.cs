using Bloom.SubscriptionAndFeatures;
using BloomTests.Book;
using NUnit.Framework;

namespace BloomTests.FeatureStatusTests
{
    /// <summary>
    /// A Playground book unlocks features as if the collection were Enterprise, except the few
    /// features the registry marks as still governed by the real subscription (BL-16855).
    /// </summary>
    [TestFixture]
    public class FeatureStatusPlaygroundTests : BookTestsBase
    {
        private const string kPlaygroundTemplateId = "aeb176bc-76fa-44e2-bb9d-6350698fce47";

        protected override string GetTestFolderName() => "FeatureStatusPlaygroundTests";

        private Bloom.Book.Book CreatePlaygroundBook()
        {
            var book = CreateBook();
            book.BookInfo.BookLineage = kPlaygroundTemplateId;
            // Sanity check that the lineage really makes this a Playground book.
            Assert.That(book.IsPlayground, Is.True);
            return book;
        }

        [Test]
        public void GetFeatureStatus_PlaygroundBook_UnlocksOrdinaryProFeatureWithoutSubscription()
        {
            var subscription = Subscription.CreateTempSubscriptionForTier(SubscriptionTier.Basic);
            var book = CreatePlaygroundBook();

            var status = FeatureStatus.GetFeatureStatus(subscription, FeatureName.Canvas, book);

            Assert.That(status.Enabled, Is.True);
        }

        [TestCase(FeatureName.AppBuilder)]
        [TestCase(FeatureName.TeamCollection)]
        public void GetFeatureStatus_PlaygroundBook_StillRequiresSubscriptionForExemptFeature(
            FeatureName featureName
        )
        {
            var subscription = Subscription.CreateTempSubscriptionForTier(SubscriptionTier.Basic);
            // Sanity check: an ordinary book gets the same answer, so the Playground unlock is what we
            // test. This must come first: CreateBook() reuses one BookInfo, which CreatePlaygroundBook()
            // then marks as Playground.
            Assert.That(
                FeatureStatus.GetFeatureStatus(subscription, featureName, CreateBook()).Enabled,
                Is.False
            );
            var book = CreatePlaygroundBook();

            var status = FeatureStatus.GetFeatureStatus(subscription, featureName, book);

            Assert.That(status.Enabled, Is.False);
        }

        [Test]
        public void GetFeatureStatus_PlaygroundBook_AppBuilderEnabledWithProSubscription()
        {
            var subscription = Subscription.CreateTempSubscriptionForTier(SubscriptionTier.Pro);
            var book = CreatePlaygroundBook();

            var status = FeatureStatus.GetFeatureStatus(subscription, FeatureName.AppBuilder, book);

            Assert.That(status.Enabled, Is.True);
        }
    }
}
