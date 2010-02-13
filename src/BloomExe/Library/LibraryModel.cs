using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Bloom.Library
{
	public class LibraryModel
	{
		private readonly BookSelection _bookSelection;
		private readonly string _pathToProject;
		private readonly string _pathToFactoryCollections;

		public LibraryModel(BookSelection bookSelection, string pathToProject, string pathToFactoryCollections)
		{
			_bookSelection = bookSelection;
			_pathToProject = pathToProject;
			_pathToFactoryCollections = pathToFactoryCollections;
		}

		public IEnumerable<BookCollection> GetBookCollections()
		{
			yield return new BookCollection(_pathToProject);

			foreach (var dir in Directory.GetDirectories(_pathToFactoryCollections))
			{
				yield return new BookCollection(dir);
			}
		}

		public bool SelectBook(Book book)
		{
			return _bookSelection.SelectBook(book);
		}
	}
}
