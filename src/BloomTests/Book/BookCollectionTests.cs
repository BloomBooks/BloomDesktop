using System.IO;
using System.Linq;
using Bloom;
using Bloom.Book;
using Bloom.Collection;
using Bloom.Edit;
using NUnit.Framework;
using Palaso.IO;
using Palaso.TestUtilities;

namespace BloomTests.Book
{
	public class BookCollectionTests
	{
		private BookCollection _collection;
		private TemporaryFolder _folder;
		private IChangeableFileLocator _fileLocator;

		[SetUp]
		public void Setup()
		{
			Palaso.Reporting.ErrorReport.IsOkToInteractWithUser = false;
			_folder  =new TemporaryFolder("BookCollectionTests");
//			_fileLocator = new BloomFileLocator(new CollectionSettings(), new XMatterPackFinder(new string[]{}), new string[] { FileLocator.GetDirectoryDistributedWithApplication("root"), FileLocator.GetDirectoryDistributedWithApplication("factoryCollections") });
			_fileLocator = new FileLocator(new string[] { FileLocator.GetDirectoryDistributedWithApplication("root"), FileLocator.GetDirectoryDistributedWithApplication("factoryCollections") });

			_collection = new BookCollection(_folder.Path, BookCollection.CollectionType.TheOneEditableCollection, new BookSelection());
		}

		 Bloom.Book.Book BookFactory(BookInfo bookInfo, IBookStorage storage, bool editable)
		 {
			 return new Bloom.Book.Book(bookInfo,  storage, true, null, new CollectionSettings(new NewCollectionSettings() { PathToSettingsFile = CollectionSettings.GetPathForNewSettings(_folder.Path, "test"),  Language1Iso639Code = "xyz" }), null,
													  new PageSelection(),
													  new PageListChangedEvent(), new BookRefreshEvent());
		 }

		 BookStorage BookStorageFactory(string folderPath)
		 {
			 return new BookStorage(folderPath,_fileLocator, new BookRenamedEvent());
		 }

		[Test, Ignore("fix me")]
		public void DeleteBook_FirstBookInEditableCollection_RemovedFromCollection()
		{
			AddBook();
			var book = _collection.GetBookInfos().First();
			var bookFolder = book.FolderPath;
			_collection.DeleteBook(book);

			Assert.IsFalse(_collection.GetBookInfos().Contains(book));
			Assert.IsFalse(Directory.Exists(bookFolder));
		}

		[Test, Ignore("fix me")]
		public void DeleteBook_FirstBookInEditableCollection_RaisesCollectionChangedEvent()
		{
			AddBook();
			bool triggered=false;
			_collection.CollectionChanged+= (x,y)=>triggered=true;
			_collection.DeleteBook(_collection.GetBookInfos().First());
			Assert.IsTrue(triggered);
		}

		private void AddBook()
		{
			string path = _folder.Combine("alpha");
			Directory.CreateDirectory(path);
			File.WriteAllText(Path.Combine(path,"alpha.htm"), @"<html></html>");
		}
	}
}
