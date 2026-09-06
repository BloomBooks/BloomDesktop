using System.IO;
using BloomTemp;
using NUnit.Framework;

namespace BloomTests.Spreadsheet
{
    /// <summary>
    /// Makes the temporary folders the spreadsheet tests work in, arranged so that no test book
    /// folder ever sits directly in %TEMP%.
    ///
    /// That matters because SpreadsheetImporter.GetPageForLabel() looks for page templates in
    /// "the collection the book is in", which it computes as the parent of the book folder. If the
    /// book folder is directly in %TEMP%, the whole temp directory looks like a Bloom collection,
    /// so Bloom inspects every subfolder of it as a candidate book. A working machine can easily
    /// have thousands of those; they belong to other tests and to other programs, and any of them
    /// may be deleted while we are part way through the scan. That used to kill a whole fixture in
    /// OneTimeSetUp, naming some unrelated folder on the way out. See BL-16661.
    ///
    /// So each fixture takes a folder of its own from here and nests everything it needs inside
    /// that, giving %TEMP%/BloomSpreadsheetTests/&lt;fixture&gt;/&lt;book&gt;. The parent of a book
    /// folder is then a small folder that only that one fixture ever writes to. A side benefit is
    /// that these tests now leave one entry in %TEMP% rather than three dozen.
    /// </summary>
    internal static class SpreadsheetTestFolders
    {
        private const string kRootFolderName = "BloomSpreadsheetTests";

        /// <summary>
        /// A folder of this fixture's own, named after it, to nest the fixture's other temp
        /// folders in. The caller owns it and should Dispose it (normally in OneTimeTearDown),
        /// which also removes everything nested inside it.
        /// </summary>
        public static TemporaryFolder MakeFolderFor(object fixture)
        {
            return MakeFolderNamed(fixture.GetType().Name);
        }

        /// <summary>
        /// As MakeFolderFor(), for the few callers that are static and so have no fixture
        /// instance to name themselves after. The name must be distinct from every fixture name,
        /// since making one of these deletes any existing folder of the same name.
        /// </summary>
        public static TemporaryFolder MakeFolderNamed(string name)
        {
            // Note that we deliberately do NOT make the root with the TemporaryFolder(name)
            // constructor: that first deletes any existing folder of the name, which would throw
            // away the folders of every other fixture using the root.
            var rootPath = Path.Combine(Path.GetTempPath(), kRootFolderName);
            Directory.CreateDirectory(rootPath);
            return new TemporaryFolder(TemporaryFolder.TrackExisting(rootPath), name);
        }
    }

    public class SpreadsheetTestFoldersTests
    {
        /// <summary>
        /// The whole point of SpreadsheetTestFolders: a book folder nested in one of its folders
        /// has, as its parent, a folder that only this fixture puts anything in. That parent is
        /// what SpreadsheetImporter treats as the book's collection and scans for page templates,
        /// so if it were %TEMP% (as it used to be) the importer would walk every temp folder on
        /// the machine. See BL-16661.
        /// </summary>
        [Test]
        public void MakeFolderFor_BookNestedInside_HasParentContainingOnlyOurFolders()
        {
            using (var testFolder = SpreadsheetTestFolders.MakeFolderFor(this))
            {
                // Sanity check: we really did get a new, empty folder to work in.
                Assert.That(Directory.Exists(testFolder.FolderPath), Is.True);
                Assert.That(Directory.GetFileSystemEntries(testFolder.FolderPath), Is.Empty);

                var bookFolder = new TemporaryFolder(testFolder, "Book");

                var collectionPath = Path.GetDirectoryName(bookFolder.FolderPath);
                Assert.That(
                    collectionPath,
                    Is.EqualTo(testFolder.FolderPath),
                    "The folder the importer will scan as the book's collection should be this fixture's own folder."
                );
                Assert.That(
                    Path.GetFullPath(collectionPath),
                    Is.Not.EqualTo(Path.GetFullPath(Path.GetTempPath())),
                    "A test book must never sit directly in %TEMP%."
                );
                Assert.That(
                    Directory.GetFileSystemEntries(collectionPath),
                    Is.EqualTo(new[] { bookFolder.FolderPath }),
                    "Only folders this fixture made should be in the folder the importer scans."
                );
            }
        }
    }
}
