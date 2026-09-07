using System.Collections.Generic;
using System.IO;
using System.Linq;
using Bloom;
using Bloom.Book;
using Bloom.Collection;
using Bloom.Edit;
using NUnit.Framework;
using SIL.IO;
using SIL.TestUtilities;

namespace BloomTests.Book
{
    [TestFixture]
    public class BookCollectionTests
    {
        private BookCollection _collection;
        private TemporaryFolder _folder;
        private IChangeableFileLocator _fileLocator;

        [SetUp]
        public void Setup()
        {
            SIL.Reporting.ErrorReport.IsOkToInteractWithUser = false;
            _folder = new TemporaryFolder("BookCollectionTests");
            _fileLocator = new BloomFileLocator(
                new CollectionSettings(),
                new XMatterPackFinder(new string[] { }),
                ProjectContext.GetFactoryFileLocations(),
                ProjectContext.GetFoundFileLocations(),
                ProjectContext.GetAfterXMatterFileLocations()
            );
            _collection = new BookCollection(
                _folder.Path,
                BookCollection.CollectionType.TheOneEditableCollection,
                new BookSelection()
            );
        }

        Bloom.Book.Book BookFactory(BookInfo bookInfo, IBookStorage storage, bool editable)
        {
            return new Bloom.Book.Book(
                bookInfo,
                storage,
                null,
                new CollectionSettings(
                    new NewCollectionSettings()
                    {
                        PathToSettingsFile = CollectionSettings.GetPathForNewSettings(
                            _folder.Path,
                            "test"
                        ),
                        Language1Tag = "xyz",
                    }
                ),
                new PageListChangedEvent(),
                new BookRefreshEvent()
            );
        }

        BookStorage BookStorageFactory(string folderPath)
        {
            return new BookStorage(
                folderPath,
                _fileLocator,
                new BookRenamedEvent(),
                new CollectionSettings()
            );
        }

        private void AddBook()
        {
            AddBook(_folder, "alpha");
        }

        internal static void AddBook(TemporaryFolder collectionFolder, string bookTitle)
        {
            string path = collectionFolder.Combine(bookTitle);
            Directory.CreateDirectory(path);
            File.WriteAllText(Path.Combine(path, $"{bookTitle}.htm"), @"<html></html>");
        }

        [Test]
        public void DeleteBook_FirstBookInEditableCollection_RemovedFromCollection()
        {
            AddBook();
            var book = _collection.GetBookInfos().First();
            var bookFolder = book.FolderPath;
            _collection.DeleteBook(book);

            Assert.IsFalse(_collection.GetBookInfos().Contains(book));
            Assert.IsFalse(Directory.Exists(bookFolder));
        }

        [Test]
        public void DeleteBook_FirstBookInEditableCollection_RaisesCollectionChangedEvent()
        {
            AddBook();
            bool triggered = false;
            _collection.CollectionChanged += (x, y) => triggered = true;
            _collection.DeleteBook(_collection.GetBookInfos().First());
            Assert.IsTrue(triggered);
        }

        [Test]
        public void InsertBook_NotPresent_InsertsInCorrectOrder()
        {
            var info1 = new BookInfo("book1", true);
            var info2 = new BookInfo("book2", true);
            var info3 = new BookInfo("book10", true);
            var info4 = new BookInfo("book20", true);
            var infoNew = new BookInfo("book11", true);
            var state = new List<BookInfo>(new[] { info1, info2, info3, info4 });
            var collection = new BookCollection(state);
            collection.InsertBookInfo(infoNew);
            Assert.That(
                state[3],
                Is.EqualTo(infoNew),
                "book info should be inserted between book10 and book20"
            );

            var infoLast = new BookInfo("book30", true);
            collection.InsertBookInfo(infoLast);
            Assert.That(state[5], Is.EqualTo(infoLast), "book info should be inserted at end");

            var infoFirst = new BookInfo("abc", true);
            collection.InsertBookInfo(infoFirst);
            Assert.That(state[0], Is.EqualTo(infoFirst), "book info should be inserted at start");
        }

