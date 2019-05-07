using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bloom.Book;

namespace BloomTests.TestDoubles.Book
{
	// A simplified implementation of BookServer that cuts some corners in order to ease setup.
	class FakeBookServer : BookServer
	{
		public FakeBookServer() : base(null, null, null, null)
		{
		}

		/// <summary>
		/// A dumbed down implementation that is able to return a book with the BookInfo and set the book's FolderPath.
		/// </summary>
		/// <returns></returns>
		public override Bloom.Book.Book GetBookFromBookInfo(BookInfo bookInfo, bool forSelectedBook = false)
		{
			var collectionSettings = new Bloom.Collection.CollectionSettings();
			var fileLocator = new Bloom.BloomFileLocator(collectionSettings, new XMatterPackFinder(Enumerable.Empty<string>()), Enumerable.Empty<string>(), Enumerable.Empty<string>(),
			Enumerable.Empty<string>());

			// Setting storage is neeeded to get it to populate the book's FolderPath.
			var storage = new BookStorage(bookInfo.FolderPath, fileLocator, new Bloom.BookRenamedEvent(), collectionSettings);

			var book = new Bloom.Book.Book(bookInfo, storage);
			return book;
		}
	}
}
