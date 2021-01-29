using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using System.Xml;
using Bloom.Book;
using Bloom.Collection;
//using Bloom.SendReceive;
using Bloom.ToPalaso;
using Bloom.ToPalaso.Experimental;
using DesktopAnalytics;
using SIL.IO;
using SIL.Progress;
using SIL.Reporting;
using SIL.Windows.Forms;
using SIL.Xml;
using SIL.Windows.Forms.FileSystem;

namespace Bloom.CollectionTab
{
	public class LibraryModel
	{
		private readonly BookSelection _bookSelection;
		private readonly string _pathToLibrary;
		private readonly CollectionSettings _collectionSettings;
		//private readonly SendReceiver _sendReceiver;
		private readonly SourceCollectionsList _sourceCollectionsList;
		private readonly BookCollection.Factory _bookCollectionFactory;
		private readonly EditBookCommand _editBookCommand;
		private readonly BookServer _bookServer;
		private readonly CurrentEditableCollectionSelection _currentEditableCollectionSelection;
		private List<BookCollection> _bookCollections;
		private readonly BookThumbNailer _thumbNailer;

		public LibraryModel(string pathToLibrary, CollectionSettings collectionSettings,
			//SendReceiver sendReceiver,
			BookSelection bookSelection,
			SourceCollectionsList sourceCollectionsList,
			BookCollection.Factory bookCollectionFactory,
			EditBookCommand editBookCommand,
			CreateFromSourceBookCommand createFromSourceBookCommand,
			BookServer bookServer,
			CurrentEditableCollectionSelection currentEditableCollectionSelection,
			BookThumbNailer thumbNailer)
		{
			_bookSelection = bookSelection;
			_pathToLibrary = pathToLibrary;
			_collectionSettings = collectionSettings;
			//_sendReceiver = sendReceiver;
			_sourceCollectionsList = sourceCollectionsList;
			_bookCollectionFactory = bookCollectionFactory;
			_editBookCommand = editBookCommand;
			_bookServer = bookServer;
			_currentEditableCollectionSelection = currentEditableCollectionSelection;
			_thumbNailer = thumbNailer;

			createFromSourceBookCommand.Subscribe(CreateFromSourceBook);
		}


		public bool CanDeleteSelection
		{
			get { return _bookSelection.CurrentSelection != null && _collectionSettings.AllowDeleteBooks && _bookSelection.CurrentSelection.CanDelete; }

		}

		public bool CanExportSelection
		{
			get { return _bookSelection.CurrentSelection != null && _bookSelection.CurrentSelection.CanExport; }
		}

		public bool CanUpdateSelection
		{
			get { return _bookSelection.CurrentSelection != null && _bookSelection.CurrentSelection.CanUpdate; }

		}

		internal CollectionSettings CollectionSettings
		{
			get { return _collectionSettings; }
		}

		public string LanguageName
		{
			get { return _collectionSettings.Language1.Name; }	// collection tab still uses collection language settings
		}

		public List<BookCollection> GetBookCollections()
		{
			if(_bookCollections == null)
			{
				_bookCollections = new List<BookCollection>(GetBookCollectionsOnce());

				//we want the templates to be second (after the vernacular collection) regardless of alphabetical sorting
				var templates = _bookCollections.First(c => c.Name == "Templates");
				_bookCollections.Remove(templates);
				_bookCollections.Insert(1,templates);
			}
			return _bookCollections;
		}

		public void ReloadCollections()
		{
			_bookCollections = null;
			GetBookCollections();
		}

		/// <summary>
		/// Titles of all the books in the vernacular collection.
		/// </summary>
		internal IEnumerable<string> BookTitles
		{
			get { return TheOneEditableCollection.GetBookInfos().Select(book => book.Title); }
		}

		internal BookCollection TheOneEditableCollection
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
			var editableCollection = _bookCollectionFactory(_pathToLibrary, BookCollection.CollectionType.TheOneEditableCollection);
			_currentEditableCollectionSelection.SelectCollection(editableCollection);
			yield return editableCollection;

