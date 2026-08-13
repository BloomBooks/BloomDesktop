using System.IO;
using System.Linq;
using Bloom.Collection;
using Bloom.Utils;
using NUnit.Framework;
using SIL.TestUtilities;
using SIL.WritingSystems;

namespace BloomTests.Utils
{
    [TestFixture]
    class MiscUtilsTests
    {
        [Test]
        public void EscapeForCmd_DoubleQuotedString_WrappedInDoubleQuotes()
        {
            string inputCommand =
                "\"C:\\src\\Bloom Desktop 2\\output\\Debug\\Bloom.exe\" upload \"C:\\Bloom Collections\\Collection Name\" -u username@domain.com -d dev";
            var result = MiscUtils.EscapeForCmd(inputCommand);

            Assert.That(
                result,
                Is.EqualTo(
                    "\"\"C:\\src\\Bloom Desktop 2\\output\\Debug\\Bloom.exe\" upload \"C:\\Bloom Collections\\Collection Name\" -u username@domain.com -d dev\""
                )
            );
        }

        /// <summary>
        /// A collection whose name ends with a period (BL-16679) gives us a path whose folder part
        /// Windows never actually created, which breaks the FileSystemWatchers we put on the
        /// collection folder. GetPathAsOnDisk must give us back the folder that really exists,
        /// without disturbing the file name, which legitimately keeps the period.
        /// </summary>
        [Test]
        public void GetPathAsOnDisk_FolderNameEndsWithPeriod_ReturnsFolderWindowsActuallyCreated()
        {
            using (var parent = new TemporaryFolder("MiscUtilsTests_PathAsOnDisk_Period"))
            {
                var dottedFolderPath = Path.Combine(parent.Path, "Collection Name.");
                Directory.CreateDirectory(dottedFolderPath);

                // Sanity check the premise of the whole test: Windows dropped the trailing period.
                var createdFolderPath = Directory.EnumerateDirectories(parent.Path).Single();
                Assert.That(
                    Path.GetFileName(createdFolderPath),
                    Is.EqualTo("Collection Name"),
                    "Windows should have dropped the trailing period when creating the folder"
                );

                // This is the shape of the path Bloom ends up holding: a folder name with the period
                // that is not on disk, and a settings file name that really does have two periods.
                var settingsPath = Path.Combine(
                    dottedFolderPath,
                    "Collection Name..bloomCollection"
                );

                var result = MiscUtils.GetPathAsOnDisk(settingsPath);

                Assert.That(
                    Path.GetDirectoryName(result),
                    Is.EqualTo(createdFolderPath),
                    "should point at the folder that exists"
                );
                Assert.That(
                    Path.GetFileName(result),
                    Is.EqualTo("Collection Name..bloomCollection"),
                    "the settings file name's doubled period must survive"
                );
            }
        }

        [Test]
        public void GetPathAsOnDisk_OrdinaryPath_Unchanged()
        {
            using (var parent = new TemporaryFolder("MiscUtilsTests_PathAsOnDisk_Ordinary"))
            {
                var path = Path.Combine(
                    parent.Path,
                    "Collection Name",
                    "Collection Name.bloomCollection"
                );

                Assert.That(MiscUtils.GetPathAsOnDisk(path), Is.EqualTo(path));
            }
        }

        [TestCase(null)]
        [TestCase("")]
        public void GetPathAsOnDisk_NullOrEmpty_ReturnedAsIs(string path)
        {
            Assert.That(MiscUtils.GetPathAsOnDisk(path), Is.EqualTo(path));
        }

        [Test]
        public void NormalizeLanguageTagCapitalization_Works()
        {
            WritingSystem.EnsureSldrInitialized();
            // Check that valid (normalized) language tags stay the same.
            var result = MiscUtils.NormalizeLanguageTagCapitalization("en");
            Assert.That(result, Is.EqualTo("en"));
            result = MiscUtils.NormalizeLanguageTagCapitalization("en-US");
            Assert.That(result, Is.EqualTo("en-US"));
            // The IetfLanguageTag class is clever and removes the default script tag.
            // Hence, the tests use Cyrl instead of Latn for checking scripts.
            result = MiscUtils.NormalizeLanguageTagCapitalization("en-Cyrl");
            Assert.That(result, Is.EqualTo("en-Cyrl"));
            result = MiscUtils.NormalizeLanguageTagCapitalization("en-Cyrl-US");
            Assert.That(result, Is.EqualTo("en-Cyrl-US"));
            result = MiscUtils.NormalizeLanguageTagCapitalization("kwy");
            Assert.That(result, Is.EqualTo("kwy"));

            // Check that valid language tags with incorrect capitalization are corrected.
            result = MiscUtils.NormalizeLanguageTagCapitalization("en-us");
            Assert.That(result, Is.EqualTo("en-US"));
            result = MiscUtils.NormalizeLanguageTagCapitalization("en-cyrl");
            Assert.That(result, Is.EqualTo("en-Cyrl"));
            result = MiscUtils.NormalizeLanguageTagCapitalization("en-cyrl-us");
            Assert.That(result, Is.EqualTo("en-Cyrl-US"));
            result = MiscUtils.NormalizeLanguageTagCapitalization("En-cyrl-Us");
            Assert.That(result, Is.EqualTo("en-Cyrl-US"));
            result = MiscUtils.NormalizeLanguageTagCapitalization("EN-CYRL-us");
            Assert.That(result, Is.EqualTo("en-Cyrl-US"));
            // The very next test is the actual use case discovered in BL-14038.
            result = MiscUtils.NormalizeLanguageTagCapitalization("Kwy");
            Assert.That(result, Is.EqualTo("kwy"));
            result = MiscUtils.NormalizeLanguageTagCapitalization("Kwy-Cyrl");
            Assert.That(result, Is.EqualTo("kwy-Cyrl"));
            result = MiscUtils.NormalizeLanguageTagCapitalization("Kwy-cyrl");
            Assert.That(result, Is.EqualTo("kwy-Cyrl"));

            // The following test the variant subtag.
            result = MiscUtils.NormalizeLanguageTagCapitalization("Kwy-Cyrl-x-variant");
            Assert.That(result, Is.EqualTo("kwy-Cyrl-x-variant"));
            result = MiscUtils.NormalizeLanguageTagCapitalization("Kwy-Cyrl-x-Variant");
            Assert.That(result, Is.EqualTo("kwy-Cyrl-x-Variant"));
            result = MiscUtils.NormalizeLanguageTagCapitalization("Qaa-x-Language");
            Assert.That(result, Is.EqualTo("qaa-x-Language"));
            result = MiscUtils.NormalizeLanguageTagCapitalization("x-language");
            Assert.That(result, Is.EqualTo("x-language"));
            result = MiscUtils.NormalizeLanguageTagCapitalization("x-Latn");
            Assert.That(result, Is.EqualTo("x-Latn"));

            // The following don't parse, so the original string is returned.
            result = MiscUtils.NormalizeLanguageTagCapitalization("En-");
            Assert.That(result, Is.EqualTo("En-"));
            result = MiscUtils.NormalizeLanguageTagCapitalization("E");
            Assert.That(result, Is.EqualTo("E"));
            result = MiscUtils.NormalizeLanguageTagCapitalization("Four");
            Assert.That(result, Is.EqualTo("Four"));
            result = MiscUtils.NormalizeLanguageTagCapitalization("nonsense");
            Assert.That(result, Is.EqualTo("nonsense"));
            result = MiscUtils.NormalizeLanguageTagCapitalization("This is a test!");
            Assert.That(result, Is.EqualTo("This is a test!"));
        }
    }
}
