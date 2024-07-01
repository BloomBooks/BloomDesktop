using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Bloom;
using Bloom.Book;
using Bloom.Collection;
using Bloom.Edit;
using BloomTemp;
using SIL.IO;

namespace BloomTests.Book
{
    /// <summary>
    /// This class supports book tests that need real books. A temporary folder is created and hangs around as long as this
    /// TestBook class exists. This should be considered a work-in-progress, I just built as much as I needed for
    /// one test. It might be useful to enhance it with more default state for the book (e.g., images) or static
    /// create methods with various arguments. Could even be worth implementing a fluent language
    /// so you can do something like TestBook.CreateBook().WithStyles(...).WithImage(name, content).WithContent(...).Book.
    /// </summary>
    public class TestBook : IDisposable
    {
        private TemporaryFolder _folder;

        public Bloom.Book.Book Book;

        public string BookFolder;

        public string BookPath;

        public TestBook(string testName, string content)
        {
            _folder = new TemporaryFolder(testName);
            BookFolder = _folder.FolderPath;
            BookPath = Path.Combine(_folder.FolderPath, testName + ".htm");
            File.WriteAllText(BookPath, content);
            var settings = CreateDefaultCollectionsSettings();
            var codeBaseDir = BloomFileLocator.GetCodeBaseFolder();
            // This is minimal...if the content doesn't specify xmatter Bloom defaults to Traditional
            // and needs the file locator to know this folder so it can find it.
            // May later need to include more folders or allow the individual tests to do so.
            var locator = new FileLocator(
                new string[] { codeBaseDir + "/../browser/templates/xMatter" }
            );
            var storage = new BookStorage(BookFolder, locator, new BookRenamedEvent(), settings);
            // very minimal...enhance if we need to test something that can really find source collections.
            var templatefinder = new SourceCollectionsList();
            Book = new Bloom.Book.Book(
                new BookInfo(BookFolder, true),
                storage,
                templatefinder,
                settings,
                new PageListChangedEvent(),
                new BookRefreshEvent()
            );
        }

        protected CollectionSettings CreateDefaultCollectionsSettings()
        {
            return new CollectionSettings(
                new NewCollectionSettings()
                {
                    PathToSettingsFile = CollectionSettings.GetPathForNewSettings(
                        BookFolder,
                        "test"
                    ),
                    Language1Tag = "xyz",
                    Language2Tag = "en",
                    Language3Tag = "fr"
                }
            );
        }

        // Since this is just for testing I didn't bother with the usual mess to catch/cleanup
        // if someone forgets to dispose.
        public void Dispose()
        {
            _folder.Dispose();
        }
    }
}
