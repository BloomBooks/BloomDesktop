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
using Bloom.SendReceive;
using Bloom.ToPalaso;
using Bloom.ToPalaso.Experimental;
using DesktopAnalytics;
using ICSharpCode.SharpZipLib.Core;
using ICSharpCode.SharpZipLib.Zip;
using Palaso.IO;
using Palaso.Progress;
using Palaso.Reporting;
using Palaso.UI.WindowsForms;
using Palaso.Xml;
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
			if(_bookCollections ==null)
			{
				_bookCollections = new List<BookCollection>(GetBookCollectionsOnce());

				//we want the templates to be second (after the vernacular collection) regardless of alphabetical sorting
				var templates = _bookCollections.First(c => c.Name.ToLowerInvariant() == "templates");
				_bookCollections.Remove(templates);
				_bookCollections.Insert(1,templates);
			}
			return _bookCollections;
		}

		/// <summary>
		/// Titles of all the books in the vernacular collection.
		/// </summary>
		internal IEnumerable<string> BookTitles
		{
			get { return TheOneEditableCollection.GetBookInfos().Select(book => book.Title); }
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
					_sendReceiver.CheckInNow(string.Format("Deleted '{0}'", title));
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
			PathUtilities.OpenDirectoryInExplorer(_bookSelection.CurrentSelection.FolderPath);
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
		/// <param name="path"></param>
		public void ExportDocFormat(string path)
		{
			string sourcePath = _bookSelection.CurrentSelection.GetPathHtmlFile();
			if (File.Exists(path))
			{
				File.Delete(path);
			}
			File.Copy(sourcePath, path);
		}

		public void UpdateThumbnailAsync(Book.Book book, HtmlThumbNailer.ThumbnailOptions thumbnailOptions, Action<Book.BookInfo, Image> callback, Action<Book.BookInfo, Exception> errorCallback)
		{
			book.RebuildThumbNailAsync(thumbnailOptions, callback, errorCallback);
		}

		public void MakeBloomPack(string path, bool forReaderTools = false)
		{
			try
			{
				if(File.Exists(path))
				{
					// UI already got permission for this
					File.Delete(path);
				}

				Logger.WriteEvent("Making BloomPack");

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

						using (var fsOut = File.Create(path))
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

		/// <summary>
		/// Adds a directory, along with all files and subdirectories, to the ZipStream.
		/// </summary>
		/// <param name="directoryPath">The directory to add recursively</param>
		/// <param name="zipStream">The ZipStream to which the files and directories will be added</param>
		/// <param name="dirNameOffest">This number of characters will be removed from the full directory or file name
		/// before creating the zip entry name</param>
		/// <param name="forReaderTools">If True, then some pre-processing will be done to the contents of decodable
		/// and leveled readers before they are added to the ZipStream</param>
		private static void CompressDirectory(string directoryPath, ZipOutputStream zipStream, int dirNameOffest,
			bool forReaderTools)
		{
			var files = Directory.GetFiles(directoryPath);
			var bookFile = BookStorage.FindBookHtmlInFolder(directoryPath);

			foreach (var fileName in files)
			{
				FileInfo fi = new FileInfo(fileName);

				var entryName = fileName.Substring(dirNameOffest);  // Makes the name in zip based on the folder
				entryName = ZipEntry.CleanName(entryName);          // Removes drive from name and fixes slash direction
				ZipEntry newEntry = new ZipEntry(entryName) { DateTime = fi.LastWriteTime };
				newEntry.IsUnicodeText = true; // encode filename and comment in UTF8
				byte[] bookContent = {};

				// if this is a ReaderTools book, call GetBookReplacedWithTemplate() to get the contents
				if (forReaderTools && (bookFile == fileName))
				{
					bookContent = GetBookReplacedWithTemplate(fileName);
					newEntry.Size = bookContent.Length;
				}
				else
				{
					newEntry.Size = fi.Length;
				}

				zipStream.PutNextEntry(newEntry);

				if (bookContent.Length > 0)
				{
					using (var memStream = new MemoryStream(bookContent))
					{
						StreamUtils.Copy(memStream, zipStream, new byte[bookContent.Length]);
					}
				}
				else
				{
					// Zip the file in buffered chunks
					byte[] buffer = new byte[4096];
					using (var streamReader = File.OpenRead(fileName))
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

		/// <summary>
		/// Does some pre-processing on reader files
		/// </summary>
		/// <param name="bookFile"></param>
		/// <returns>A UTF8-encoded byte array filled with the contents of the bookFile</returns>
		private static byte[] GetBookReplacedWithTemplate(string bookFile)
		{
			var text = BloomFile.ReadAllText(bookFile, Encoding.UTF8);
			// Note that we're getting rid of preceding newline but not following one. Hopefully we cleanly remove a whole line.
			// I'm not sure the </meta> ever occurs in html files, but just in case we'll match if present.
			var regex = new Regex("\\s*<meta\\s+name=(['\\\"])lockedDownAsShell\\1 content=(['\\\"])true\\2>(</meta>)? *");
			var match = regex.Match(text);
			if (match.Success)
				text = text.Substring(0, match.Index) + text.Substring(match.Index + match.Length);

			return Encoding.UTF8.GetBytes(text);
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
				progress.WriteMessage("Processing " + book.TitleBestForUserDisplay + " " + i + "/" + TheOneEditableCollection.GetBookInfos().Count());
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
					File.WriteAllText(path, dlg.ProgressString.Text);
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
				File.WriteAllText(path, dlg.ProgressString.Text);
				PathUtilities.OpenFileInApplication(path);
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
			dialog.ProgressBar.Value++;
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
				Palaso.Reporting.ErrorReport.NotifyUserOfProblem(e,
					"Bloom ran into an error while creating that book. (Sorry!)");
			}

		}

		public Book.Book GetBookFromBookInfo(BookInfo bookInfo)
		{
			return _bookServer.GetBookFromBookInfo(bookInfo);
		}

	}
}
