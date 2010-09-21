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

		public BookCollection(string path, CollectionType collectionType,
			Book.Factory bookFactory, BookStorage.Factory storageFactory,
			BookStarter.Factory bookStarterFactory,
			CreateFromTemplateCommand createFromTemplateCommand)
		{
			_path = path;
			_bookFactory = bookFactory;
			_storageFactory = storageFactory;
			_bookStarterFactory = bookStarterFactory;
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
			var starter=_bookStarterFactory();
			starter.CreateBookOnDiskFromTemplate(templateBook.FolderPath,_path);
			CollectionChanged.Invoke(this, null);
		}

		public string Name
		{
			get { return Path.GetFileName(_path); }
		}

		public IEnumerable<Book> GetBooks()
		{
			foreach(string path in Directory.GetDirectories(_path))
			{
//                yield return _bookFactory(_storageFactory(path));
				var book = _bookFactory(_storageFactory(path));
				Debug.WriteLine(book.Title);
				yield return book;
			}
		}


	}
}