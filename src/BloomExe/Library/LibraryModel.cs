using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using Bloom.Book;
using Palaso.UI.WindowsForms.FileSystem;

namespace Bloom.Library
{
	public class LibraryModel
	{
		private readonly BookSelection _bookSelection;
		private readonly string _pathToLibrary;
		private readonly TemplateCollectionList _templateCollectionList;
		private readonly BookCollection.Factory _bookCollectionFactory;
		private readonly EditBookCommand _editBookCommand;

		public LibraryModel(string pathToLibrary, BookSelection bookSelection,
			TemplateCollectionList templateCollectionList,
			BookCollection.Factory bookFactory,
			EditBookCommand editBookCommand)
		{
			_bookSelection = bookSelection;
			_pathToLibrary = pathToLibrary;
			_templateCollectionList = templateCollectionList;
			_bookCollectionFactory = bookFactory;
			_editBookCommand = editBookCommand;
		}

		public bool CanDeleteSelection
		{
			get { return _bookSelection.CurrentSelection != null && _bookSelection.CurrentSelection.CanDelete; }

		}

		public IEnumerable<BookCollection> GetBookCollections()
		{
			yield return _bookCollectionFactory(_pathToLibrary, BookCollection.CollectionType.TheOneEditableCollection);

			foreach (var root in _templateCollectionList.RepositoryFolders)
			{
				if (!Directory.Exists(root))
					continue;

				foreach (var dir in Directory.GetDirectories(root))
				{
					if (Path.GetFileName(dir).StartsWith("."))//skip thinks like .idea, .hg, etc.
						continue;
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

		public  void SelectBook(Book.Book book)
		{
			 _bookSelection.SelectBook(book);
		}

		public void DeleteBook(Book.Book book, BookCollection collection)
		{
			Debug.Assert(book == _bookSelection.CurrentSelection);

			if (_bookSelection.CurrentSelection != null && _bookSelection.CurrentSelection.CanDelete)
			{
				if(ConfirmRecycleDialog.JustConfirm(string.Format("The book '{0}'",_bookSelection.CurrentSelection.Title )))
				{
					collection.DeleteBook(book);
					_bookSelection.SelectBook(null);
				}
			}
		}

		public void DoubleClickedBook()
		{
			if(_bookSelection.CurrentSelection.IsInEditableLibrary && ! _bookSelection.CurrentSelection.HasFatalError)
				_editBookCommand.Raise(_bookSelection.CurrentSelection);
		}
	}
}
