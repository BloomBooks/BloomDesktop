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

		public LibraryModel(string pathToProject, BookSelection bookSelection,
			TemplateCollectionList templateCollectionList,
			BookCollection.Factory bookFactory)
		{
			_bookSelection = bookSelection;
			_pathToProject = pathToProject;
			_templateCollectionList = templateCollectionList;
			_bookCollectionFactory = bookFactory;
		}

		public IEnumerable<BookCollection> GetBookCollections()
		{
			yield return _bookCollectionFactory(_pathToProject, BookCollection.CollectionType.TheOneEditableCollection);

			foreach (var root in _templateCollectionList.RepositoryFolders)
			{
				foreach (var dir in Directory.GetDirectories(root))
				{
					yield return _bookCollectionFactory(dir,BookCollection.CollectionType.TemplateCollection);
				}
			}
		}

		public bool SelectBook(Book book)
		{
			return _bookSelection.SelectBook(book);
		}
	}
}