			foreach (var bookCollection in _sourceCollectionsList.GetSourceCollectionsFolders())
				yield return _bookCollectionFactory(bookCollection, BookCollection.CollectionType.SourceCollection);
		}


		public  void SelectBook(Book.Book book)
		{
			 _bookSelection.SelectBook(book);
		}

		public bool DeleteBook(Book.Book book)//, BookCollection collection)
		{
			Debug.Assert(book == _bookSelection.CurrentSelection);

			if (_bookSelection.CurrentSelection != null && _bookSelection.CurrentSelection.CanDelete)
			{
				var title = _bookSelection.CurrentSelection.TitleBestForUserDisplay;
				var confirmRecycleDescription = L10NSharp.LocalizationManager.GetString("CollectionTab.ConfirmRecycleDescription", "The book '{0}'");
				if (ConfirmRecycleDialog.JustConfirm(string.Format(confirmRecycleDescription, title), false, "Palaso"))
				{
					TheOneEditableCollection.DeleteBook(book.BookInfo);
					_bookSelection.SelectBook(null);
					#if Chorus
					_sendReceiver.CheckInNow(string.Format("Deleted '{0}'", title));
					#endif
					return true;
				}
			}
			return false;
		}

		public void DoubleClickedBook()
		{
			if(_bookSelection.CurrentSelection.IsEditable && ! _bookSelection.CurrentSelection.HasFatalError)
				_editBookCommand.Raise(_bookSelection.CurrentSelection);
		}

		public void OpenFolderOnDisk()
		{
			try
			{
				PathUtilities.SelectFileInExplorer(_bookSelection.CurrentSelection.FolderPath);
			}
			catch (System.Runtime.InteropServices.COMException e)
			{
				SIL.Reporting.ErrorReport.NotifyUserOfProblem(e,
					"Bloom had a problem asking your operating system to show that folder. Sorry!");
			}
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
			var pathToXnDesignXslt = FileLocationUtilities.GetFileDistributedWithApplication("xslts", "BloomXhtmlToDataForMergingIntoInDesign.xsl");

#if DEBUG
			 _bookSelection.CurrentSelection.OurHtmlDom.RawDom.Save(path.Replace(".xml",".xhtml"));
#endif

			var dom = _bookSelection.CurrentSelection.OurHtmlDom.ApplyXSLT(pathToXnDesignXslt);

			using (var writer = XmlWriter.Create(path, CanonicalXmlSettings.CreateXmlWriterSettings()))
			{
				dom.Save(writer);
			}
		}

		private bool bookHasClass(string className)
		{
			return _bookSelection.CurrentSelection?.OurHtmlDom.Body.GetAttribute("class").Contains(className) ?? false;
		}

		public bool IsBookLeveled => bookHasClass("leveled-reader");
		public bool IsBookDecodable => bookHasClass("decodable-reader");

		public void SetIsBookLeveled(bool leveled)
		{
			if (leveled && IsBookDecodable)
				SetIsBookDecodable(false);
			SetBookHasClass(leveled, "leveled-reader");
		}
		public void SetIsBookDecodable(bool decodable)
		{
			if (decodable && IsBookLeveled)
				SetIsBookLeveled(false);
			SetBookHasClass(decodable, "decodable-reader");
		}

		private void SetBookHasClass(bool shouldHaveClass, string className)
		{
			var body = _bookSelection.CurrentSelection?.OurHtmlDom.Body;
			if (body == null)
				return;
			var classVal = body.GetAttribute("class");
			if (shouldHaveClass && !classVal.Contains(className))
			{
				body.SetAttribute("class", (classVal + " " + className).Trim());
				_bookSelection.CurrentSelection.Save();
			} else if (!shouldHaveClass && classVal.Contains(className))
			{
				body.SetAttribute("class", classVal.Replace(className, "").Trim());
				_bookSelection.CurrentSelection.Save();
			}
		}

