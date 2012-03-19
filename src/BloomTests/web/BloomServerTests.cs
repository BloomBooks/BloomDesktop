using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using Bloom;
using Bloom.Book;
using Bloom.Edit;
using Bloom.web;
using Moq;
using NUnit.Framework;
using Palaso.IO;
using Palaso.TestUtilities;

namespace BloomTests.web
{
	[TestFixture, Ignore]
	public class BloomServerTests
	{
		private TemporaryFolder _folder;
		private FileLocator _fileLocator;
		private Mock<BookCollection> _vernacularLibraryCollection;
		private List<Bloom.Book.Book> _bookList;
		private Mock<StoreCollectionList> _storeCollectionList;
		private Mock<LibrarySettings> _librarySettings;

		[SetUp]
		public void Setup()
		{
			_folder = new TemporaryFolder("BookCollectionTests");
			//			_fileLocator = new BloomFileLocator(new LibrarySettings(), new XMatterPackFinder(new string[]{}), new string[] { FileLocator.GetDirectoryDistributedWithApplication("root"), FileLocator.GetDirectoryDistributedWithApplication("factoryCollections") });
			_fileLocator = new FileLocator(new string[] { FileLocator.GetDirectoryDistributedWithApplication("root"), FileLocator.GetDirectoryDistributedWithApplication("factoryCollections") });

//			_vernacularLibraryCollection = new BookCollection(_folder.Path, BookCollection.CollectionType.TheOneEditableCollection, BookFactory,
//				BookStorageFactory, null, null, new CreateFromTemplateCommand(), new EditBookCommand());

			_vernacularLibraryCollection = new Moq.Mock<BookCollection>();
			_bookList = new List<Bloom.Book.Book>();
			_vernacularLibraryCollection.Setup(x => x.GetBooks()).Returns(_bookList);
			_storeCollectionList = new Mock<StoreCollectionList>();
			_storeCollectionList.Setup(x => x.GetStoreCollections()).Returns(() => GetStoreCollections());
			_librarySettings = new Mock<LibrarySettings>();
			_librarySettings.Setup(x => x.LibraryName).Returns(() => "Foo");

		}

		public virtual IEnumerable<BookCollection> GetStoreCollections()
		{
			Mock<BookCollection> c = new Mock<BookCollection>();
			c.Setup(x => x.Name).Returns("alpha");
			c.Setup(x => x.GetBooks()).Returns(_bookList);
			yield return c.Object;
			Mock<BookCollection> b = new Mock<BookCollection>();
			b.Setup(x => x.Name).Returns("beta");
			b.Setup(x => x.GetBooks()).Returns(_bookList);
			yield return b.Object;
		}

		Bloom.Book.Book BookFactory(BookStorage storage, bool editable)
		{
			return new Bloom.Book.Book(storage, true, null, new LibrarySettings(new NewLibraryInfo() { PathToSettingsFile = LibrarySettings.GetPathForNewSettings(_folder.Path, "test"), VernacularIso639Code = "xyz" }), null,
													 new PageSelection(),
													 new PageListChangedEvent(), new BookRefreshEvent());
		}

		BookStorage BookStorageFactory(string folderPath)
		{
			return new BookStorage(folderPath, _fileLocator);
		}

		[Test]
		public void GetLibaryPage_ReturnsLibraryPage()
		{
			var b = CreateBloomServer();
			var transaction = new PretendRequestInfo("http://localhost:8089/bloom/library/library.htm");
			b.MakeReply(transaction);
			Assert.IsTrue(transaction.ReplyContents.Contains("library.css"));
		}

		private BloomServer CreateBloomServer()
		{
			return new BloomServer(_librarySettings.Object, _vernacularLibraryCollection.Object, _storeCollectionList.Object,null);
		}

		[Test]
		public void GetVernacularBookList_ThereAreNone_ReturnsNoListItems()
		{
			var b = CreateBloomServer();
			var transaction = new PretendRequestInfo("http://localhost:8089/bloom/libraryContents");
			_bookList.Clear();
			b.MakeReply(transaction);
			AssertThatXmlIn.String(transaction.ReplyContentsAsXml).HasNoMatchForXpath("//li");
		}
		[Test]
		public void GetVernacularBookList_ThereAre2_Returns2ListItems()
		{
			var b = CreateBloomServer();
			var transaction = new PretendRequestInfo("http://localhost:8089/bloom/libraryContents");
			AddBook("1","one");
			AddBook("2", "two");
			b.MakeReply(transaction);
			AssertThatXmlIn.String(transaction.ReplyContentsAsXml).HasSpecifiedNumberOfMatchesForXpath("//li", 2);
		}

		[Test]
		public void GetStoreBooks_ThereAre2_Returns2CollectionItems()
		{
			var b = CreateBloomServer();
			var transaction = new PretendRequestInfo("http://localhost:8089/bloom/storeCollectionList");
			b.MakeReply(transaction);
			AssertThatXmlIn.String(transaction.ReplyContentsAsXml).HasSpecifiedNumberOfMatchesForXpath("//li//h2[text()='alpha']", 1);
			AssertThatXmlIn.String(transaction.ReplyContentsAsXml).HasSpecifiedNumberOfMatchesForXpath("//li//h2[text()='beta']", 1);
			AssertThatXmlIn.String(transaction.ReplyContentsAsXml).HasSpecifiedNumberOfMatchesForXpath("//li/ul", 2);
		}

		private void AddBook(string id, string title)
		{
			var b = new Moq.Mock<Bloom.Book.Book>();
			b.SetupGet(x => x.Id).Returns(id);
			b.SetupGet(x => x.Title).Returns(title);
			b.SetupGet(x => x.FolderPath).Returns(Path.GetTempPath);//TODO. this works at the moment, cause we just need some folder which exists
			_bookList.Add(b.Object);
		}
	}

}
