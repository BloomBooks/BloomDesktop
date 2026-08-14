using System;
using System.IO;
using Bloom.Collection;
using NUnit.Framework;
using SIL.IO;
using SIL.TestUtilities;

namespace BloomTests.Collection
{
    /// <summary>
    /// Tests the gate that keeps an older Bloom from opening a collection that declares
    /// a MinimumBloomVersion. See BL-16690.
    /// </summary>
    [TestFixture]
    public class MinimumBloomVersionCheckTests
    {
        private TemporaryFolder _folder;

        [OneTimeSetUp]
        public void FixtureSetup()
        {
            _folder = new TemporaryFolder("MinimumBloomVersionCheckTests");
        }

        [OneTimeTearDown]
        public void Cleanup()
        {
            _folder.Dispose();
        }

        // No requirement at all: everything is allowed in.
        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        // A requirement we can't make sense of is ignored rather than locking the user out.
        [TestCase("banana")]
        [TestCase("6")] // Version.TryParse insists on at least major.minor
        [TestCase("6.5-beta")]
        public void IsVersionSufficient_NoUsableRequirement_AllowsAnyVersion(string minimumVersion)
        {
            Assert.That(
                MinimumBloomVersionCheck.IsVersionSufficient(minimumVersion, new Version(1, 0, 0)),
                Is.True,
                "Even an ancient Bloom should be allowed in when there is no usable requirement."
            );
        }

        [TestCase("6.5", "6.5.0.0", true, Description = "exactly the required version")]
        [TestCase("6.5", "6.5.132.0", true, Description = "same minor, later build")]
        [TestCase("6.5", "6.6.0.0", true, Description = "later minor")]
        [TestCase("6.5", "7.0.0.0", true, Description = "later major")]
        [TestCase("6.5", "6.4.900.0", false, Description = "earlier minor, even with a high build")]
        [TestCase("6.5", "5.9.0.0", false, Description = "earlier major")]
        [TestCase("7.0", "6.9.0.0", false, Description = "earlier major, higher minor")]
        // We compare major.minor only, matching Bloom's other version gates, so a build number
        // in the requirement is deliberately ignored.
        [TestCase("6.5.132", "6.5.10.0", true, Description = "build number in requirement ignored")]
        public void IsVersionSufficient_ComparesMajorAndMinor(
            string minimumVersion,
            string runningVersion,
            bool expected
        )
        {
            Assert.That(
                MinimumBloomVersionCheck.IsVersionSufficient(
                    minimumVersion,
                    Version.Parse(runningVersion)
                ),
                Is.EqualTo(expected)
            );
        }

        /// <summary>
        /// We compare major.minor only, so that is what the user should be shown. Reporting a build
        /// number we don't actually enforce would misrepresent the rule. Version 99 is used so these
        /// stay true however far Bloom's real version advances.
        /// </summary>
        // Each case needs its own collection name: two of these differ only by whitespace, so
        // deriving the name from the value would have them share a folder and overwrite each other.
        [TestCase("99.0", "99.0", "ReportedPlain")]
        [TestCase(
            "99.0.132",
            "99.0",
            "ReportedWithBuild",
            Description = "build number dropped, since we ignore it"
        )]
        [TestCase(
            "  99.0  ",
            "99.0",
            "ReportedPadded",
            Description = "surrounding whitespace tolerated"
        )]
        public void IsThisBloomTooOld_TooOld_ReportsRequirementAsMajorMinor(
            string declaredVersion,
            string expectedReported,
            string collectionName
        )
        {
            var path = WriteSettingsFile(
                collectionName,
                $"<MinimumBloomVersion>{declaredVersion}</MinimumBloomVersion>"
            );

            Assert.That(
                MinimumBloomVersionCheck.IsThisBloomTooOld(path, out var minimumVersion),
                Is.True,
                "No conceivable Bloom version satisfies a requirement of 99.x."
            );
            Assert.That(minimumVersion, Is.EqualTo(expectedReported));
        }

        [Test]
        public void IsThisBloomTooOld_RequirementSatisfied_ReturnsFalse()
        {
            var path = WriteSettingsFile(
                "AncientRequirement",
                "<MinimumBloomVersion>0.1</MinimumBloomVersion>"
            );

            Assert.That(
                MinimumBloomVersionCheck.IsThisBloomTooOld(path, out var minimumVersion),
                Is.False,
                "Every Bloom that has ever shipped is newer than 0.1."
            );
            Assert.That(minimumVersion, Is.Empty);
        }

        /// <summary>
        /// A Team Collection has to judge the settings sitting in the repository, which it reads out
        /// of a zip and never writes to disk, so the same check has to work on content in hand.
        /// </summary>
        [Test]
        public void IsThisBloomTooOldForSettings_RequirementNoBloomCanMeet_SaysSoAndReportsIt()
        {
            var xml = SettingsXml("<MinimumBloomVersion>99.0</MinimumBloomVersion>");

            Assert.That(
                MinimumBloomVersionCheck.IsThisBloomTooOldForSettings(xml, out var minimumVersion),
                Is.True,
                "No conceivable Bloom version satisfies a requirement of 99.x."
            );
            Assert.That(minimumVersion, Is.EqualTo("99.0"));
        }

