using System;
using System.IO;
using Bloom.CLI;
using BloomTemp;
using NUnit.Framework;
using SIL.IO;

namespace BloomTests.CLI
{
    /// <summary>
    /// Tests for the argument checks SendFontAnalyticsCommand makes before doing any real work.
    /// </summary>
    [TestFixture]
    public class SendFontAnalyticsCommandTests
    {
        private TemporaryFolder _folder;
        private string _bookFolder;
        private string _bookFile;
        private string _collectionFile;

        /// <summary>
        /// Make a book folder with its .htm file, and a collection file.
        /// </summary>
        [SetUp]
        public void SetUp()
        {
            _folder = new TemporaryFolder("SendFontAnalyticsCommandTests");
            _bookFolder = Path.Combine(_folder.FolderPath, "My Book");
            Directory.CreateDirectory(_bookFolder);
            _bookFile = Path.Combine(_bookFolder, "My Book.htm");
            RobustFile.WriteAllText(_bookFile, "<html><body></body></html>");
            _collectionFile = Path.Combine(_folder.FolderPath, "Test.bloomCollection");
            RobustFile.WriteAllText(_collectionFile, "<Collection/>");
        }

        /// <summary>
        /// Remove the test's files.
        /// </summary>
        [TearDown]
        public void TearDown()
        {
            _folder?.Dispose();
        }

        /// <summary>
        /// The book's folder is what the command wants; that should pass unchanged.
        /// </summary>
        [Test]
        public void CheckPaths_BookFolder_Accepted()
        {
            var options = MakeOptions(_bookFolder);
            Assert.That(SendFontAnalyticsCommand.CheckPaths(options), Is.True);
            Assert.That(options.BookPath, Is.EqualTo(_bookFolder));
        }

        /// <summary>
        /// Giving the book's .htm file, as other verbs take, should be understood as its folder.
        /// </summary>
        [Test]
        public void CheckPaths_BookHtmFile_UsesItsFolder()
        {
            var options = MakeOptions(_bookFile);
            Assert.That(SendFontAnalyticsCommand.CheckPaths(options), Is.True);
            Assert.That(options.BookPath, Is.EqualTo(_bookFolder));
        }

        /// <summary>
        /// A book path that does not exist is reported plainly rather than as a stack trace.
        /// </summary>
        [Test]
        public void CheckPaths_MissingBook_ReportsIt()
        {
            var missing = Path.Combine(_folder.FolderPath, "bookPath");
            var (ok, output) = Check(MakeOptions(missing));
            Assert.That(ok, Is.False);
            Assert.That(output, Does.Contain($"The book folder '{missing}' does not exist."));
        }

        /// <summary>
        /// A collection file that does not exist, in a folder holding no other, is reported
        /// plainly too.
        /// </summary>
        [Test]
        public void CheckPaths_MissingCollection_ReportsIt()
        {
            var emptyFolder = Path.Combine(_folder.FolderPath, "Empty");
            Directory.CreateDirectory(emptyFolder);
            var options = MakeOptions(_bookFolder);
            options.CollectionPath = Path.Combine(emptyFolder, "Nope.bloomCollection");
            var (ok, output) = Check(options);
            Assert.That(ok, Is.False);
            Assert.That(
                output,
                Does.Contain($"The collection file '{options.CollectionPath}' does not exist.")
            );
        }

        /// <summary>
        /// A collection path worked out from a renamed folder names a file that isn't there, but
        /// the folder still holds the collection's real settings file; that should be used, as
        /// ProjectContext would use it.
        /// </summary>
        [Test]
        public void CheckPaths_CollectionFileNamedDifferently_UsesTheOneInTheFolder()
        {
            var options = MakeOptions(_bookFolder);
            options.CollectionPath = Path.Combine(_folder.FolderPath, "Renamed.bloomCollection");
            Assert.That(
                RobustFile.Exists(options.CollectionPath),
                Is.False,
                "test setup: the path given should not exist"
            );
            var (ok, output) = Check(options);
            Assert.That(ok, Is.True, output);
            Assert.That(options.CollectionPath, Is.EqualTo(_collectionFile));
        }

        private SendFontAnalyticsParameters MakeOptions(string bookPath)
        {
            return new SendFontAnalyticsParameters
            {
                BookPath = bookPath,
                CollectionPath = _collectionFile,
            };
        }

        private static (bool, string) Check(SendFontAnalyticsParameters options)
        {
            var output = new StringWriter();
            var originalOut = Console.Out;
            Console.SetOut(output);
            try
            {
                return (SendFontAnalyticsCommand.CheckPaths(options), output.ToString());
            }
            finally
            {
                Console.SetOut(originalOut);
            }
        }
    }
}
