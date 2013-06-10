using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using Bloom;
using Bloom.Book;
using Bloom.Collection;
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
		private List<Bloom.Book.BookInfo> _bookInfoList;
		private Mock<SourceCollectionsList> _storeCollectionList;
		private Mock<CollectionSettings> _librarySettings;

		[SetUp]
		public void Setup()
		{
			_folder = new TemporaryFolder("BookCollectionTests");
			//			_fileLocator = new BloomFileLocator(new CollectionSettings(), new XMatterPackFinder(new string[]{}), new string[] { FileLocator.GetDirectoryDistributedWithApplication("root"), FileLocator.GetDirectoryDistributedWithApplication("factoryCollections") });
			_fileLocator = new FileLocator(new string[] { FileLocator.GetDirectoryDistributedWithApplication("root"), FileLocator.GetDirectoryDistributedWithApplication("factoryCollections") });

//			_vernacularLibraryCollection = new BookCollection(_folder.Path, BookCollection.CollectionType.TheOneEditableCollection, BookFactory,
//				BookStorageFactory, null, null, new CreateFromSourceBookCommand(), new EditBookCommand());

			_vernacularLibraryCollection = new Moq.Mock<BookCollection>();
			_bookInfoList = new List<Bloom.Book.BookInfo>();
			_vernacularLibraryCollection.Setup(x => x.GetBookInfos()).Returns(_bookInfoList);
			_storeCollectionList = new Mock<SourceCollectionsList>();
			_storeCollectionList.Setup(x => x.GetSourceCollections()).Returns(() => GetStoreCollections());
			_librarySettings = new Mock<CollectionSettings>();
			_librarySettings.Setup(x => x.CollectionName).Returns(() => "Foo");

		}

		public virtual IEnumerable<BookCollection> GetStoreCollections()
		{
			Mock<BookCollection> c = new Mock<BookCollection>();
			c.Setup(x => x.Name).Returns("alpha");
			c.Setup(x => x.GetBookInfos()).Returns(_bookInfoList);
			yield return c.Object;
			Mock<BookCollection> b = new Mock<BookCollection>();
			b.Setup(x => x.Name).Returns("beta");
			b.Setup(x => x.GetBookInfos()).Returns(_bookInfoList);
			yield return b.Object;
		}

		Bloom.Book.Book BookFactory(BookStorage storage, bool editable)
		{
			return new Bloom.Book.Book(new BookInfo(storage.FolderPath, true),  storage, null, new CollectionSettings(new NewCollectionSettings() { PathToSettingsFile = CollectionSettings.GetPathForNewSettings(_folder.Path, "test"), Language1Iso639Code = "xyz" }), null,
													 new PageSelection(),
													 new PageListChangedEvent(), new BookRefreshEvent());
		}

		BookStorage BookStorageFactory(string folderPath)
		{
			return new BookStorage(folderPath, _fileLocator, new BookRenamedEvent(), new CollectionSettings());
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
			_bookInfoList.Clear();
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
			var b = new Moq.Mock<Bloom.Book.BookInfo>();
			b.SetupGet(x => x.Id).Returns(id);
			b.SetupGet(x => x.QuickTitleUserDisplay).Returns(title);
			b.SetupGet(x => x.FolderPath).Returns(Path.GetTempPath);//TODO. this works at the moment, cause we just need some folder which exists
			_bookInfoList.Add(b.Object);
		}
	}

}
