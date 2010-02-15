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
		private readonly BookCollection.Factory _bookCollectionFactory;

		public LibraryModel(BookSelection bookSelection, string pathToProject, string pathToFactoryCollections,
			BookCollection.Factory bookFactory)
		{
			_bookSelection = bookSelection;
			_pathToProject = pathToProject;
			_pathToFactoryCollections = pathToFactoryCollections;
			_bookCollectionFactory = bookFactory;
		}

		public IEnumerable<BookCollection> GetBookCollections()
		{
			yield return _bookCollectionFactory(_pathToProject);

			foreach (var dir in Directory.GetDirectories(_pathToFactoryCollections))
			{
				yield return _bookCollectionFactory(dir);
			}
		}

		public bool SelectBook(Book book)
		{
			return _bookSelection.SelectBook(book);
		}
	}
}
