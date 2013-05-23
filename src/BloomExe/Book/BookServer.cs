using System;
using System.IO;
using System.Windows.Forms;
using Bloom.Edit;
using Palaso.Reporting;

namespace Bloom.Book
{
	public class BookServer
	{
		private readonly Book.Factory _bookFactory;
		private readonly BookStorage.Factory _storageFactory;
		private readonly BookStarter.Factory _bookStarterFactory;

		public BookServer(Book.Factory bookFactory, BookStorage.Factory storageFactory,
						  BookStarter.Factory bookStarterFactory)
		{
			_bookFactory = bookFactory;
			_storageFactory = storageFactory;
			_bookStarterFactory = bookStarterFactory;
		}

		public Book GetBookFromBookInfo(BookInfo bookInfo)
		{
			//Review: Note that this isn't doing any caching yet... worried that caching will just eat up memory, but if anybody is holding onto these, then the memory won't be freed anyhow

			var book = _bookFactory(bookInfo, _storageFactory(bookInfo.FolderPath));
			return book;
		}

		public Book CreateFromSourceBook(Book sourceBook, string containingDestinationFolder)
		{
			string pathToFolderOfNewBook = null;

			Logger.WriteMinorEvent("Starting CreateFromSourceBook({0})", sourceBook.FolderPath);
			try
			{
				var starter = _bookStarterFactory();
				pathToFolderOfNewBook = starter.CreateBookOnDiskFromTemplate(sourceBook.FolderPath, containingDestinationFolder);
				if (Configurator.IsConfigurable(pathToFolderOfNewBook))
				{
					var c = new Configurator(containingDestinationFolder);
					if (DialogResult.Cancel == c.ShowConfigurationDialog(pathToFolderOfNewBook))
					{
						return null; // the template had a configuration page and they clicked "cancel"
					}
					c.ConfigureBook(BookStorage.FindBookHtmlInFolder(pathToFolderOfNewBook));
				}

				var newBookInfo = new BookInfo(pathToFolderOfNewBook,true); // _bookInfos.Find(b => b.FolderPath == pathToFolderOfNewBook);
				if (newBookInfo is ErrorBookInfo)
				{
					throw ((ErrorBookInfo)newBookInfo).Exception;
				}

				Book newBook = GetBookFromBookInfo(newBookInfo);

				//Hack: this is a bit of a hack, to handle problems where we make the book with the suggested initial name, but the title is still something else
				var name = Path.GetFileName(newBookInfo.FolderPath); // this way, we get "my book 1", "my book 2", etc.
				newBook.SetTitle(name);

				Logger.WriteMinorEvent("Finished CreateFromnewBook({0})", newBook.FolderPath);
				Logger.WriteEvent("CreateFromSourceBook({0})", newBook.FolderPath);
				return newBook;
			}
			catch (Exception)
			{
				Logger.WriteEvent("Cleaning up after error CreateFromSourceBook({0})", pathToFolderOfNewBook);
				//clean up this ill-fated book folder up
				if (!string.IsNullOrEmpty(pathToFolderOfNewBook) && Directory.Exists(pathToFolderOfNewBook))
					Directory.Delete(pathToFolderOfNewBook, true);
				throw;
			}
		}
	}
}
