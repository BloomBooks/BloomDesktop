using Bloom;
using Bloom.SubscriptionAndFeatures;
using NUnit.Framework;

namespace BloomTests.FeatureStatusTests
{
    /// <summary>
    /// Flow text is gated two ways at once: it needs a subscription of any paid tier, and it
    /// needs the flow-text experimental feature. These tests hold both halves in place.
    /// </summary>
    [TestFixture]
    public class FlowTextFeatureTests
    {
        private bool _experimentalFeatureWasOn;

        /// <summary>
        /// The experimental feature is a saved setting shared with the developer's own Bloom,
        /// so what it was is put back whatever these tests do to it.
        /// </summary>
        [SetUp]
        public void Setup()
        {
            _experimentalFeatureWasOn = ExperimentalFeatures.IsFeatureEnabled(
                ExperimentalFeatures.kFlowText
            );
        }

        [TearDown]
        public void TearDown()
        {
            ExperimentalFeatures.SetValue(
                ExperimentalFeatures.kFlowText,
                _experimentalFeatureWasOn
            );
        }

        [Test]
        public void FlowText_NeedsAtLeastPro()
        {
            var feature = FeatureRegistry.Features.Find(f => f.Feature == FeatureName.FlowText);

            Assert.That(feature, Is.Not.Null, "flow text is not in the feature registry");
            Assert.That(
                feature.SubscriptionTier,
                Is.EqualTo(SubscriptionTier.Pro),
                "flow text asks for the lowest paid tier, so that any subscription unlocks it"
            );
            Assert.That(
                feature.ExperimentalFeatureToken,
                Is.EqualTo(ExperimentalFeatures.kFlowText)
            );
        }

        [TestCase(SubscriptionTier.Basic, false)]
        [TestCase(SubscriptionTier.Pro, true)]
        [TestCase(SubscriptionTier.LocalCommunity, true)]
        [TestCase(SubscriptionTier.Enterprise, true)]
        public void FlowText_EnabledForEverySubscribedTier(
            SubscriptionTier tier,
            bool expectedEnabled
        )
        {
            ExperimentalFeatures.SetValue(ExperimentalFeatures.kFlowText, true);
            var subscription = Subscription.CreateTempSubscriptionForTier(tier);

            var status = FeatureStatus.GetFeatureStatus(subscription, FeatureName.FlowText);

            Assert.That(status.Enabled, Is.EqualTo(expectedEnabled));
            Assert.That(status.Visible, Is.True, "the experimental feature is on");
        }

        [Test]
        public void FlowText_HiddenWhileTheExperimentalFeatureIsOff()
        {
            var subscription = Subscription.CreateTempSubscriptionForTier(SubscriptionTier.Pro);

            ExperimentalFeatures.SetValue(ExperimentalFeatures.kFlowText, true);
            var whenOn = FeatureStatus.GetFeatureStatus(subscription, FeatureName.FlowText);

            ExperimentalFeatures.SetValue(ExperimentalFeatures.kFlowText, false);
            var whenOff = FeatureStatus.GetFeatureStatus(subscription, FeatureName.FlowText);

            // Sanity check: the subscription is the same in both, so only the experimental
            // feature can be what changed.
            Assert.That(whenOn.Visible, Is.True);
            Assert.That(whenOn.Enabled, Is.True);
            Assert.That(whenOff.Visible, Is.False);
            Assert.That(whenOff.Enabled, Is.True);
        }
    }
}
