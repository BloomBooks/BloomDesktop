using Bloom;
using NUnit.Framework;

namespace BloomTests
{
    [TestFixture]
    public class ExperimentalFeaturesTests
    {
        /// <summary>
        /// Test the SetValue and IsFeatureEnabled methods as well as the
        /// TokensOfEnabledFeatures property.
        /// </summary>
        [Test]
        public void SetValueWorksProperly()
        {
            // In the test setup, this setting always starts out empty (the default value).
            Assert.AreEqual("", ExperimentalFeatures.TokensOfEnabledFeatures);
            Assert.IsFalse(ExperimentalFeatures.IsFeatureEnabled("testing"));

            ExperimentalFeatures.SetValue("testing", true);
            Assert.IsTrue(ExperimentalFeatures.IsFeatureEnabled("testing"));
            Assert.AreEqual("testing", ExperimentalFeatures.TokensOfEnabledFeatures);

            // setting more than once should not change the stored value
            ExperimentalFeatures.SetValue("testing", true);
            Assert.IsTrue(ExperimentalFeatures.IsFeatureEnabled("testing"));
            Assert.AreEqual("testing", ExperimentalFeatures.TokensOfEnabledFeatures);

            ExperimentalFeatures.SetValue("testing", false);
            Assert.IsFalse(ExperimentalFeatures.IsFeatureEnabled("testing"));
            Assert.AreEqual("", ExperimentalFeatures.TokensOfEnabledFeatures);

            ExperimentalFeatures.SetValue(ExperimentalFeatures.kExperimentalSourceBooks, true);
            ExperimentalFeatures.SetValue(ExperimentalFeatures.kTeamCollections, true);
            ExperimentalFeatures.SetValue(ExperimentalFeatures.kExperimentalSourceBooks, true);
            ExperimentalFeatures.SetValue(ExperimentalFeatures.kTeamCollections, true);
            Assert.IsTrue(
                ExperimentalFeatures.IsFeatureEnabled(ExperimentalFeatures.kExperimentalSourceBooks)
            );
            Assert.IsTrue(
                ExperimentalFeatures.IsFeatureEnabled(ExperimentalFeatures.kTeamCollections)
            );
            Assert.AreEqual(
                ExperimentalFeatures.kExperimentalSourceBooks
                    + ","
                    + ExperimentalFeatures.kTeamCollections,
                ExperimentalFeatures.TokensOfEnabledFeatures
            );

            ExperimentalFeatures.SetValue(ExperimentalFeatures.kExperimentalSourceBooks, false);
            Assert.IsFalse(
                ExperimentalFeatures.IsFeatureEnabled(ExperimentalFeatures.kExperimentalSourceBooks)
            );
            Assert.IsTrue(
                ExperimentalFeatures.IsFeatureEnabled(ExperimentalFeatures.kTeamCollections)
            );
            Assert.AreEqual(
                ExperimentalFeatures.kTeamCollections,
                ExperimentalFeatures.TokensOfEnabledFeatures
            );

            ExperimentalFeatures.SetValue(ExperimentalFeatures.kTeamCollections, false);
            Assert.IsFalse(
                ExperimentalFeatures.IsFeatureEnabled(ExperimentalFeatures.kExperimentalSourceBooks)
            );
            Assert.IsFalse(
                ExperimentalFeatures.IsFeatureEnabled(ExperimentalFeatures.kTeamCollections)
            );
            Assert.AreEqual("", ExperimentalFeatures.TokensOfEnabledFeatures);
        }

        /// <summary>
        /// Regression (Devin review, 14 Jul 2026): the tokens "team-collections" and
        /// "cloud-team-collections" share a substring, so a substring-based check/replace made
        /// enabling the cloud feature read as the folder feature being enabled, and disabling the
        /// folder feature corrupted the cloud token ("cloud-team-collections" -> "cloud-"). The
        /// check/removal must be by EXACT token.
        /// </summary>
        [Test]
        public void CloudAndFolderTeamCollectionTokensDoNotCollide()
        {
            // Start clean regardless of test order.
            ExperimentalFeatures.SetValue(ExperimentalFeatures.kTeamCollections, false);
            ExperimentalFeatures.SetValue(ExperimentalFeatures.kCloudTeamCollections, false);
            Assert.IsFalse(
                ExperimentalFeatures.IsFeatureEnabled(ExperimentalFeatures.kTeamCollections),
                "sanity: folder TC should start disabled"
            );

            // Enabling ONLY cloud must not make the folder feature read as enabled.
            ExperimentalFeatures.SetValue(ExperimentalFeatures.kCloudTeamCollections, true);
            Assert.IsTrue(
                ExperimentalFeatures.IsFeatureEnabled(ExperimentalFeatures.kCloudTeamCollections)
            );
            Assert.IsFalse(
                ExperimentalFeatures.IsFeatureEnabled(ExperimentalFeatures.kTeamCollections),
                "enabling cloud TC must NOT silently enable folder TC"
            );

            // Disabling the folder feature must leave the cloud token intact.
            ExperimentalFeatures.SetValue(ExperimentalFeatures.kTeamCollections, false);
            Assert.IsTrue(
                ExperimentalFeatures.IsFeatureEnabled(ExperimentalFeatures.kCloudTeamCollections),
                "disabling folder TC must NOT corrupt/disable the cloud token"
            );
            Assert.AreEqual(
                ExperimentalFeatures.kCloudTeamCollections,
                ExperimentalFeatures.TokensOfEnabledFeatures
            );

            // With both enabled, disabling folder removes exactly it and leaves cloud enabled.
            ExperimentalFeatures.SetValue(ExperimentalFeatures.kTeamCollections, true);
            Assert.IsTrue(
                ExperimentalFeatures.IsFeatureEnabled(ExperimentalFeatures.kTeamCollections)
            );
            ExperimentalFeatures.SetValue(ExperimentalFeatures.kTeamCollections, false);
            Assert.IsFalse(
                ExperimentalFeatures.IsFeatureEnabled(ExperimentalFeatures.kTeamCollections)
            );
            Assert.IsTrue(
                ExperimentalFeatures.IsFeatureEnabled(ExperimentalFeatures.kCloudTeamCollections)
            );

            // cleanup
            ExperimentalFeatures.SetValue(ExperimentalFeatures.kCloudTeamCollections, false);
            Assert.AreEqual("", ExperimentalFeatures.TokensOfEnabledFeatures);
        }
    }
}
