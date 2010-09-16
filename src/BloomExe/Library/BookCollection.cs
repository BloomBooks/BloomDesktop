using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace Bloom.Library
{
	public class BookCollection
	{
		public delegate BookCollection Factory(string path);//autofac uses this

		private readonly string _path;
		private readonly Book.Factory _bookFactory;
		private readonly BookStorage.Factory _storageFactory;

		public BookCollection(string path, Book.Factory bookFactory, BookStorage.Factory storageFactory)
		{
			_path = path;
			_bookFactory = bookFactory;
			_storageFactory = storageFactory;
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