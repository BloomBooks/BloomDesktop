using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using System.Xml;
using Bloom.Book;
using Bloom.Collection;
//using Bloom.SendReceive;
using Bloom.ToPalaso;
using Bloom.ToPalaso.Experimental;
using DesktopAnalytics;
using ICSharpCode.SharpZipLib.Core;
using ICSharpCode.SharpZipLib.Zip;
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

		public string LanguageName
		{
			get { return _collectionSettings.Language1Name; }
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
			var editableCllection = _bookCollectionFactory(_pathToLibrary, BookCollection.CollectionType.TheOneEditableCollection);
			_currentEditableCollectionSelection.SelectCollection(editableCllection);
			yield return editableCllection;

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
			PathUtilities.SelectFileInExplorer(_bookSelection.CurrentSelection.FolderPath);
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
			string sourcePath = _bookSelection.CurrentSelection.GetPathHtmlFile();
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
			string content = RobustFile.ReadAllText(sourcePath);
			string fixedContent = content.Replace("<meta charset=\"UTF-8\">", "<meta http-equiv=\"content-type\" content=\"text/html; charset=utf-8\">");
			RobustFile.WriteAllText(path, fixedContent);
		}

		public void UpdateThumbnailAsync(Book.Book book, HtmlThumbNailer.ThumbnailOptions thumbnailOptions, Action<Book.BookInfo, Image> callback, Action<Book.BookInfo, Exception> errorCallback)
		{
			if (!(book is ErrorBook))
			{
				_thumbNailer.RebuildThumbNailAsync(book, thumbnailOptions, callback, errorCallback);
			}
			
		}

		public void MakeBloomPack(string path, bool forReaderTools = false)
		{
			try
			{
				if(RobustFile.Exists(path))
				{
					// UI already got permission for this
					RobustFile.Delete(path);
				}

				Logger.WriteEvent("Making BloomPack at "+path+" forReaderTools="+forReaderTools.ToString());

				using (var pleaseWait = new SimpleMessageDialog("Creating BloomPack...", "Bloom"))
				{
					try
					{
						pleaseWait.Show();
						pleaseWait.BringToFront();
						Application.DoEvents(); // actually show it
						Cursor.Current = Cursors.WaitCursor;

						var dir = TheOneEditableCollection.PathToDirectory;

						var rootName = Path.GetFileName(dir);
						if (rootName == null) return;

						var dirNameOffest = dir.Length - rootName.Length;

						Logger.WriteEvent("BloomPack path will be " + path + ", made from " + dir + " with rootName " + rootName);
						using (var fsOut = RobustFile.Create(path))
						{
							using (ZipOutputStream zipStream = new ZipOutputStream(fsOut))
							{
								zipStream.SetLevel(9);

								CompressDirectory(dir, zipStream, dirNameOffest, forReaderTools);

								zipStream.IsStreamOwner = true; // makes the Close() also close the underlying stream
								zipStream.Close();
							}
						}

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
				ErrorReport.NotifyUserOfProblem(e, "Could not make the BloomPack");
			}
		}

		// these files (if encountered) won't be compressed into a BloomPack
		private static readonly string[] excludedFileExtensionsLowerCase = { ".db", ".pdf", ".bloompack", ".bak", ".userprefs" };

		/// <summary>
		/// Adds a directory, along with all files and subdirectories, to the ZipStream.
		/// </summary>
		/// <param name="directoryPath">The directory to add recursively</param>
		/// <param name="zipStream">The ZipStream to which the files and directories will be added</param>
		/// <param name="dirNameOffest">This number of characters will be removed from the full directory or file name
		/// before creating the zip entry name</param>
		/// <param name="forReaderTools">If True, then some pre-processing will be done to the contents of decodable
		/// and leveled readers before they are added to the ZipStream</param>
		/// <remarks>Protected for testing purposes</remarks>
		protected static void CompressDirectory(string directoryPath, ZipOutputStream zipStream, int dirNameOffest,
			bool forReaderTools)
		{
			if (Path.GetFileName(directoryPath).ToLowerInvariant() == "audio")
				return; // don't want audio in bloompack. todo: test
			var files = Directory.GetFiles(directoryPath);
			var bookFile = BookStorage.FindBookHtmlInFolder(directoryPath);

			foreach (var filePath in files)
			{
				if (excludedFileExtensionsLowerCase.Contains(Path.GetExtension(filePath.ToLowerInvariant())))
					continue; // BL-2246: skip putting this one into the BloomPack

				FileInfo fi = new FileInfo(filePath);

				var entryName = filePath.Substring(dirNameOffest);  // Makes the name in zip based on the folder
				entryName = ZipEntry.CleanName(entryName);          // Removes drive from name and fixes slash direction
				ZipEntry newEntry = new ZipEntry(entryName) { DateTime = fi.LastWriteTime };
				newEntry.IsUnicodeText = true; // encode filename and comment in UTF8
				byte[] modifiedContent = {};

				// if this is a ReaderTools book, call GetBookReplacedWithTemplate() to get the contents
				if (forReaderTools && (bookFile == filePath))
				{
					modifiedContent = Encoding.UTF8.GetBytes(GetBookReplacedWithTemplate(filePath));
					newEntry.Size = modifiedContent.Length;
				}
				else if (forReaderTools && (Path.GetFileName(filePath)=="meta.json"))
				{
					modifiedContent = Encoding.UTF8.GetBytes(GetMetaJsonModfiedForTemplate(filePath));
					newEntry.Size = modifiedContent.Length;
				}
				else
				{
					newEntry.Size = fi.Length;
				}

				zipStream.PutNextEntry(newEntry);

				if (modifiedContent.Length > 0)
				{
					using (var memStream = new MemoryStream(modifiedContent))
					{
						StreamUtils.Copy(memStream, zipStream, new byte[modifiedContent.Length]);
					}
				}
				else
				{
					// Zip the file in buffered chunks
					byte[] buffer = new byte[4096];
					using (var streamReader = RobustFile.OpenRead(filePath))
					{
						StreamUtils.Copy(streamReader, zipStream, buffer);
					}
				}

				zipStream.CloseEntry();
			}

			var folders = Directory.GetDirectories(directoryPath);

			foreach (var folder in folders)
			{
				var dirName = Path.GetFileName(folder);
				if ((dirName == null) || (dirName.ToLowerInvariant() == "sample texts"))
					continue; // Don't want to bundle these up

				CompressDirectory(folder, zipStream, dirNameOffest, forReaderTools);
			}
		}

		private static string GetMetaJsonModfiedForTemplate(string path)
		{
			var meta = BookMetaData.FromString(RobustFile.ReadAllText(path));
			meta.IsSuitableForMakingShells = true;
			return meta.Json;
		}

		/// <summary>
		/// Does some pre-processing on reader files
		/// </summary>
		/// <param name="bookPath"></param>
		private static string GetBookReplacedWithTemplate(string bookPath)
		{		
			//TODO: the following, which is the original code before late in 3.6, just modified the tags in the HTML
			//Whereas currently, we use the meta.json as the authoritative source.
			//TODO Should we just get rid of these tags in the HTML? Can they be accessed from javascript? If so,
			//then they will be needed eventually (as we involve c# less in the UI)
			var text = RobustFile.ReadAllText(bookPath, Encoding.UTF8);
			// Note that we're getting rid of preceding newline but not following one. Hopefully we cleanly remove a whole line.
			// I'm not sure the </meta> ever occurs in html files, but just in case we'll match if present.
			var regex = new Regex("\\s*<meta\\s+name=(['\\\"])lockedDownAsShell\\1 content=(['\\\"])true\\2>(</meta>)? *");
			var match = regex.Match(text);
			if (match.Success)
				text = text.Substring(0, match.Index) + text.Substring(match.Index + match.Length);

			// BL-2476: Readers made from BloomPacks should have the formatting dialog disabled
			regex = new Regex("\\s*<meta\\s+name=(['\\\"])pageTemplateSource\\1 content=(['\\\"])(Leveled|Decodable) Reader\\2>(</meta>)? *");
			match = regex.Match(text);
			if (match.Success)
			{
				// has the lockFormatting meta tag been added already?
				var regexSuppress = new Regex("\\s*<meta\\s+name=(['\\\"])lockFormatting\\1 content=(['\\\"])(.*)\\2>(</meta>)? *");
				var matchSuppress = regexSuppress.Match(text);
				if (matchSuppress.Success)
				{
					// the meta tag already exists, make sure the value is "true"
					if (matchSuppress.Groups[3].Value.ToLower() != "true")
					{
						text = text.Substring(0, matchSuppress.Groups[3].Index) + "true"
							+  text.Substring(matchSuppress.Groups[3].Index + matchSuppress.Groups[3].Length);
					}
				}
				else
				{
					// the meta tag has not been added, add it now
					text = text.Insert(match.Index + match.Length,
						"\r\n    <meta name=\"lockFormatting\" content=\"true\"></meta>");
				}
			}

			return text;
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
					_bookSelection.SelectBook(newBook);
				}
				//enhance: would be nice to know if this is a new shell
				if (sourceBook.IsShellOrTemplate)
				{
					Analytics.Track("Create Book",
						new Dictionary<string, string>() {{"Category", sourceBook.CategoryForUsageReporting}});
				}
				_editBookCommand.Raise(newBook);
			}
			catch (Exception e)
			{
				SIL.Reporting.ErrorReport.NotifyUserOfProblem(e,
					"Bloom ran into an error while creating that book. (Sorry!)");
			}

		}

		public Book.Book GetBookFromBookInfo(BookInfo bookInfo)
		{
			return _bookServer.GetBookFromBookInfo(bookInfo);
		}

	}
}
