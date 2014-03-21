using System.IO;
using System.Threading;
using System.Xml;
using Bloom;
using Bloom.Book;
using Bloom.Collection;
using Bloom.Edit;
using Moq;
using NUnit.Framework;
using Palaso.Extensions;
using Palaso.IO;
using Palaso.Progress;
using Palaso.Reporting;
using Palaso.TestUtilities;

namespace BloomTests.Book
{
	[TestFixture]
	public class BookStorageTests
	{
	    private FileLocator _fileLocator;
        private TemporaryFolder _fixtureFolder; 
        private TemporaryFolder _folder;
	    private string _bookPath;

	    [SetUp]
        public void Setup()
        {
            ErrorReport.IsOkToInteractWithUser = false;
            _fileLocator = new FileLocator(new string[]
			                               	{
			                               		FileLocator.GetDirectoryDistributedWithApplication( "factoryCollections"),
			                               		FileLocator.GetDirectoryDistributedWithApplication( "factoryCollections", "Templates", "Basic Book"),
												FileLocator.GetDirectoryDistributedWithApplication( "xMatter")
			                               	});
	        _fixtureFolder = new TemporaryFolder("BloomBookStorageTest");
            _folder = new TemporaryFolder(_fixtureFolder,"theBook");
            
            _bookPath = _folder.Combine("theBook.htm");
        }

        [TearDown]
        public void TearDown()
        {
            _fixtureFolder.Dispose();
        }

	    [Test]
        public void Save_BookHadOnlyPaperSizeStyleSheet_StillHasIt()
        {
            GetInitialStorageWithCustomHtml("<html><head><link rel='stylesheet' href='Basic Book.css' type='text/css' /></head><body><div class='bloom-page'></div></body></html>");
             AssertThatXmlIn.HtmlFile(_bookPath).HasSpecifiedNumberOfMatchesForXpath("//link[contains(@href, 'Basic Book')]", 1);
        }

        [Test]
        public void Save_BookHadEditStyleSheet_NowHasPreviewAndBase()
        {
            GetInitialStorageWithCustomHtml("<html><head> href='file://blahblah\\editMode.css' type='text/css' /></head><body><div class='bloom-page'></div></body></html>");
            AssertThatXmlIn.HtmlFile(_bookPath).HasSpecifiedNumberOfMatchesForXpath("//link[contains(@href, 'basePage')]", 1);
            AssertThatXmlIn.HtmlFile(_bookPath).HasSpecifiedNumberOfMatchesForXpath("//link[contains(@href, 'preview')]", 1);
        }

//
//        [Test]
//        public void Delete_IsDeleted()
//        {
//            BookStorage storage = GetInitialStorageWithCustomHtml();
//            Assert.IsTrue(Directory.Exists(_folder.Path)); 
//            Assert.IsTrue(storage.DeleteBook());
//            Thread.Sleep(2000);
//            Assert.IsFalse(Directory.Exists(_folder.Path));
//        }

        private BookStorage GetInitialStorageWithCustomHtml(string html)
        {
            File.WriteAllText(_bookPath, html);
            var projectFolder = new TemporaryFolder("BookStorageTests_ProjectCollection");
            var collectionSettings = new CollectionSettings(Path.Combine(projectFolder.Path, "test.bloomCollection"));
            var storage = new BookStorage(_folder.Path, _fileLocator, new BookRenamedEvent(), collectionSettings);
            storage.Save();
            return storage;
        }

	    private BookStorage GetInitialStorage()
	    {
	        return GetInitialStorageWithCustomHtml("<html><head> href='file://blahblah\\editMode.css' type='text/css' /></head><body><div class='bloom-page'></div></body></html>");
	    }

		private BookStorage GetInitialStorageWithCustomHead(string head)
		{
			File.WriteAllText(_bookPath, "<html><head>"+head+" </head></body></html>");
			var storage = new BookStorage(_folder.Path, _fileLocator, new BookRenamedEvent(), new CollectionSettings());
			storage.Save();
			return storage;
		}

        private BookStorage GetInitialStorageWithDifferentFileName(string bookName)
        {
            var bookPath = _folder.Combine(bookName + ".htm");
            File.WriteAllText(bookPath, "<html><head> href='file://blahblah\\editMode.css' type='text/css' /></head><body><div class='bloom-page'></div></body></html>");
            var projectFolder = new TemporaryFolder("BookStorageTests_ProjectCollection");
            var collectionSettings = new CollectionSettings(Path.Combine(projectFolder.Path, "test.bloomCollection"));
            var storage = new BookStorage(_folder.Path, _fileLocator, new BookRenamedEvent(), collectionSettings);
            storage.Save();
            return storage;
        }

