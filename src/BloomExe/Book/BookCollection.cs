using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
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

		public BookCollection(string path, CollectionType collectionType,
			Book.Factory bookFactory, BookStorage.Factory storageFactory,
			BookStarter.Factory bookStarterFactory, BookSelection bookSelection,
			CreateFromTemplateCommand createFromTemplateCommand)
		{
			_path = path;
			_bookFactory = bookFactory;
			_storageFactory = storageFactory;
			_bookStarterFactory = bookStarterFactory;
			_bookSelection = bookSelection;
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
			UsageReporter.SendNavigationNotice("Create/"+templateBook.CategoryForUsageReporting+"/"+templateBook.Title);
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

		public string Name
		{
			get { return Path.GetFileName(_path); }
		}

		private List<Book> _books;
		private void ListOfBooksIsOutOfDate()
		{
			_books = null;
		}

		public IEnumerable<Book> GetBooks()
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