		/// <summary>
		/// All we do at this point is make a file with a ".doc" extension and open it.
		/// </summary>
		/// <remarks>
		/// The .doc extension allows the operating system to recognize which program
		/// should open the file, and the program (whether Microsoft Word or LibreOffice
		/// or OpenOffice) seems to handle HTML content just fine.
		/// </remarks>
		public void ExportDocFormat(string path)
		{
			var sourcePath = _bookSelection.CurrentSelection.GetPathHtmlFile();
			if (RobustFile.Exists(path))
			{
				RobustFile.Delete(path);
			}
			// Linux (Trusty) LibreOffice requires slightly different metadata at the beginning
			// of the file in order to recognize it as HTML.  Otherwise it opens the file as raw
			// HTML (See https://silbloom.myjetbrains.com/youtrack/issue/BL-2276 if you don't
			// believe me.)  I don't know any perfect way to add this information to the file,
			// but a simple string replace should be safe.  This change works okay for both
			// Windows and Linux and for all three programs (Word, OpenOffice and Libre Office).
			var content = RobustFile.ReadAllText(sourcePath);
			var fixedContent = content.Replace("<meta charset=\"UTF-8\">",
				"<meta http-equiv=\"content-type\" content=\"text/html; charset=utf-8\">");
			var xmlDoc = RepairWordVisibility(fixedContent);
			XmlHtmlConverter.SaveDOMAsHtml5(xmlDoc, path); // writes file and returns path
		}

		// BL-5998 Apparently Word doesn't read our CSS rules for bloom-visibility correctly.
		// So we're forced to control visibility more directly with inline styles.
		private static XmlDocument RepairWordVisibility(string content)
		{
			var xmlDoc = XmlHtmlConverter.GetXmlDomFromHtml(content);
			var dom = new HtmlDom(xmlDoc);
			var bloomEditableDivs = dom.RawDom.SafeSelectNodes("//div[contains(@class, 'bloom-editable')]");
			foreach (XmlElement editableDiv in bloomEditableDivs)
			{
				HtmlDom.SetInlineStyle(editableDiv,
					HtmlDom.HasClass(editableDiv, "bloom-visibility-code-on") ? "display: block;" : "display: none;");
			}

			return dom.RawDom;
		}

		public void UpdateThumbnailAsync(Book.Book book, HtmlThumbNailer.ThumbnailOptions thumbnailOptions, Action<Book.BookInfo, Image> callback, Action<Book.BookInfo, Exception> errorCallback)
		{
			if (!(book is ErrorBook))
			{
				_thumbNailer.RebuildThumbNailAsync(book, thumbnailOptions, callback, errorCallback);
			}

		}

		internal (string dirName, string dirPrefix) GetDirNameAndPrefixForCollectionBloomPack()
		{
			var dir = TheOneEditableCollection.PathToDirectory;
			return (dir, "");
		}

		public void MakeBloomPack(string path, bool forReaderTools = false)
		{
			var (dirName, dirPrefix) = GetDirNameAndPrefixForCollectionBloomPack();
			var rootName = Path.GetFileName(dirName);
			if (rootName == null) return;
			Logger.WriteEvent($"Making BloomPack at {path} forReaderTools={forReaderTools}");
			MakeBloomPackWithUI(path, dirName, dirPrefix, forReaderTools, isCollection: true);
		}

		internal (string dirName, string dirPrefix) GetDirNameAndPrefixForSingleBookBloomPack(string inputBookFolder)
		{
			var rootName = Path.GetFileName(inputBookFolder);
			if (rootName != null)
				rootName += Path.DirectorySeparatorChar;

			return (inputBookFolder, rootName);
		}

