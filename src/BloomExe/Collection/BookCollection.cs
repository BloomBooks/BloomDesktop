using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using Bloom.Book;
using Bloom.Edit;
using DesktopAnalytics;
using Palaso.Progress;
using Palaso.Reporting;

namespace Bloom.Collection
{
	public class BookCollection
	{
		public enum CollectionType
		{
			TheOneEditableCollection,
			SourceCollection
		}
		public delegate BookCollection Factory(string path, CollectionType collectionType);//autofac uses this

		public EventHandler CollectionChanged;

		private readonly string _path;
		private List<Book.BookInfo> _bookInfos;
		private readonly Book.Book.Factory _bookFactory;
		private readonly BookStorage.Factory _storageFactory;
		private readonly BookStarter.Factory _bookStarterFactory;
		private readonly BookSelection _bookSelection;
		private readonly EditBookCommand _editBookCommand;

		//private Color[] kCoverColors = new Color[] { Color.FromArgb(225, 0, 0), Color.FromArgb(0, 225, 0), Color.FromArgb(0, 0, 225), Color.FromArgb(180, 180, 180) };
		private Color[] kCoverColors = new Color[] { Color.FromArgb(228, 140, 132), Color.FromArgb(176,222,228), Color.FromArgb(152, 208, 185), Color.FromArgb(194, 166, 191) };
		private int _coverColorIndex = 0;


		//for moq only
		public BookCollection()
		{
		}

		public BookCollection(string path, CollectionType collectionType,
			Book.Book.Factory bookFactory, BookStorage.Factory storageFactory,
			BookStarter.Factory bookStarterFactory, BookSelection bookSelection,
			CreateFromSourceBookCommand createFromSourceBookCommand,
			  EditBookCommand editBookCommand)
		{
			_path = path;
			_bookFactory = bookFactory;
			_storageFactory = storageFactory;
			_bookStarterFactory = bookStarterFactory;
			_bookSelection = bookSelection;
			_editBookCommand = editBookCommand;
			Type = collectionType;

			//we only pay attention if we are the editable collection 'round here.
			if (collectionType == CollectionType.TheOneEditableCollection)
			{
				createFromSourceBookCommand.Subscribe(CreateFromSourceBook);
				MakeCollectionCSSIfMissing();
			}
		}

		private void MakeCollectionCSSIfMissing()
		{
			string path = Path.Combine(_path, "customCollectionStyles.css");
			if(File.Exists(path))
				return;
			File.Copy(BloomFileLocator.GetFileDistributedWithApplication("root","collection styles override template.css"),path);
		}

		public CollectionType Type { get; private set; }

		private void CreateFromSourceBook(Book.Book sourceBook)
		{
			string pathToFolderOfNewBook = null;

			Logger.WriteMinorEvent("Starting CreateFromSourceBook({0})", sourceBook.FolderPath);
			try
			{
				var starter = _bookStarterFactory();
				pathToFolderOfNewBook = starter.CreateBookOnDiskFromTemplate(sourceBook.FolderPath, _path);
				if (Configurator.IsConfigurable(pathToFolderOfNewBook))
				{
					var c = new Configurator(_path);
					if (DialogResult.Cancel == c.ShowConfigurationDialog(pathToFolderOfNewBook))
					{
						return; // the template had a configuration page and they clicked "cancel"
					}
					c.ConfigureBook(BookStorage.FindBookHtmlInFolder(pathToFolderOfNewBook));
				}

				AddBookInfo(pathToFolderOfNewBook);
				NotifyCollectionChanged();

				var newBookInfo = _bookInfos.Find(b => b.FolderPath == pathToFolderOfNewBook);

				if (newBookInfo is ErrorBookInfo)
				{
					throw ((ErrorBookInfo)newBookInfo).Exception;
				}

				//Hack: this is a bit of a hack, to handle problems where we make the book with the suggested initial name, but the title is still something else
				var name = Path.GetFileName(newBookInfo.FolderPath); // this way, we get "my book 1", "my book 2", etc.

				Book.Book newBook = CreateBookFromBookInfo(newBookInfo);
				newBook.SetTitle(name);

				if (_bookSelection != null)
				{
					_bookSelection.SelectBook(newBook);
				}
				//enhance: would be nice to know if this is a new shell
				if (sourceBook.IsShellOrTemplate)
				{
					Analytics.Track("Create Book", new Dictionary<string, string>()
						{{"Category", sourceBook.CategoryForUsageReporting}});
				}
				_editBookCommand.Raise(newBook);
			}
			catch (Exception)
			{
				Logger.WriteEvent("Cleaning up after error CreateFromSourceBook({0})", sourceBook.FolderPath);
				//clean up this ill-fated book folder up
				if (!string.IsNullOrEmpty(pathToFolderOfNewBook) && Directory.Exists(pathToFolderOfNewBook))
					Directory.Delete(pathToFolderOfNewBook, true);
				throw;
			}
			Logger.WriteMinorEvent("Finished CreateFromSourceBook({0})", sourceBook.FolderPath);
			Logger.WriteEvent("CreateFromSourceBook({0})", sourceBook.FolderPath);
		}

