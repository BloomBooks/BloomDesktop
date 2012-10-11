using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Bloom.Book;
using Bloom.Collection;
using Bloom.SendReceive;
using Bloom.ToPalaso;
using Bloom.ToPalaso.Experimental;
using Ionic.Zip;
using Palaso.Reporting;
using Palaso.UI.WindowsForms.FileSystem;

namespace Bloom.CollectionTab
{
	public class LibraryModel
	{
		private readonly BookSelection _bookSelection;
		private readonly string _pathToLibrary;
		private readonly CollectionSettings _collectionSettings;
		private readonly SendReceiver _sendReceiver;
		private readonly SourceCollectionsList _sourceCollectionsList;
		private readonly BookCollection.Factory _bookCollectionFactory;
		private readonly EditBookCommand _editBookCommand;
		private List<BookCollection> _bookCollections;

		public LibraryModel(string pathToLibrary, CollectionSettings collectionSettings,
			SendReceiver sendReceiver,
			BookSelection bookSelection,
			SourceCollectionsList sourceCollectionsList,
			BookCollection.Factory bookCollectionFactory,
			EditBookCommand editBookCommand)
		{
			_bookSelection = bookSelection;
			_pathToLibrary = pathToLibrary;
			_collectionSettings = collectionSettings;
			_sendReceiver = sendReceiver;
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
			get { return _collectionSettings.Language1Name; }
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

			foreach (var bookCollection in _sourceCollectionsList.GetSourceCollections())
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
				var title = _bookSelection.CurrentSelection.Title;
				if(ConfirmRecycleDialog.JustConfirm(string.Format("The book '{0}'",title )))
				{
					TheOneEditableCollection.DeleteBook(book);
					_bookSelection.SelectBook(null);
					_sendReceiver.CheckInNow(string.Format("Deleted '{0}'", title));
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

		public void BringBookUpToDate()
		{
			var b = _bookSelection.CurrentSelection;
			_bookSelection.SelectBook(null);

			using (var dlg = new ProgressDialogForeground()) //REVIEW: this foreground dialog has known problems in other contexts... it was used here because of its ability to handle exceptions well. TODO: make the background one handle exceptions well
			{
				dlg.ShowAndDoWork(progress=>b.BringBookUpToDate(progress));
			}

			_bookSelection.SelectBook(b);
		}

		public void UpdateThumbnailAsync(Action<Book.Book, Image> callback, Action<Book.Book, Exception> errorCallback)
		{
			_bookSelection.CurrentSelection.RebuildThumbNailAsync(callback,errorCallback);
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
				Logger.WriteEvent("Making BloomPack");
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
						Logger.WriteEvent("Showing BloomPack on disk");
						Process.Start(Path.GetDirectoryName(path));
						UsageReporter.SendNavigationNotice("Made BloomPack");
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

		public void DoChecksAndUpdatesOfAllBooks()
		{
			using (var dlg = new ProgressDialogBackground())
			{
				dlg.ShowAndDoWork((progress, args) =>
									  {
										  var books = TheOneEditableCollection.GetBooks();
										  int i = 0;
										  foreach (var book in books)
										  {
											  i++;
											  //gets overwritten: progress.WriteStatus(book.TitleBestForUserDisplay);
											  progress.WriteMessage("Processing "+book.TitleBestForUserDisplay +" "+i+"/"+books.Count());
											  book.BringBookUpToDate(progress);
										  }
									  }
					);
			}
		}
	}
}
