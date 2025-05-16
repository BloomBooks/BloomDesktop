using Moq;
using NUnit.Framework;
using Bloom.SubscriptionAndFeatures;

namespace BloomTests.FeatureStatusTests
{
    [TestFixture]
    public class FeatureStatusTests
    {
        [SetUp]
        public void Setup()
        {
            L10NSharp.LocalizationManager.SetUILanguage("en", false); // review
        }

        [TestCase(SubscriptionTier.Basic, SubscriptionTier.Pro, FeatureName.Overlay, false)] // Basic subscription cannot access Pro tier feature
        [TestCase(
            SubscriptionTier.Basic,
            SubscriptionTier.LocalCommunity,
            FeatureName.TeamCollection,
            false
        )] // Basic subscription cannot access LocalCommunity tier feature
        [TestCase(
            SubscriptionTier.Basic,
            SubscriptionTier.Enterprise,
            FeatureName.PrintShopReady,
            false
        )] // Basic subscription cannot access Enterprise tier feature
        [TestCase(SubscriptionTier.Pro, SubscriptionTier.Pro, FeatureName.Overlay, true)] // Pro subscription can access Pro tier feature
        [TestCase(
            SubscriptionTier.Enterprise,
            SubscriptionTier.LocalCommunity,
            FeatureName.TeamCollection,
            true
        )] // Enterprise can access LocalCommunity tier feature
        [TestCase(SubscriptionTier.Enterprise, SubscriptionTier.Pro, FeatureName.Overlay, true)] // Enterprise subscription can access Pro tier feature
        public void GetFeatureStatus_Using_Enum(
            SubscriptionTier currentTier,
            SubscriptionTier minimalFeatureTier,
            FeatureName featureEnum,
            bool expectedEnabled
        )
        {
            // Arrange
            var subscription = Subscription.ForUnitTestWithOverrideTier(currentTier);

            // Act - Overlay feature with Basic subscription
            var status = FeatureStatus.GetFeatureUseStatus(subscription, featureEnum);

            // Assert
            Assert.That(status, Is.Not.Null);
            Assert.That(status.FeatureName, Is.EqualTo(featureEnum));
            Assert.That(status.SubscriptionTier, Is.EqualTo(minimalFeatureTier));
            Assert.That(status.Enabled, Is.EqualTo(expectedEnabled)); // Basic subscription cannot access Overlay tier
            Assert.That(status.Visible, Is.True);
        }

        [TestCase(SubscriptionTier.Basic, SubscriptionTier.Pro, "OveRLay", false)] // Basic subscription cannot access Pro tier feature
        [TestCase(SubscriptionTier.Enterprise, SubscriptionTier.Pro, "OVerlay", true)]
        [TestCase(SubscriptionTier.Pro, SubscriptionTier.Pro, "overlAY", true)]
        public void GetFeatureStatus_Using_String(
            SubscriptionTier currentTier,
            SubscriptionTier minimalFeatureTier,
            string featureName,
            bool expectedEnabled
        )
        {
            // Arrange
            var subscription = Subscription.ForUnitTestWithOverrideTier(currentTier);

            // Act - Overlay feature with Basic subscription
            var status = FeatureStatus.GetFeatureStatus(subscription, featureName);

            // Assert
            Assert.That(status, Is.Not.Null);
            Assert.That(status.FeatureName, Is.EqualTo(FeatureName.Overlay));
            Assert.That(status.SubscriptionTier, Is.EqualTo(minimalFeatureTier));
            Assert.That(status.Enabled, Is.EqualTo(expectedEnabled)); // Basic subscription cannot access Overlay tier
            Assert.That(status.Visible, Is.True);
        }

        [Test]
        public void ToJson_CorrectlyFormatsSubscriptionTier()
        {
            // Arrange
            var subscription = Subscription.ForUnitTestWithOverrideTier(SubscriptionTier.Pro);
            var status = FeatureStatus.GetFeatureUseStatus(subscription, FeatureName.Motion);

            // Act
            string json = status.ToJson();

            // Assert
            StringAssert.Contains("\"localizedFeature\":\"Motion\"", json);
            StringAssert.Contains("\"localizedTier\":\"Pro\"", json);
            StringAssert.Contains("\"subscriptionTier\":\"Pro\"", json);
            StringAssert.Contains("\"enabled\":true", json);
            StringAssert.Contains("\"visible\":true", json);
        }

        [Test]
        public void ForSerialization_ReturnsValidObject()
        {
            // Arrange
            var subscription = Subscription.ForUnitTestWithOverrideTier(SubscriptionTier.Pro);
            var status = FeatureStatus.GetFeatureUseStatus(subscription, FeatureName.Motion);

            // Act
            var serializableObject = status.ForSerialization();
            dynamic dynamicObject = serializableObject;

            // Assert
            Assert.That((string)dynamicObject.localizedTier, Is.EqualTo("Pro"));
            Assert.That((string)dynamicObject.localizedFeature, Is.EqualTo("Motion"));
            Assert.That((string)dynamicObject.subscriptionTier, Is.EqualTo("Pro"));
            Assert.That((bool)dynamicObject.enabled, Is.EqualTo(true));
            Assert.That((bool)dynamicObject.visible, Is.EqualTo(true));
            Assert.That(dynamicObject.firstPageNumber, Is.Empty);
        }

        [TestCase(SubscriptionTier.Enterprise, null, null)] // nothing is invalid at the enterprise level
        [TestCase(SubscriptionTier.Basic, FeatureName.Overlay, "2")] // overlay is invalid at the basic level
        public void GetFirstFeatureThatIsInvalidForNewBooks(
            SubscriptionTier tier,
            FeatureName? featureName, // Make the enum parameter nullable
            string expectedPageNumber
        )
        {
            var subscription = Subscription.ForUnitTestWithOverrideTier(tier);

            var dom = new Bloom.Book.HtmlDom(
                @"<html><body>
				<div class='bloom-page' data-page-number='1'/>
				<div class='bloom-page' data-page-number='2'><div class='bloom-canvas-element'/></div>
                <div class='bloom-page' data-page-number='3'><div class='bloom-canvas-element'/></div>
			 </body></html>"
            );

            var featureStatus = FeatureStatus.GetFirstFeatureThatIsInvalidForNewBooks(
                subscription,
                dom.RawDom
            );

            // Assert
            if (featureName == null)
            {
                Assert.That(featureStatus, Is.Null);
            }
            else
            {
                Assert.That(featureStatus.FeatureName, Is.EqualTo(featureName));
                Assert.That(featureStatus.Enabled, Is.False);
                Assert.That(
                    featureStatus.FirstPageNumber,
                    Is.EqualTo(expectedPageNumber.ToString())
                );
            }
        }
    }
}
