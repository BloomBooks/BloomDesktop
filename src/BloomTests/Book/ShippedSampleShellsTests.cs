using System.IO;
using System.Linq;
using Bloom;
using Bloom.Book;
using NUnit.Framework;
using SIL.IO;

namespace BloomTests.Book
{
    /// <summary>
    /// The Sample Shells we ship (src/content/templates/Sample Shells) should already be fully
    /// "updated": brought to our current maintenance levels, including the per-page browser fix-up.
    /// Otherwise every user who opens one (or makes a book from one) pays for that update, and the
    /// AI image editor and page-size changes stop to show the "Bloom needs to update the pages of
    /// this book" dialog (BL-16905).
    ///
    /// When one of the BookStorage maintenance levels is bumped, this fails until the shells are
    /// re-updated, as described in the README.md in the Sample Shells folder.
    /// </summary>
    [TestFixture]
    public class ShippedSampleShellsTests
    {
        private static string SampleShellsSourceDirectory =>
            Path.Combine(
                FileLocationUtilities.DirectoryOfApplicationOrSolution,
                "src",
                "content",
                "templates",
                "Sample Shells"
            );

        private static string[] GetShippedShellHtmPaths()
        {
            return Directory
                .GetDirectories(SampleShellsSourceDirectory)
                .Select(folder => Path.Combine(folder, Path.GetFileName(folder) + ".htm"))
                .ToArray();
        }

        [Test]
        public void ShippedSampleShells_AreAtCurrentMaintenanceLevels()
        {
            var htmPaths = GetShippedShellHtmPaths();
            // Sanity check: we are looking at the real shells, not an empty or missing folder.
            Assert.That(
                htmPaths.Select(Path.GetFileNameWithoutExtension),
                Is.SupersetOf(
                    new[] { "The Moon and the Cap", "A Family Learns about Immunisations" }
                ),
                "Did not find the expected shells in " + SampleShellsSourceDirectory
            );

            foreach (var htmPath in htmPaths)
            {
                Assert.That(RobustFile.Exists(htmPath), Is.True, "Missing book file " + htmPath);
                var dom = new HtmlDom(XmlHtmlConverter.GetXmlDomFromHtmlFile(htmPath));
                var book = Path.GetFileNameWithoutExtension(htmPath);
                const string howToFix =
                    " Re-update the shipped Sample Shells as described in src/content/templates/Sample Shells/README.md.";

                Assert.That(
                    dom.GetMetaValue("maintenanceLevel", "missing"),
                    Is.EqualTo(BookStorage.kMaintenanceLevel.ToString()),
                    book + ": maintenanceLevel is behind." + howToFix
                );
                Assert.That(
                    dom.GetMetaValue("mediaMaintenanceLevel", "missing"),
                    Is.EqualTo(BookStorage.kMediaMaintenanceLevel.ToString()),
                    book + ": mediaMaintenanceLevel is behind." + howToFix
                );
                Assert.That(
                    dom.GetMetaValue(BookProcessor.kPageLayoutUpdateLevelMeta, "missing"),
                    Is.EqualTo(BookStorage.kPageLayoutUpdateLevel.ToString()),
                    book + ": pageLayoutUpdateLevel is behind." + howToFix
                );
            }
        }
    }
}
