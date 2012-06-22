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

			_collection = new BookCollection(_folder.Path, BookCollection.CollectionType.TheOneEditableCollection, BookFactory,
				BookStorageFactory, null,null, new CreateFromSourceBookCommand(), new EditBookCommand());
		}

		 Bloom.Book.Book BookFactory(BookStorage storage, bool editable)
		 {
			 return new Bloom.Book.Book(storage, true, null, new CollectionSettings(new NewCollectionInfo() { PathToSettingsFile = CollectionSettings.GetPathForNewSettings(_folder.Path, "test"),  Language1Iso639Code = "xyz" }), null,
													  new PageSelection(),
													  new PageListChangedEvent(), new BookRefreshEvent());
		 }

		 BookStorage BookStorageFactory(string folderPath)
		 {
			 return new BookStorage(folderPath,_fileLocator);
		 }

		[Test, Ignore("fix me")]
		public void DeleteBook_FirstBookInEditableCollection_RemovedFromCollection()
		{
			AddBook();
			var book = _collection.GetBooks().First();
			var bookFolder = book.FolderPath;
			_collection.DeleteBook(book);

			Assert.IsFalse(_collection.GetBooks().Contains(book));
			Assert.IsFalse(Directory.Exists(bookFolder));
		}

		[Test, Ignore("fix me")]
		public void DeleteBook_FirstBookInEditableCollection_RaisesCollectionChangedEvent()
		{
			AddBook();
			bool triggered=false;
			_collection.CollectionChanged+= (x,y)=>triggered=true;
			_collection.DeleteBook(_collection.GetBooks().First());
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
