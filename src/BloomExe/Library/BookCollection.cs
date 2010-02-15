using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;

namespace Bloom.Library
{
	public class BookCollection
	{
		public delegate BookCollection Factory(string path);//autofac uses this

		private readonly string _path;
		private readonly Book.Factory _bookFactory;

		public BookCollection(string path, Book.Factory bookFactory)
		{
			_path = path;
			_bookFactory = bookFactory;
		}

		public string Name
		{
			get { return Path.GetFileName(_path); }
		}

		public IEnumerable<Book> GetBooks()
		{
			foreach(string path in Directory.GetDirectories(_path))
			{
				yield return _bookFactory(path);
			}
		}
	}
}