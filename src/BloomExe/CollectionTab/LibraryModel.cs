using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.Xml;
using Bloom.Book;
using Bloom.Collection;
using Bloom.Edit;
using Bloom.SendReceive;
using Bloom.ToPalaso;
using Bloom.ToPalaso.Experimental;
using DesktopAnalytics;
using Ionic.Zip;
using Palaso.IO;
using Palaso.Progress;
using Palaso.Reporting;
using Palaso.Xml;

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
		private readonly BookServer _bookServer;
		private readonly CurrentEditableCollectionSelection _currentEditableCollectionSelection;
		private List<BookCollection> _bookCollections;

		public LibraryModel(string pathToLibrary, CollectionSettings collectionSettings,
			SendReceiver sendReceiver,
			BookSelection bookSelection,
			SourceCollectionsList sourceCollectionsList,
			BookCollection.Factory bookCollectionFactory,
			EditBookCommand editBookCommand,
			CreateFromSourceBookCommand createFromSourceBookCommand,
			BookServer bookServer,
			CurrentEditableCollectionSelection currentEditableCollectionSelection)
		{
			_bookSelection = bookSelection;
			_pathToLibrary = pathToLibrary;
			_collectionSettings = collectionSettings;
			_sendReceiver = sendReceiver;
			_sourceCollectionsList = sourceCollectionsList;
			_bookCollectionFactory = bookCollectionFactory;
			_editBookCommand = editBookCommand;
			_bookServer = bookServer;
			_currentEditableCollectionSelection = currentEditableCollectionSelection;

			createFromSourceBookCommand.Subscribe(CreateFromSourceBook);
		}


		public bool CanDeleteSelection
		{
			get { return _bookSelection.CurrentSelection != null && _collectionSettings.AllowDeleteBooks && _bookSelection.CurrentSelection.CanDelete; }

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

		public bool ShowSourceCollections
		{
			get { return _collectionSettings.AllowNewBooks; }

		}

		private IEnumerable<BookCollection> GetBookCollectionsOnce()
		{
			var editableCllection = _bookCollectionFactory(_pathToLibrary, BookCollection.CollectionType.TheOneEditableCollection);
			_currentEditableCollectionSelection.SelectCollection(editableCllection);
			yield return editableCllection;

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
				var title = _bookSelection.CurrentSelection.TitleBestForUserDisplay;
				var confirmRecycleDescription = L10NSharp.LocalizationManager.GetString("CollectionTab.ConfirmRecycleDescription", "The book '{0}'");
				if (Bloom.ConfirmRecycleDialog.JustConfirm(string.Format(confirmRecycleDescription, title)))
				{
					TheOneEditableCollection.DeleteBook(book.BookInfo);
					_bookSelection.SelectBook(null);
					_sendReceiver.CheckInNow(string.Format("Deleted '{0}'", title));
				}
			}
		}

		public void DoubleClickedBook()
		{
			if(_bookSelection.CurrentSelection.IsEditable && ! _bookSelection.CurrentSelection.HasFatalError)
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


		public void ExportInDesignXml(string path)
		{
			var pathToXnDesignXslt = FileLocator.GetFileDistributedWithApplication("xslts", "BloomXhtmlToDataForMergingIntoInDesign.xsl");

#if DEBUG
			 _bookSelection.CurrentSelection.OurHtmlDom.RawDom.Save(path.Replace(".xml",".xhtml"));
#endif

			var dom = _bookSelection.CurrentSelection.OurHtmlDom.ApplyXSLT(pathToXnDesignXslt);

			using (var writer = XmlWriter.Create(path, CanonicalXmlSettings.CreateXmlWriterSettings()))
			{
				dom.Save(writer);
			}
		}

		public void UpdateThumbnailAsync(Book.Book book, Action<Book.BookInfo, Image> callback, Action<Book.BookInfo, Exception> errorCallback)
		{
			book.RebuildThumbNailAsync(callback,errorCallback);
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
						using (var zip = new ZipFile(Encoding.UTF8))
						{
							string dir = TheOneEditableCollection.PathToDirectory;
							//nb: without this second argument, we don't get the outer directory included, and we need that for the name of the collection
							zip.AddDirectory(dir, System.IO.Path.GetFileName(dir));
							zip.Save(path);
						}
						//show it
						Logger.WriteEvent("Showing BloomPack on disk");
						Process.Start(Path.GetDirectoryName(path));
						Analytics.Track("Create BloomPack");
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
				dlg.ShowAndDoWork((progress, args) => DoChecksAndUpdatesOfAllBooks(progress));
			}
		}

		public void DoChecksAndUpdatesOfAllBooks(IProgress progress)
		{
			int i = 0;
			foreach (var bookInfo in TheOneEditableCollection.GetBookInfos())
			{
				i++;
				var book = _bookServer.GetBookFromBookInfo(bookInfo);
				//gets overwritten: progress.WriteStatus(book.TitleBestForUserDisplay);
				progress.WriteMessage("Processing " + book.TitleBestForUserDisplay + " " + i + "/" + TheOneEditableCollection.GetBookInfos().Count());
				book.BringBookUpToDate(progress);
			}
		}

		private void CreateFromSourceBook(Book.Book sourceBook)
		{
			var newBook = _bookServer.CreateFromSourceBook(sourceBook, TheOneEditableCollection.PathToDirectory);

			TheOneEditableCollection.AddBookInfo(newBook.BookInfo);

			if (_bookSelection != null)
			{
				_bookSelection.SelectBook(newBook);
			}
			//enhance: would be nice to know if this is a new shell
			if (sourceBook.IsShellOrTemplate)
			{
				Analytics.Track("Create Book", new Dictionary<string, string>() { { "Category", sourceBook.CategoryForUsageReporting } });
			}
			_editBookCommand.Raise(newBook);
		}

		public Book.Book GetBookFromBookInfo(BookInfo bookInfo)
		{
			return _bookServer.GetBookFromBookInfo(bookInfo);
		}
	}
}