		public void MakeSingleBookBloomPack(string path, string inputBookFolder)
		{
			var (dirName, dirPrefix) = GetDirNameAndPrefixForSingleBookBloomPack(inputBookFolder);
			if (dirPrefix == null) return;
			Logger.WriteEvent($"Making single book BloomPack at {path} bookFolderPath={inputBookFolder}");
			MakeBloomPackWithUI(path, dirName, dirPrefix, false, isCollection: false);
		}

		private void MakeBloomPackWithUI(string path, string dir, string dirNamePrefix, bool forReaderTools, bool isCollection)
		{
			try
			{
				if (RobustFile.Exists(path))
				{
					// UI already got permission for this
					RobustFile.Delete(path);
				}
				using (var pleaseWait = new SimpleMessageDialog("Creating BloomPack...", "Bloom"))
				{
					try
					{
						pleaseWait.Show();
						pleaseWait.BringToFront();
						Application.DoEvents(); // actually show it
						Cursor.Current = Cursors.WaitCursor;

						Logger.WriteEvent("BloomPack path will be " + path + ", made from " + dir + " with rootName " + Path.GetFileName(dir));
						MakeBloomPackInternal(path, dir, dirNamePrefix, forReaderTools, isCollection);

						// show it
						Logger.WriteEvent("Showing BloomPack on disk");
						PathUtilities.SelectFileInExplorer(path);
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
				ErrorReport.NotifyUserOfProblem(e, "Could not make the BloomPack at " + path);
			}
		}

		/// <summary>
		/// Makes a BloomPack of the specified dir.
		/// </summary>
		/// <param name="path">The path to write to. Precondition: Must not exist.</param>
		internal void MakeBloomPackInternal(string path, string dir, string dirNamePrefix, bool forReaderTools, bool isCollection)
		{
			var excludeAudio = true; // don't want audio in bloompack
			if (isCollection)
			{
				BookCompressor.CompressCollectionDirectory(path, dir, dirNamePrefix, forReaderTools, excludeAudio);
			}
			else
			{
				BookCompressor.CompressBookDirectory(path, dir, dirNamePrefix, forReaderTools, excludeAudio);
			}
		}

		public string GetSuggestedBloomPackPath()
		{
			return TheOneEditableCollection.Name+".BloomPack";
		}

		public void DoUpdatesOfAllBooks()
		{
			using (var dlg = new ProgressDialogBackground())
			{
				dlg.ShowAndDoWork((progress, args) => DoUpdatesOfAllBooks(progress));
			}
		}

		public void DoUpdatesOfAllBooks(IProgress progress)
		{
			int i = 0;
			foreach (var bookInfo in TheOneEditableCollection.GetBookInfos())
			{
				i++;
				var book = _bookServer.GetBookFromBookInfo(bookInfo);
				//gets overwritten: progress.WriteStatus(book.TitleBestForUserDisplay);
				progress.WriteMessage("Processing " + book.TitleBestForUserDisplay+ " " + i + "/" + TheOneEditableCollection.GetBookInfos().Count());
				book.BringBookUpToDate(progress);
			}
		}

		public void DoChecksOfAllBooks()
		{
			using (var dlg = new ProgressDialogBackground())
			{
				dlg.ShowAndDoWork((progress, args) => DoChecksOfAllBooksBackgroundWork(dlg,null));
				if (dlg.Progress.ErrorEncountered || dlg.Progress.WarningsEncountered)
				{
					MessageBox.Show("Bloom will now open a list of problems it found.");
					var path = Path.GetTempFileName() + ".txt";
					RobustFile.WriteAllText(path, dlg.ProgressString.Text);
					PathUtilities.OpenFileInApplication(path);
				}
				else
				{
					MessageBox.Show("Bloom didn't find any problems.");
				}
			}
		}

		public void AttemptMissingImageReplacements(string pathToFolderOfReplacementImages=null)
		{
			using (var dlg = new ProgressDialogBackground())
			{
				dlg.ShowAndDoWork((progress, args) => DoChecksOfAllBooksBackgroundWork(dlg, pathToFolderOfReplacementImages));
				if (dlg.Progress.ErrorEncountered || dlg.Progress.WarningsEncountered)
				{
					MessageBox.Show("There were some problems. Bloom will now open a log of the attempt to replace missing images.");
				}
				else
				{
					MessageBox.Show("There are no more missing images. Bloom will now open a log of what it did.");
				}

				var path = Path.GetTempFileName() + ".txt";
				RobustFile.WriteAllText(path, dlg.ProgressString.Text);
				try
				{
					PathUtilities.OpenFileInApplication(path);
				}
				catch (System.OutOfMemoryException)
				{
					// This has happened at least once.  See https://silbloom.myjetbrains.com/youtrack/issue/BL-3431.
					MessageBox.Show("Bloom ran out of memory trying to open the log.  You should quit and restart the program.  (Your books should all be okay.)");
				}
			}

		}


		public void DoChecksOfAllBooksBackgroundWork(ProgressDialogBackground dialog, string pathToFolderOfReplacementImages)
		{
			var bookInfos = TheOneEditableCollection.GetBookInfos();
			var count = bookInfos.Count();
			if (count == 0)
				return;

			foreach (var bookInfo in bookInfos)
			{
				//not allowed in this thread: dialog.ProgressBar.Value++;
				dialog.Progress.ProgressIndicator.PercentCompleted += 100/count;

				var book = _bookServer.GetBookFromBookInfo(bookInfo);

				dialog.Progress.WriteMessage("Checking " + book.TitleBestForUserDisplay);
				book.CheckBook(dialog.Progress, pathToFolderOfReplacementImages);
				dialog.ProgressString.WriteMessage("");
			}
			dialog.Progress.ProgressIndicator.PercentCompleted = 100;
		}




		private void CreateFromSourceBook(Book.Book sourceBook)
		{
			try
			{
				var newBook = _bookServer.CreateFromSourceBook(sourceBook, TheOneEditableCollection.PathToDirectory);
				if (newBook == null)
					return; //This can happen if there is a configuration dialog and the user clicks Cancel

				TheOneEditableCollection.AddBookInfo(newBook.BookInfo);

				if (_bookSelection != null)
				{
					_bookSelection.SelectBook(newBook, aboutToEdit: true);
				}
				//enhance: would be nice to know if this is a new shell
				if (sourceBook.IsShellOrTemplate)
				{
					Analytics.Track("Create Book",
						new Dictionary<string, string>() {
							{ "Category", sourceBook.CategoryForUsageReporting},
							{ "BookId", newBook.ID},
							{ "Country", _collectionSettings.Country}
						});
				}
				_editBookCommand.Raise(newBook);
			}
			catch (Exception e)
			{
				SIL.Reporting.ErrorReport.NotifyUserOfProblem(e,
					"Bloom ran into an error while creating that book. (Sorry!)");
			}

		}

		public Book.Book GetBookFromBookInfo(BookInfo bookInfo, bool forSelectedBook = false)
		{
			return _bookServer.GetBookFromBookInfo(bookInfo, forSelectedBook);
		}

		/// <summary>
		/// Zip up the book folder, excluding .pdf, .bloombookorder, .map, .bloompack, .db files.
		/// The resulting file will have a .bloom extension.
		/// </summary>
		/// <param name="srcFolderName"></param>
		/// <param name="destFileName"></param>
		internal void SaveAsBloomFile(string srcFolderName, string destFileName)
		{
			var excludedExtensions = new[] { ".pdf", ".bloombookorder", ".map", ".bloompack", ".db" };

			Logger.WriteEvent("Zipping up {0} ...", destFileName);
			var zipFile = new BloomZipFile(destFileName);
			zipFile.AddDirectoryContents(srcFolderName, excludedExtensions);
			Logger.WriteEvent("Saving {0} ...", destFileName);
			zipFile.Save();
			Logger.WriteEvent("Finished writing .bloom file.");
		}
	}
}
