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
using Bloom.Collection;
using Ionic.Zip;
using Palaso.UI.WindowsForms.FileSystem;

namespace Bloom.Library
{
	public class LibraryModel
	{
		private readonly BookSelection _bookSelection;
		private readonly string _pathToLibrary;
		private readonly CollectionSettings _collectionSettings;
		private readonly SourceCollectionsList _sourceCollectionsList;
		private readonly BookCollection.Factory _bookCollectionFactory;
		private readonly EditBookCommand _editBookCommand;
		private List<BookCollection> _bookCollections;

		public LibraryModel(string pathToLibrary, CollectionSettings collectionSettings,
			BookSelection bookSelection,
			SourceCollectionsList sourceCollectionsList,
			BookCollection.Factory bookCollectionFactory,
			EditBookCommand editBookCommand)
		{
			_bookSelection = bookSelection;
			_pathToLibrary = pathToLibrary;
			_collectionSettings = collectionSettings;
			_sourceCollectionsList = sourceCollectionsList;
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
			get { return _collectionSettings.VernacularLanguageName; }
		}

		public List<BookCollection> GetBookCollections()
		{
			if(_bookCollections ==null)
			{
				_bookCollections = new List<BookCollection>(GetBookCollectionsOnce());

				//we want the templates to be second (after the vernacular collection) regardless of alphabetical sorting
				var templates = _bookCollections.First(c => c.Name.ToLower() == "templates");
				_bookCollections.Remove(templates);
				_bookCollections.Insert(1,templates);
			}
			return _bookCollections;
		}

		private BookCollection TheOneEditableCollection
		{
			get { return GetBookCollections().First(c => c.Type == BookCollection.CollectionType.TheOneEditableCollection); }
		}

		public string VernacularLibraryNamePhrase
		{
			get { return _collectionSettings.VernacularCollectionNamePhrase; }
		}

		public bool IsShellProject
		{
			get { return _collectionSettings.IsSourceCollection; }
		}

		private IEnumerable<BookCollection> GetBookCollectionsOnce()
		{
			yield return _bookCollectionFactory(_pathToLibrary, BookCollection.CollectionType.TheOneEditableCollection);

			foreach (var bookCollection in _sourceCollectionsList.GetStoreCollections())
				yield return bookCollection;
		}


		public  void SelectBook(Book.Book book)
		{
			 _bookSelection.SelectBook(book);
		}

		public void DeleteBook(Book.Book book)//, BookCollection collection)
		{
			Debug.Assert(book == _bookSelection.CurrentSelection);

			if (_bookSelection.CurrentSelection != null && _bookSelection.CurrentSelection.CanDelete)
			{
				if(ConfirmRecycleDialog.JustConfirm(string.Format("The book '{0}'",_bookSelection.CurrentSelection.Title )))
				{
					TheOneEditableCollection.DeleteBook(book);
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

		public void MakeBloomPack(string path)
		{
			try
			{
				if(File.Exists(path))
				{
					//UI already go permission for this
					File.Delete(path);
				}

				using (var pleaseWait = new SimpleMessageDialog("Creating BloomPack..."))
				{
					try
					{
						pleaseWait.Show();
						pleaseWait.BringToFront();
						Application.DoEvents();//actually show it
						Cursor.Current = Cursors.WaitCursor;
						using (var zip = new ZipFile())
						{
							string dir = TheOneEditableCollection.PathToDirectory;
							//nb: without this second argument, we don't get the outer directory included, and we need that for the name of the collection
							zip.AddDirectory(dir, System.IO.Path.GetFileName(dir));
							zip.Save(path);
						}
						//show it
						Process.Start(Path.GetDirectoryName(path));
					}
					finally
					{
						Cursor.Current = Cursors.Default;
						pleaseWait.Close();
					}
				}
			}
			catch (Exception e)
			{
				Palaso.Reporting.ErrorReport.NotifyUserOfProblem(e, "Could not make the BloomPack");
			}
		}

		public string GetSuggestedBloomPackPath()
		{
			return TheOneEditableCollection.Name+".BloomPack";
		}
	}
}
