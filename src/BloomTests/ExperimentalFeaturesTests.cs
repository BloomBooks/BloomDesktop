using Bloom;
using Bloom.Properties;
using NUnit.Framework;

namespace BloomTests
{
    [TestFixture]
    public class ExperimentalFeaturesTests
    {
        private string _originalEnabledFeatures;

        /// <summary>
        /// SetValue() saves the settings, so this fixture writes state that outlives the test.
        /// Establish the starting value rather than assuming it: relying on it being empty meant
        /// that a run which died part way through (see "*** TEST RUN ABORTED ***" in AGENTS.md)
        /// left a value behind that failed this fixture on every later run.
        /// </summary>
        [SetUp]
        public void SetUp()
        {
            _originalEnabledFeatures = Settings.Default.EnabledExperimentalFeatures;
            Settings.Default.EnabledExperimentalFeatures = "";
        }

        /// <summary>
        /// Put back whatever we found, on disk as well as in memory, so we don't affect anything else.
        /// </summary>
        [TearDown]
        public void TearDown()
        {
            Settings.Default.EnabledExperimentalFeatures = _originalEnabledFeatures;
            Settings.Default.Save();
        }

        /// <summary>
        /// Test the SetValue and IsFeatureEnabled methods as well as the
        /// TokensOfEnabledFeatures property.
        /// </summary>
        [Test]
        public void SetValueWorksProperly()
        {
            // Sanity check that SetUp has put us in the state the rest of the test assumes.
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
    }
}