	    [Test]
        public void SetBookName_EasyCase_ChangesFolderAndFileName()
	    {
           var storage = GetInitialStorage();
           using (var newFolder = new TemporaryFolder(_fixtureFolder,"newName"))
           {
               Directory.Delete(newFolder.Path);
               ChangeNameAndCheck(newFolder, storage);
           }
	    }

        [Test]
        public void SetBookName_FolderWithNameAlreadyExists_AddsANumberToName()
        {
            using (var original = new TemporaryFolder(_folder, "original"))
            using (var x = new TemporaryFolder(_folder, "foo"))
            using (var y = new TemporaryFolder(_folder, "foo1"))
            using (var z = new TemporaryFolder(_folder, "foo2"))
            {

                File.WriteAllText(Path.Combine(original.Path, "original.htm"), "<html><head> href='file://blahblah\\editMode.css' type='text/css' /></head><body><div class='bloom-page'></div></body></html>");

                var projectFolder = new TemporaryFolder("BookStorage_ProjectCollection");
                var collectionSettings = new CollectionSettings(Path.Combine(projectFolder.Path, "test.bloomCollection"));
                var storage = new BookStorage(original.Path, _fileLocator, new BookRenamedEvent(), collectionSettings); 
                storage.Save();
                
                Directory.Delete(z.Path);
                //so, we ask for "foo", but should get "foo2", because there is already a foo and foo1
            var newBookName = Path.GetFileName(x.Path);
            storage.SetBookName(newBookName);
	        var newPath = z.Combine("foo2.htm");
            Assert.IsTrue(Directory.Exists(z.Path), "Expected folder:" + z.Path);
            Assert.IsTrue(File.Exists(newPath), "Expected file:" + newPath);
            }
        }

        [Test]
        public void SetBookName_FolderNameWasDifferentThanFileName_ChangesFolderAndFileName()
        {
            var storage = GetInitialStorageWithDifferentFileName("foo");
            using (var newFolder = new TemporaryFolder(_fixtureFolder,"newName"))
            {
                Directory.Delete(newFolder.Path);
                ChangeNameAndCheck(newFolder, storage);
            }
        }

        [Test]
        public void SetBookName_NameIsNotValidFileName_UsesSanitizedName()
        {
            var storage = GetInitialStorage();
            storage.SetBookName("/b?loom*test/");
            Assert.IsTrue(Directory.Exists(_fixtureFolder.Combine("b loom test")));
            Assert.IsTrue(File.Exists(_fixtureFolder.Combine("b loom test", "b loom test.htm")));
        }

		/// <summary>
		/// This is really testing some Book.cs functionality, but it has to manipulate real files with a real storage,
		/// so it seems to fit better here.
		/// </summary>
		[Test]
		public void BringBookUpToDate_ConvertsTagsToJson()
		{
			var storage = GetInitialStorage();
			var locator = (FileLocator)storage.GetFileLocator();
			string root = FileLocator.GetDirectoryDistributedWithApplication("BloomBrowserUI");
			locator.AddPath(root.CombineForPath("bookLayout"));
			var folder = storage.FolderPath;
			var tagsPath = Path.Combine(folder, "tags.txt");
			File.WriteAllText(tagsPath, "suitableForMakingShells\nexperimental\nfolio\n");
			var collectionSettings = new CollectionSettings(new NewCollectionSettings() { PathToSettingsFile = CollectionSettings.GetPathForNewSettings(folder, "test"), Language1Iso639Code = "xyz", Language2Iso639Code = "en", Language3Iso639Code = "fr" });
			var book = new Bloom.Book.Book(new BookInfo(folder, true), storage, new Moq.Mock<ITemplateFinder>().Object,
				collectionSettings,
				new Moq.Mock<HtmlThumbNailer>(new object[] { 60, 60 }).Object, new Mock<PageSelection>().Object, new PageListChangedEvent(), new BookRefreshEvent());

			book.BringBookUpToDate(new NullProgress());

			Assert.That(!File.Exists(tagsPath), "The tags.txt file should have been removed");
			Assert.That(storage.MetaData.IsSuitableForMakingShells, Is.True);
			Assert.That(storage.MetaData.IsFolio, Is.True);
			Assert.That(storage.MetaData.IsExperimental, Is.True);
		}

		[Test]
		public void Save_SetsJsonFormatVersion()
		{
			var storage = GetInitialStorage();
			Assert.That(storage.MetaData.FormatVersion, Is.EqualTo(BookStorage.kBloomFormatVersion));
		}


	    private void ChangeNameAndCheck(TemporaryFolder newFolder, BookStorage storage)
	    {
            var newBookName = Path.GetFileName(newFolder.Path);
            storage.SetBookName(newBookName);
	        var newPath = newFolder.Combine(newBookName+".htm");
	        Assert.IsTrue(Directory.Exists(newFolder.Path), "Expected folder:" + newFolder.Path); 
	        Assert.IsTrue(File.Exists(newPath), "Expected file:" +newPath);
	    }
	}
}