		private Book.Book CreateBookFromBookInfo(BookInfo bookInfo)
		{
			var book = _bookFactory(bookInfo, _storageFactory(bookInfo.FolderPath), Type == CollectionType.TheOneEditableCollection);
			book.CoverColor = bookInfo.CoverColor;
			return book;
		}

		private void NotifyCollectionChanged()
		{
			if (CollectionChanged != null)
				CollectionChanged.Invoke(this, null);
		}

		public void DeleteBook(Book.BookInfo book)
		{
			var didDelete = Bloom.ConfirmRecycleDialog.Recycle(book.FolderPath);
			if (!didDelete)
				return;

			Logger.WriteEvent("After BookStorage.DeleteBook({0})", book.FolderPath);
			ListOfBooksIsOutOfDate();
			if (CollectionChanged != null)
				CollectionChanged.Invoke(this, null);
			if (_bookSelection != null)
			{
				_bookSelection.SelectBook(null);
			}
		}

		public virtual string Name
		{
			get { return Path.GetFileName(_path); }
		}

		public string PathToDirectory
		{
			get { return _path; }

		}


		private void ListOfBooksIsOutOfDate()
		{
			_bookInfos = null;
		}

		public virtual IEnumerable<Book.BookInfo> GetBookInfos()
		{
			if (_bookInfos == null)
			{
				LoadBooks();
			}

			return _bookInfos;
		}

		private void LoadBooks()
		{
			_bookInfos = new List<Book.BookInfo>();
			foreach(string path in Directory.GetDirectories(_path))
			{
				if (Path.GetFileName(path).StartsWith("."))//as in ".hg"
					continue;
				AddBookInfo(path);
			}
		}

		private void AddBookInfo(string path)
		{
			try
			{
				//this is handy when windows explorer won't let go of the thumbs.db file, but we want to delete the folder
				if (Directory.GetFiles(path, "*.htm").Length == 0)
					return;
				var bookInfo = new BookInfo(path)
					{
						CoverColor = NextBookColor()
					};
				_bookInfos.Add(bookInfo);
//    			var book = _bookFactory(_storageFactory(path), Type == CollectionType.TheOneEditableCollection);
//    			book.CoverColor = NextBookColor();
//    			Debug.WriteLine(book.Title);
//    			_books.Add(book);
			}
			catch (Exception e)
			{
				if (e.InnerException != null)
				{
					e = e.InnerException;
				}
				//_books.Add(new ErrorBook(e, path, Type == CollectionType.TheOneEditableCollection));
				_bookInfos.Add(new ErrorBookInfo(path, e){});
			}
		}

		protected Color CoverColor
		{
			get { throw new NotImplementedException(); }
			set { throw new NotImplementedException(); }
		}

		public Color NextBookColor()
		{
			return kCoverColors[_coverColorIndex++ % kCoverColors.Length];
		}

		public void DoChecksAndUpdatesOfAllBooks(IProgress progress)
		{
			int i = 0;
			foreach (var bookInfo in _bookInfos)
			{
				i++;
				var book = CreateBookFromBookInfo(bookInfo);
				//gets overwritten: progress.WriteStatus(book.TitleBestForUserDisplay);
				progress.WriteMessage("Processing " + book.TitleBestForUserDisplay + " " + i + "/" + _bookInfos.Count);
				book.BringBookUpToDate(progress);
			}
		}
	}
}