        [Test]
        public void InsertBook_NotPresent_InsertsInEmptyList()
        {
            var infoNew = new BookInfo("book11", true);
            var state = new List<BookInfo>();
            var collection = new BookCollection(state);
            collection.InsertBookInfo(infoNew);
            Assert.That(
                state[0],
                Is.EqualTo(infoNew),
                "book info should be inserted between book10 and book20"
            );
        }

        [Test]
        public void GetBookInfos_XmatterInCollectionPathAndBookName_StillListsBooks()
        {
            // A checkout, a user folder, a collection, or a book with "xmatter" somewhere in its
            // name must not lose its books. Only a folder named exactly xMatter is special.
            using (var folder = new TemporaryFolder("BookCollectionTests-xmatter-in-path"))
            {
                AddBook(folder, "alpha");
                AddBook(folder, "Xmatter Packs Test");
                var collection = new BookCollection(
                    folder.Path,
                    BookCollection.CollectionType.SourceCollection,
                    new BookSelection()
                );

                Assert.That(
                    collection.GetBookInfos().Select(b => b.FolderName).ToArray(),
                    Is.EquivalentTo(new[] { "alpha", "Xmatter Packs Test" })
                );
            }
        }

        [Test]
        public void GetBookInfos_CollectionFolderIsXMatter_ListsNothing()
        {
            // templates/xMatter is scanned like any other source collection folder, but what it
            // holds are front/back matter packs, not books.
            using (var folder = new TemporaryFolder("BookCollectionTests-xmatter-folders"))
            {
                var xmatterFolder = Path.Combine(folder.Path, "xMatter");
                Directory.CreateDirectory(xmatterFolder);
                var pack = Path.Combine(xmatterFolder, "SIL-Cameroon-Mothballed");
                Directory.CreateDirectory(pack);
                File.WriteAllText(
                    Path.Combine(pack, "SIL-Cameroon-Mothballed.htm"),
                    "<html></html>"
                );
                var collection = new BookCollection(
                    xmatterFolder,
                    BookCollection.CollectionType.SourceCollection,
                    new BookSelection()
                );

                Assert.That(collection.GetBookInfos(), Is.Empty);
            }
        }

        [Test]
        public void GetBookInfos_BookFolderNamedXMatter_IsSkipped()
        {
            using (var folder = new TemporaryFolder("BookCollectionTests-xmatter-child"))
            {
                AddBook(folder, "alpha");
                AddBook(folder, "xMatter");
                var collection = new BookCollection(
                    folder.Path,
                    BookCollection.CollectionType.SourceCollection,
                    new BookSelection()
                );

                Assert.That(
                    collection.GetBookInfos().Select(b => b.FolderName).ToArray(),
                    Is.EqualTo(new[] { "alpha" })
                );
            }
        }

        [Test]
        public void InsertBook_Present_Replaces()
        {
            var info1 = new BookInfo("book1", true);
            var info2 = new BookInfo("book2", true);
            var info3 = new BookInfo("book10", true);
            var info4 = new BookInfo("book20", true);
            var infoNew = new BookInfo("book10", true);
            var state = new List<BookInfo>(new[] { info1, info2, info3, info4 });
            var collection = new BookCollection(state);
            collection.InsertBookInfo(infoNew);
            Assert.That(state[2], Is.EqualTo(infoNew), "book info should replace existing book");
            Assert.That(state, Has.Count.EqualTo(4));
        }

        // Can't find a way to test this now that debounce (using timers) is in this class.
        // Any way I can find to wait for it prevents the timer ticking, even calling DoEvents()
        // repeatedly.
        //[Test]
        //public void WatchDirectory_CausesNotification_OnAddFile()
        //{
        //	var temp = new TemporaryFolder("BookCollectionWatch");
        //	var collection = new BookCollection(temp.Path, BookCollection.CollectionType.SourceCollection, null);
        //	collection.WatchDirectory();
        //	bool gotNotification = false;
        //	collection.FolderContentChanged += (sender, args) =>
        //	{
        //		gotNotification = true;
        //	};
        //	File.WriteAllText(Path.Combine(temp.Path, "somefile"), @"This is some test data");
        //	// It takes a little time to get the notification. This tells NUnit to try every 20ms for up to 1s.
        //	Assert.That(() => gotNotification, Is.True.After(1000, 20));
        //}
    }
}
