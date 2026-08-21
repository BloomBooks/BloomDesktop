using Bloom;
using Bloom.SubscriptionAndFeatures;
using NUnit.Framework;

namespace BloomTests.FeatureStatusTests
{
    [TestFixture]
    public class FeatureStatusTests
    {
        [SetUp]
        public void Setup()
        {
            L10NSharp.LocalizationManager.SetUILanguage("en"); // review
        }

        [TestCase(SubscriptionTier.Basic, SubscriptionTier.Pro, FeatureName.Canvas, false)] // Basic subscription cannot access Pro tier feature
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
        [TestCase(SubscriptionTier.Pro, SubscriptionTier.Pro, FeatureName.Canvas, true)] // Pro subscription can access Pro tier feature
        [TestCase(
            SubscriptionTier.Enterprise,
            SubscriptionTier.LocalCommunity,
            FeatureName.TeamCollection,
            true
        )] // Enterprise can access LocalCommunity tier feature
        [TestCase(SubscriptionTier.Enterprise, SubscriptionTier.Pro, FeatureName.Canvas, true)] // Enterprise subscription can access Pro tier feature
        public void GetFeatureStatus_Using_Enum(
            SubscriptionTier currentTier,
            SubscriptionTier minimalFeatureTier,
            FeatureName featureEnum,
            bool expectedEnabled
        )
        {
            // Arrange
            var subscription = Subscription.CreateTempSubscriptionForTier(currentTier);

            // Act - Canvas feature with Basic subscription
            var status = FeatureStatus.GetFeatureStatus(subscription, featureEnum);

            // Assert
            Assert.That(status, Is.Not.Null);
            Assert.That(status.FeatureName, Is.EqualTo(featureEnum));
            Assert.That(status.SubscriptionTier, Is.EqualTo(minimalFeatureTier));
            Assert.That(status.Enabled, Is.EqualTo(expectedEnabled)); // Basic subscription cannot access Canvas tier
            Assert.That(status.Visible, Is.True);
        }

        [TestCase(SubscriptionTier.Basic, SubscriptionTier.Pro, "CanVas", false)] // Basic subscription cannot access Pro tier feature
        [TestCase(SubscriptionTier.Enterprise, SubscriptionTier.Pro, "CANvas", true)]
        [TestCase(SubscriptionTier.Pro, SubscriptionTier.Pro, "canvAS", true)]
        public void GetFeatureStatus_Using_String(
            SubscriptionTier currentTier,
            SubscriptionTier minimalFeatureTier,
            string featureName,
            bool expectedEnabled
        )
        {
            // Arrange
            var subscription = Subscription.CreateTempSubscriptionForTier(currentTier);

            // Act - Canvas feature with Basic subscription
            var status = FeatureStatus.GetFeatureStatus(subscription, featureName);

            // Assert
            Assert.That(status, Is.Not.Null);
            Assert.That(status.FeatureName, Is.EqualTo(FeatureName.Canvas));
            Assert.That(status.SubscriptionTier, Is.EqualTo(minimalFeatureTier));
            Assert.That(status.Enabled, Is.EqualTo(expectedEnabled)); // Basic subscription cannot access Canvas tier
            Assert.That(status.Visible, Is.True);
        }

        [Test]
        public void ToJson_CorrectlyFormatsSubscriptionTier()
        {
            // Arrange
            var subscription = Subscription.CreateTempSubscriptionForTier(SubscriptionTier.Pro);
            var status = FeatureStatus.GetFeatureStatus(subscription, FeatureName.Motion);

            // Act
            string json = status.ToJson();

            // Assert
            StringAssert.Contains("\"localizedFeature\":\"Motion\"", json);
            StringAssert.Contains("\"localizedTier\":\"Pro\"", json);
            StringAssert.Contains("\"subscriptionTier\":\"Pro\"", json);
            StringAssert.Contains("\"enabled\":true", json);
            StringAssert.Contains("\"visible\":true", json);
        }

        // No feature in the registry is currently gated by an experimental token, so these two
        // tests exercise the mechanism itself with a FeatureInfo made up here. The token is one
        // no real feature uses, so flipping it cannot disturb any other test.
        private const string kTestExperimentalToken = "test-only-experimental-feature";

        private static FeatureInfo MakeExperimentalProFeature()
        {
            return new FeatureInfo
            {
                Feature = FeatureName.AppBuilder,
                SubscriptionTier = SubscriptionTier.Pro,
                ExperimentalFeatureToken = kTestExperimentalToken,
            };
        }

        [Test]
        public void GetFeatureStatus_ExperimentalFeatureHiddenUnlessEnabled()
        {
            var subscription = Subscription.CreateTempSubscriptionForTier(SubscriptionTier.Pro);
            var feature = MakeExperimentalProFeature();

            ExperimentalFeatures.SetValue(kTestExperimentalToken, false);
            var hiddenStatus = FeatureStatus.GetFeatureStatus(subscription, feature);

            ExperimentalFeatures.SetValue(kTestExperimentalToken, true);
            var visibleStatus = FeatureStatus.GetFeatureStatus(subscription, feature);

            ExperimentalFeatures.SetValue(kTestExperimentalToken, false);

            Assert.That(hiddenStatus.Visible, Is.False);
            Assert.That(hiddenStatus.Enabled, Is.True);
            Assert.That(visibleStatus.Visible, Is.True);
            Assert.That(visibleStatus.Enabled, Is.True);
        }

        [Test]
        public void GetFeatureStatus_ExperimentalFeatureStillRequiresSubscription()
        {
            var subscription = Subscription.CreateTempSubscriptionForTier(SubscriptionTier.Basic);
            ExperimentalFeatures.SetValue(kTestExperimentalToken, true);

            var status = FeatureStatus.GetFeatureStatus(subscription, MakeExperimentalProFeature());

            ExperimentalFeatures.SetValue(kTestExperimentalToken, false);

            Assert.That(status.Visible, Is.True);
            Assert.That(status.Enabled, Is.False);
            Assert.That(status.SubscriptionTier, Is.EqualTo(SubscriptionTier.Pro));
        }

        [Test]
        public void GetFeatureStatus_NonExperimentalFeatureIsAlwaysVisible()
        {
            var subscription = Subscription.CreateTempSubscriptionForTier(SubscriptionTier.Pro);

            // AppBuilder and AiImageEditing stopped being experimental in 6.5 (BL-16731):
            // they are visible to everyone and gated only by the subscription tier.
            var appBuilder = FeatureStatus.GetFeatureStatus(subscription, FeatureName.AppBuilder);
            var aiImageEditing = FeatureStatus.GetFeatureStatus(
                subscription,
                FeatureName.AiImageEditing
            );

            Assert.That(appBuilder.Visible, Is.True);
            Assert.That(appBuilder.Enabled, Is.True);
            Assert.That(aiImageEditing.Visible, Is.True);
            Assert.That(aiImageEditing.Enabled, Is.True);
        }

        [Test]
        public void ForSerialization_ReturnsValidObject()
        {
            // Arrange
            var subscription = Subscription.CreateTempSubscriptionForTier(SubscriptionTier.Pro);
            var status = FeatureStatus.GetFeatureStatus(subscription, FeatureName.Motion);

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
        [TestCase(SubscriptionTier.Basic, FeatureName.Canvas, "2")] // canvas is invalid at the basic level
        public void GetFirstFeatureThatIsInvalidForNewBooks(
            SubscriptionTier tier,
            FeatureName? featureName, // Make the enum parameter nullable
            string expectedPageNumber
        )
        {
            var subscription = Subscription.CreateTempSubscriptionForTier(tier);

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
