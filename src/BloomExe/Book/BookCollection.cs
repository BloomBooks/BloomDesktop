using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using Bloom.Edit;
using Palaso.Reporting;

namespace Bloom.Book
{
	public class BookCollection
	{
		public enum CollectionType
		{
			TheOneEditableCollection,
			TemplateCollection
		}
		public delegate BookCollection Factory(string path, CollectionType collectionType);//autofac uses this

		public EventHandler CollectionChanged;

		private readonly string _path;
		private readonly Book.Factory _bookFactory;
		private readonly BookStorage.Factory _storageFactory;
		private readonly BookStarter.Factory _bookStarterFactory;
		private readonly BookSelection _bookSelection;
		private readonly EditBookCommand _editBookCommand;
		private readonly LibrarySettings _librarySettings;


		//for moq only
		public BookCollection()
		{
		}

		public BookCollection(string path, CollectionType collectionType,
			Book.Factory bookFactory, BookStorage.Factory storageFactory,
			BookStarter.Factory bookStarterFactory, BookSelection bookSelection,
			CreateFromTemplateCommand createFromTemplateCommand,
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
				createFromTemplateCommand.Subscribe(CreateFromTemplate);
			}
		}

		public CollectionType Type { get; private set; }

		private void CreateFromTemplate(Book templateBook)
		{
			//var x = _librarySettings.IsShellLibrary; //need to differentiate between template and shell, as well our our mode
			var starter = _bookStarterFactory();
			var newBookFolder = starter.CreateBookOnDiskFromTemplate(templateBook.FolderPath, _path);
			if (Configurator.IsConfigurable(newBookFolder))
			{
				var c = new Configurator(_path);
				if (DialogResult.Cancel == c.ShowConfigurationDialog(newBookFolder))
				{
					return; // the template had a configuration page and they clicked "cancel"
				}
				c.ConfigureBook(BookStorage.FindBookHtmlInFolder(newBookFolder));
			}

			ListOfBooksIsOutOfDate();
			NotifyCollectionChanged();
			var newBook = _books.Find(b => b.FolderPath == newBookFolder);

			if (_bookSelection != null)
			{
				 _bookSelection.SelectBook(newBook);
			}
			//enhance: would be nice to know if this is a new shell
			if(templateBook.IsShellOrTemplate)
			{
				UsageReporter.SendNavigationNotice("Create/"+templateBook.CategoryForUsageReporting+"/"+templateBook.Title);
			}

			_editBookCommand.Raise(newBook);
		}

		private void NotifyCollectionChanged()
		{
			if (CollectionChanged != null)
				CollectionChanged.Invoke(this, null);
		}

		public void DeleteBook(Book book)
		{
			if(!book.Delete())
				return;
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

		private List<Book> _books;
		private void ListOfBooksIsOutOfDate()
		{
			_books = null;
		}

		public virtual IEnumerable<Book> GetBooks()
		{
			if (_books == null)
			{
				LoadBooks();
			}

			return _books;
		}

		private void LoadBooks()
		{
			_books = new List<Book>();
			foreach(string path in Directory.GetDirectories(_path))
			{
				if (Path.GetFileName(path).StartsWith("."))//as in ".hg"
					continue;
				try
				{
					var book = _bookFactory(_storageFactory(path), Type== CollectionType.TheOneEditableCollection);
					Debug.WriteLine(book.Title);
					_books.Add(book);
				}
				catch(Exception e)
				{
					if (e.InnerException != null)
					{
						e = e.InnerException;
					}
					Palaso.Reporting.ErrorReport.NotifyUserOfProblem(e, "Could not load " + path);
				}
			}
		}


	}
}