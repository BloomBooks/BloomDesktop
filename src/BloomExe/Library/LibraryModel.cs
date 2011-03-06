using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

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

		public bool CanDeleteSelection
		{
			get { return _bookSelection.CurrentSelection != null && _bookSelection.CurrentSelection.CanDelete; }

		}

		public IEnumerable<BookCollection> GetBookCollections()
		{
			yield return _bookCollectionFactory(_pathToProject, BookCollection.CollectionType.TheOneEditableCollection);

			foreach (var root in _templateCollectionList.RepositoryFolders)
			{

				if (!Directory.Exists(root))
					continue;


				foreach (var dir in Directory.GetDirectories(root))
				{

					yield return _bookCollectionFactory(dir,BookCollection.CollectionType.TemplateCollection);
				}

				//follow shortcuts
				foreach (var shortcut in Directory.GetFiles(root,"*.lnk",SearchOption.TopDirectoryOnly))
				{
					var path = ResolveShortcut.Resolve(shortcut);
					if(Directory.Exists(path))
						yield return _bookCollectionFactory(path,BookCollection.CollectionType.TemplateCollection);
				}
			}
		}

		public  void SelectBook(Book book)
		{
			 _bookSelection.SelectBook(book);
		}

		public void DeleteBook(Book book, BookCollection collection)
		{
			Debug.Assert(book == _bookSelection.CurrentSelection);
			using(var dlg = new ConfirmDelete())
			{
				if (DialogResult.OK != dlg.ShowDialog())
					return;
			}
			if (_bookSelection.CurrentSelection != null && _bookSelection.CurrentSelection.CanDelete)
			{
				collection.DeleteBook(book);
				_bookSelection.SelectBook(null);
			}
		}
	}
}