        [Test]
        public void IsThisBloomTooOldForSettings_NoRequirement_LetsUsIn()
        {
            Assert.That(
                MinimumBloomVersionCheck.IsThisBloomTooOldForSettings(SettingsXml(""), out _),
                Is.False
            );
        }

        /// <summary>
        /// Nothing to read means we know nothing, which must not be mistaken for "you are locked
        /// out" -- that would shut a Team Collection user out of their work over a read failure.
        /// </summary>
        [TestCase(null)]
        [TestCase("")]
        public void IsThisBloomTooOldForSettings_NothingToRead_LetsUsIn(string xml)
        {
            Assert.That(
                MinimumBloomVersionCheck.IsThisBloomTooOldForSettings(xml, out _),
                Is.False
            );
        }

        [Test]
        public void IsThisBloomTooOldForSettings_UnparseableXml_LetsUsInRatherThanThrowing()
        {
            Assert.That(
                MinimumBloomVersionCheck.IsThisBloomTooOldForSettings("not xml at all", out _),
                Is.False
            );
        }

        private static string SettingsXml(string extraElements) =>
            $@"<?xml version='1.0' encoding='utf-8'?>
<Collection version='0.2'>
  <Language1Iso639Code>xyz</Language1Iso639Code>
  {extraElements}
</Collection>";

        [Test]
        public void ReadMinimumBloomVersion_ElementPresent_ReturnsIt()
        {
            var path = WriteSettingsFile(
                "MinimumVersionPresent",
                "<MinimumBloomVersion>6.5</MinimumBloomVersion>"
            );

            Assert.That(MinimumBloomVersionCheck.ReadMinimumBloomVersion(path), Is.EqualTo("6.5"));
        }

        [Test]
        public void ReadMinimumBloomVersion_ElementAbsent_ReturnsEmpty()
        {
            var path = WriteSettingsFile("MinimumVersionAbsent", "");

            Assert.That(MinimumBloomVersionCheck.ReadMinimumBloomVersion(path), Is.Empty);
        }

        /// <summary>
        /// A settings file we can't parse is a real problem, but it is not this check's problem to
        /// report; the normal open will fail and give the user a much better error report.
        /// </summary>
        [Test]
        public void ReadMinimumBloomVersion_UnparseableFile_ReturnsEmptyRatherThanThrowing()
        {
            var path = Path.Combine(_folder.Path, "Garbage.bloomCollection");
            RobustFile.WriteAllText(path, "this is not xml at all");

            Assert.That(MinimumBloomVersionCheck.ReadMinimumBloomVersion(path), Is.Empty);
        }

        [Test]
        public void ReadMinimumBloomVersion_NoSuchFile_ReturnsEmpty()
        {
            var path = Path.Combine(_folder.Path, "NotThere.bloomCollection");
            Assert.That(
                RobustFile.Exists(path),
                Is.False,
                "Test setup problem: this file was supposed to not exist."
            );

            Assert.That(MinimumBloomVersionCheck.ReadMinimumBloomVersion(path), Is.Empty);
        }

        /// <summary>
        /// Save() rebuilds the settings file from scratch, so a hand-added MinimumBloomVersion would be
        /// silently lost the first time the user changed anything in Collection Settings, unless we
        /// write it back out. That would be a nasty way to lose the protection.
        /// </summary>
        [Test]
        public void CollectionSettings_MinimumBloomVersion_SurvivesLoadAndSave()
        {
            var path = WriteSettingsFile(
                "RoundTrip",
                "<MinimumBloomVersion>6.5</MinimumBloomVersion>"
            );

            var settings = new CollectionSettings(path);
            Assert.That(
                settings.MinimumBloomVersion,
                Is.EqualTo("6.5"),
                "Should have read the minimum version from the file we just wrote."
            );

            settings.Save();

            Assert.That(
                MinimumBloomVersionCheck.ReadMinimumBloomVersion(path),
                Is.EqualTo("6.5"),
                "Save() dropped the minimum version, which would leave the collection unprotected."
            );
        }

        /// <summary>
        /// We don't want to add a meaningless empty element to every collection settings file in the world.
        /// </summary>
        [Test]
        public void CollectionSettings_NoMinimumBloomVersion_NotWrittenOnSave()
        {
            var path = WriteSettingsFile("NoMinimum", "");

            var settings = new CollectionSettings(path);
            Assert.That(
                settings.MinimumBloomVersion,
                Is.Empty,
                "Test setup problem: there should be no minimum version yet."
            );

            settings.Save();

            Assert.That(
                RobustFile.ReadAllText(path),
                Does.Not.Contain(CollectionSettings.kMinimumBloomVersionElementName)
            );
        }

        /// <summary>
        /// Writes a minimal .bloomCollection file in its own folder, since CollectionSettings
        /// expects the folder to be named after the collection.
        /// </summary>
        private string WriteSettingsFile(string collectionName, string extraElements)
        {
            var collectionFolder = Path.Combine(_folder.Path, collectionName);
            Directory.CreateDirectory(collectionFolder);
            var path = Path.Combine(collectionFolder, collectionName + ".bloomCollection");
            RobustFile.WriteAllText(
                path,
                $@"<?xml version='1.0' encoding='utf-8'?>
<Collection version='0.2'>
  <Language1Iso639Code>xyz</Language1Iso639Code>
  {extraElements}
</Collection>"
            );
            return path;
        }
    }
}
