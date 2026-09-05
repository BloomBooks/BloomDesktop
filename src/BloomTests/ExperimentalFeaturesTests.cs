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
            // The command-line tests below leave --e2e and --experimental-features set in Program's
            // statics; parsing empty args puts every one of them back (see ProgramTests.TearDown).
            Program.ParseStartupPortArguments(System.Array.Empty<string>(), out _);
        }

        /// <summary>
        /// A feature named on the command line (--experimental-features, under --e2e) counts as
        /// enabled without being saved, and under --e2e the saved features are not reported at all,
        /// so a run does not depend on the developer's own settings.
        /// </summary>
        [Test]
        public void CommandLineFeatures_CountAsEnabledUnderE2e_AndAreNotSaved()
        {
            Program.ParseStartupPortArguments(
                new[] { "--e2e", "--experimental-features", "testing" },
                out var errorMessage
            );
            Assert.That(errorMessage, Is.Null, "Sanity check: the arguments parsed.");
            Assert.That(Program.RunningE2eTests, Is.True, "Sanity check.");

            Assert.IsTrue(ExperimentalFeatures.IsFeatureEnabled("testing"));
            Assert.AreEqual("testing", ExperimentalFeatures.TokensOfEnabledFeatures);
            Assert.AreEqual(
                "",
                Settings.Default.EnabledExperimentalFeatures,
                "The command line must not reach the saved setting."
            );

            // A saved feature is still saved, but under --e2e it is not enabled: the run sees only
            // what its command line asked for.
            ExperimentalFeatures.SetValue(ExperimentalFeatures.kTeamCollections, true);
            Assert.AreEqual("testing", ExperimentalFeatures.TokensOfEnabledFeatures);
            Assert.IsFalse(
                ExperimentalFeatures.IsFeatureEnabled(ExperimentalFeatures.kTeamCollections)
            );
            Assert.AreEqual(
                ExperimentalFeatures.kTeamCollections,
                Settings.Default.EnabledExperimentalFeatures
            );
        }

        /// <summary>
        /// Under --e2e with no --experimental-features, nothing is enabled, whatever the developer
        /// has saved: a test that needs a feature off must be able to rely on that.
        /// </summary>
        [Test]
        public void UnderE2e_WithoutCommandLineFeatures_SavedFeaturesAreIgnored()
        {
            ExperimentalFeatures.SetValue(ExperimentalFeatures.kTeamCollections, true);
            Assert.IsTrue(
                ExperimentalFeatures.IsFeatureEnabled(ExperimentalFeatures.kTeamCollections),
                "Sanity check: outside --e2e the saved feature counts."
            );

            Program.ParseStartupPortArguments(new[] { "--e2e" }, out var errorMessage);
            Assert.That(errorMessage, Is.Null, "Sanity check: the arguments parsed.");

            Assert.AreEqual("", ExperimentalFeatures.TokensOfEnabledFeatures);
            Assert.IsFalse(
                ExperimentalFeatures.IsFeatureEnabled(ExperimentalFeatures.kTeamCollections)
            );
        }

        /// <summary>
        /// Under --e2e a saved feature does not count as enabled, so SetValue must not take that
        /// as a reason to save it again: the setting is shared with the developer's own Bloom.
        /// </summary>
        [Test]
        public void SetValue_UnderE2e_DoesNotSaveAFeatureTwice()
        {
            ExperimentalFeatures.SetValue(ExperimentalFeatures.kTeamCollections, true);
            Program.ParseStartupPortArguments(new[] { "--e2e" }, out var errorMessage);
            Assert.That(errorMessage, Is.Null, "Sanity check: the arguments parsed.");
            Assert.IsFalse(
                ExperimentalFeatures.IsFeatureEnabled(ExperimentalFeatures.kTeamCollections),
                "Sanity check: under --e2e the saved feature does not count."
            );

            ExperimentalFeatures.SetValue(ExperimentalFeatures.kTeamCollections, true);

            Assert.AreEqual(
                ExperimentalFeatures.kTeamCollections,
                Settings.Default.EnabledExperimentalFeatures
            );
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
