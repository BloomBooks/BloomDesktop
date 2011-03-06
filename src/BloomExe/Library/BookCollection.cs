using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace Bloom.Library
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
			var path = starter.CreateBookOnDiskFromTemplate(templateBook.FolderPath, _path);
			ListOfBooksIsOutOfDate();
			if (CollectionChanged != null)
				CollectionChanged.Invoke(this, null);
			var newBook = _books.Find(b => b.FolderPath == path);
			if (_bookSelection != null)
			{
				 _bookSelection.SelectBook(newBook);
			}
		}

		public void DeleteBook(Book book)
		{
			book.Delete();
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
				var book = _bookFactory(_storageFactory(path), Type== CollectionType.TheOneEditableCollection);
				Debug.WriteLine(book.Title);
				_books.Add(book);
			}
		}


	}
}