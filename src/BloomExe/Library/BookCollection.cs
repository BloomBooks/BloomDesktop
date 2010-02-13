using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;

namespace Bloom.Library
{
	public class BookCollection
	{
		private readonly string _path;

		public BookCollection(string path)
		{
			_path = path;
		}

		public string Name
		{
			get { return Path.GetFileName(_path); }
		}

		public IEnumerable<Book> GetBooks()
		{
			foreach(string path in Directory.GetDirectories(_path))
			{
				yield return new Book(path);
			}
		}
	}
}