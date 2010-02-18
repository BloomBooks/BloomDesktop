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
		private readonly TemplateCollectionList _templateCollectionList;
		private readonly BookCollection.Factory _bookCollectionFactory;

		public LibraryModel(BookSelection bookSelection, string pathToProject, TemplateCollectionList templateCollectionList,
			BookCollection.Factory bookFactory)
		{
			_bookSelection = bookSelection;
			_pathToProject = pathToProject;
			_templateCollectionList = templateCollectionList;
			_bookCollectionFactory = bookFactory;
		}

		public IEnumerable<BookCollection> GetBookCollections()
		{
			yield return _bookCollectionFactory(_pathToProject);

			foreach (var root in _templateCollectionList.ReposistoryFolders)
			{
				foreach (var dir in Directory.GetDirectories(root))
				{
					yield return _bookCollectionFactory(dir);
				}
			}
		}

		public bool SelectBook(Book book)
		{
			return _bookSelection.SelectBook(book);
		}
	}
}
