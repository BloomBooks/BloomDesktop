using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
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
		private readonly LibrarySettings _librarySettings;
		private readonly StoreCollectionList _storeCollectionList;
		private readonly BookCollection.Factory _bookCollectionFactory;
		private readonly EditBookCommand _editBookCommand;
		private IEnumerable<BookCollection> _bookCollections;

		public LibraryModel(string pathToLibrary, LibrarySettings librarySettings,
			BookSelection bookSelection,
			StoreCollectionList storeCollectionList,
			BookCollection.Factory bookCollectionFactory,
			EditBookCommand editBookCommand)
		{
			_bookSelection = bookSelection;
			_pathToLibrary = pathToLibrary;
			_librarySettings = librarySettings;
			_storeCollectionList = storeCollectionList;
			_bookCollectionFactory = bookCollectionFactory;
			_editBookCommand = editBookCommand;
		}

		public bool CanDeleteSelection
		{
			get { return _bookSelection.CurrentSelection != null && _bookSelection.CurrentSelection.CanDelete; }

		}
		public bool CanUpdateSelection
		{
			get { return _bookSelection.CurrentSelection != null && _bookSelection.CurrentSelection.CanUpdate; }

		}

		public string LanguageName
		{
			get { return _librarySettings.VernacularLanguageName; }
		}

		public IEnumerable<BookCollection> GetBookCollections()
		{
			if(_bookCollections ==null)
				_bookCollections = GetBookCollectionsOnce();
			return _bookCollections;
		}

		private IEnumerable<BookCollection> GetBookCollectionsOnce()
		{
			yield return _bookCollectionFactory(_pathToLibrary, BookCollection.CollectionType.TheOneEditableCollection);

			foreach (var bookCollection in _storeCollectionList.GetStoreCollections())
				yield return bookCollection;
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

		public void OpenFolderOnDisk()
		{
			Process.Start(_bookSelection.CurrentSelection.FolderPath);
		}

		public void UpdateFrontMatter()
		{
			var b = _bookSelection.CurrentSelection;
			_bookSelection.SelectBook(null);
			b.UpdateXMatter();
			_bookSelection.SelectBook(b);
		}

		public void UpdateThumbnailAsync(Action<Book.Book,Image> callback)
		{
			_bookSelection.CurrentSelection.RebuildThumbNailAsync(callback);
		}
	}
}
