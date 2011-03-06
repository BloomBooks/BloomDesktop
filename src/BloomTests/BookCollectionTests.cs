using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Bloom;
using Bloom.Edit;
using Bloom.Library;
using NUnit.Framework;
using Palaso.IO;
using Palaso.TestUtilities;

namespace BloomTests
{
	public class BookCollectionTests
	{
		private BookCollection _collection;
		private TemporaryFolder _folder;
		private IFileLocator _fileLocator;

		[SetUp]
		public void Setup()
		{
			_folder  =new TemporaryFolder("BookCollectionTests");
			_fileLocator = new FileLocator(new string[]{});

			_collection = new BookCollection(_folder.Path, BookCollection.CollectionType.TheOneEditableCollection, BookFactory,
				BookStorageFactory, null,null, new CreateFromTemplateCommand());
		}

		 Book BookFactory(BookStorage storage, bool editable)
		 {
			 return new Book(storage, true, null, null, null,
													  new PageSelection(),
													  new PageListChangedEvent());
		 }

		 BookStorage BookStorageFactory(string folderPath)
		 {
			 return new BookStorage(folderPath,_fileLocator);
		 }

		[Test]
		public void DeleteBook_FirstBookInEditableCollection_RemovedFromCollection()
		{
			AddBook();
			var book = _collection.GetBooks().First();
			var bookFolder = book.FolderPath;
			_collection.DeleteBook(book);

			Assert.IsFalse(_collection.GetBooks().Contains(book));
			Assert.IsFalse(Directory.Exists(bookFolder));
		}

		[Test]
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
