using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Dynamic;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Amazon.Auth.AccessControlPolicy;
using Bloom.Api;
using Bloom.Book;
using Bloom.Collection;
using Bloom.CollectionTab;
using Bloom.MiscUI;
using Bloom.Properties;
using Bloom.Spreadsheet;
using Bloom.TeamCollection;
using Bloom.ToPalaso;
using DesktopAnalytics;
using Gecko.WebIDL;
using L10NSharp;
using Newtonsoft.Json;
using SIL.IO;
using SIL.Linq;
using SIL.PlatformUtilities;
using SIL.Reporting;

namespace Bloom.web.controllers
{

	public class CollectionApi
	{
		private readonly CollectionSettings _settings;
		private readonly LibraryModel _libraryModel;
		public const string kApiUrlPart = "collections/";
		private string _previousTargetSaveAs;
		private BookSelection _bookSelection;
		private readonly BloomWebSocketServer _webSocketServer;
		public 	 CollectionApi(CollectionSettings settings, LibraryModel libraryModel, BloomWebSocketServer webSocketServer, BookSelection bookSelection)
		{
			_settings = settings;
			_libraryModel = libraryModel;
			_webSocketServer = webSocketServer;
			_bookSelection = bookSelection;
		}

		public void RegisterWithApiHandler(BloomApiHandler apiHandler)
		{
			apiHandler.RegisterEndpointHandler(kApiUrlPart + "list", HandleListRequest, true);

			apiHandler.RegisterEndpointHandler(kApiUrlPart + "name", request =>
			{
				// always null? request.ReplyWithText(_collection.Name);
				request.ReplyWithText(_settings.CollectionName);
			}, true);

			apiHandler.RegisterEndpointHandler(kApiUrlPart + "books", HandleBooksRequest, true);
			apiHandler.RegisterEndpointHandler(kApiUrlPart + "book/thumbnail", HandleThumbnailRequest, true);

			apiHandler.RegisterEndpointHandler(kApiUrlPart + "selected-book-id", request =>
			{
				switch (request.HttpMethod)
				{
					case HttpMethods.Get:
						request.ReplyWithText("" + _libraryModel.GetSelectedBookOrNull()?.ID);
						break;
					case HttpMethods.Post:
						var book = GetBookObjectFromPost(request);
						_libraryModel.SelectBook(book);
						request.PostSucceeded();
						break;
				}
			}, true);

			apiHandler.RegisterEndpointHandler(kApiUrlPart + "bookCommand", HandleBookCommand, true);
			//apiHandler.RegisterEndpointHandler(kApiUrlPart+"duplicateBook", HandleDuplicateBook, true);
			//apiHandler.RegisterEndpointHandler(kApiUrlPart + "deleteBook", HandleDeleteBook, true);
			//apiHandler.RegisterEndpointHandler(kApiUrlPart + "openFolder", HandleOpenFolder, true);
			//apiHandler.RegisterEndpointHandler(kApiUrlPart + "makeBloomPack", HandleMakeBloompack, true);
			//apiHandler.RegisterEndpointHandler(kApiUrlPart + "exportToWord", HandleExportToWord, true);
			//apiHandler.RegisterEndpointHandler(kApiUrlPart + "exportToSpreadsheet", HandleExportToSpreadsheet, true);
			//apiHandler.RegisterEndpointHandler(kApiUrlPart + "importSpreadsheetContent", HandleImportSpreadsheetContent, true);
			//apiHandler.RegisterEndpointHandler(kApiUrlPart + "saveAsDotBloom", HandleSaveAsDotBloom, true);
		}

		private void HandleBookCommand(ApiRequest request)
		{
			var book = GetBookObjectFromPost(request);
			var command = request.RequiredParam("command");
			switch (command)
			{
				case "duplicateBook":
					HandleDuplicateBook(book, request);
					break;
				case "makeBloompack":
					HandleMakeBloompack(book);
					break;
				case "openFolderOnDisk":
					// Currently, the request comes with data to let us identify which book,
					// but it will always be the current book, which is all the model api lets us open anyway.
					//var book = GetBookObjectFromPost(request);
					_libraryModel.OpenFolderOnDisk();
					break;
				case "exportToWord":
					HandleExportToWord(book);
					break;
				case "exportToSpreadsheet":
					HandleExportToSpreadsheet(book);
					break;
				case "importSpreadsheetContent":
					HandleImportContentFromSpreadsheet(book, request);
					break;
				case "saveAsDotBloom":
					HandleSaveAsDotBloom(book);
					break;
				case "updateThumbnail":
					ScheduleRefreshOfOneThumbnail(book);
					break;
				case "updateBook":
					HandleBringBookUpToDate(book);
					break;
				case "deleteBook":
					_libraryModel.DeleteBook(book);
					break;
			}
			request.PostSucceeded();
		}

		private void HandleDuplicateBook(Book.Book book, ApiRequest request)
		{
			var collection = GetCollectionOfRequest(request);
			var newBookDir = book.Storage.Duplicate();

			// Get rid of any TC status we copied from the original, so Bloom treats it correctly as a new book.
			BookStorage.RemoveLocalOnlyFiles(newBookDir);

			ReloadEditableCollection(collection);

			var dupInfo = _libraryModel.TheOneEditableCollection.GetBookInfos()
				.FirstOrDefault(info => info.FolderPath == newBookDir);
			if (dupInfo != null)
			{
				var newBook = _libraryModel.GetBookFromBookInfo(dupInfo);
				// Select the new book
				_libraryModel.SelectBook(newBook);
				BookHistory.AddEvent(newBook, BookHistoryEventType.Created, $"Duplicated from existing book \"{book.Title}\"");
			}
		}

		private void ReloadEditableCollection(BookCollection collection)
		{
			// reload the collection
			// I hope we can get rid of this when we retire the old LibraryListView, but for now we need to keep both views up to date.
			// optimize: we only need to reload the first (editable) collection; better yet, we only need to add the one new book to it.
			_libraryModel.ReloadCollections();

			_webSocketServer.SendEvent("editableCollectionList", "reload:" + collection.PathToDirectory);
		}

		private void HandleMakeBloompack(Book.Book book)
		{
			using (var dlg = new DialogAdapters.SaveFileDialogAdapter())
			{
				var extension = Path.GetExtension(_libraryModel.GetSuggestedBloomPackPath());
				var filename = book.Storage.FileName;
				dlg.FileName = Path.ChangeExtension(filename, extension);
				dlg.Filter = "BloomPack|*.BloomPack";
				dlg.RestoreDirectory = true;
				dlg.OverwritePrompt = true;
				if (DialogResult.Cancel == dlg.ShowDialog())
				{
					return;
				}
				var folder = book.Storage.FolderPath;
				_libraryModel.MakeSingleBookBloomPack(dlg.FileName, book.Storage.FolderPath);
			}
		}

		private void HandleExportToWord(Book.Book book)
		{
			try
			{
				MessageBox.Show(LocalizationManager.GetString("CollectionTab.BookMenu.ExportDocMessage",
					"Bloom will now open this HTML document in your word processing program (normally Word or LibreOffice). You will be able to work with the text and images of this book. These programs normally don't do well with preserving the layout, so don't expect much."));
				var destPath = book.GetPathHtmlFile().Replace(".htm", ".doc");
				_libraryModel.ExportDocFormat(destPath);
				PathUtilities.OpenFileInApplication(destPath);
				Analytics.Track("Exported To Doc format");
			}
			catch (IOException error)
			{
				SIL.Reporting.ErrorReport.NotifyUserOfProblem(error.Message, "Could not export the book");
				Analytics.ReportException(error);
			}
			catch (Exception error)
			{
				SIL.Reporting.ErrorReport.NotifyUserOfProblem(error, "Could not export the book");
				Analytics.ReportException(error);
			}
		}

		private void HandleExportToSpreadsheet(Book.Book book)
		{
			// Throw up a Requires Bloom Enterprise dialog if it's not turned on
			if (!_libraryModel.CollectionSettings.HaveEnterpriseFeatures)
			{
				Enterprise.ShowRequiresEnterpriseNotice(Form.ActiveForm, "Export to Spreadsheet");
				return;
			}

			var bookPath = book.GetPathHtmlFile();
			try
			{
				var dom = new HtmlDom(XmlHtmlConverter.GetXmlDomFromHtmlFile(bookPath, false));
				var exporter = new SpreadsheetExporter();

				string outputFilename;

				using (var dlg = new DialogAdapters.SaveFileDialogAdapter())
				{
					var extension = "xlsx";
					var filename = book.Storage.FileName;
					dlg.FileName = Path.ChangeExtension(filename, extension);
					dlg.Filter = "xlsx|*.xlsx";
					dlg.InitialDirectory = !String.IsNullOrWhiteSpace(Settings.Default.ExportImportFileFolder) ? Settings.Default.ExportImportFileFolder : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
					dlg.RestoreDirectory = true;
					dlg.OverwritePrompt = true;
					if (DialogResult.Cancel == dlg.ShowDialog())
					{
						return;
					}
					outputFilename = dlg.FileName;
					Settings.Default.ExportImportFileFolder = Path.GetDirectoryName(outputFilename);
					Settings.Default.Save();
				}
				string imagesFolderPath = Path.GetDirectoryName(bookPath);
				var _sheet = exporter.Export(dom, imagesFolderPath);
				_sheet.WriteToFile(outputFilename);
				PathUtilities.OpenFileInApplication(outputFilename);
			}
			catch (Exception ex)
			{
				var msg = LocalizationManager.GetString("Spreadsheet:ExportFailed", "Export failed: ");
				NonFatalProblem.Report(ModalIf.All, PassiveIf.None, msg + ex.Message, exception: ex);
			}
		}

		private void HandleImportContentFromSpreadsheet(Book.Book book, ApiRequest request)
		{
			if (!_libraryModel.CollectionSettings.HaveEnterpriseFeatures)
			{
				Enterprise.ShowRequiresEnterpriseNotice(Form.ActiveForm, "Import to Spreadsheet");
				return;
			}

			var bookPath = book.GetPathHtmlFile();
			try
			{
				string inputFilepath;
				using (var dlg = new DialogAdapters.OpenFileDialogAdapter())
				{
					dlg.Filter = "xlsx|*.xlsx";
					dlg.RestoreDirectory = true;
					dlg.InitialDirectory = !String.IsNullOrWhiteSpace(Settings.Default.ExportImportFileFolder) ? Settings.Default.ExportImportFileFolder : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
					if (DialogResult.Cancel == dlg.ShowDialog())
					{
						return;
					}
					inputFilepath = dlg.FileName;
					Settings.Default.ExportImportFileFolder = Path.GetDirectoryName(inputFilepath);
					Settings.Default.Save();
				}

				string folderPath = book.FolderPath;
				BookStorage.SaveCopyBeforeImportOverwrite(folderPath, bookPath);

				var sheet = InternalSpreadsheet.ReadFromFile(inputFilepath);
				var dom = book.OurHtmlDom;
				var importer = new SpreadsheetImporter(dom, sheet);
				var messages = importer.Import();
				if (messages.Count > 0)
				{
					var allMessages = String.Join("\r\n", messages);
					var mainMsg = LocalizationManager.GetString("Spreadsheet:ImportWarning", "Import warning: ");
					NonFatalProblem.Report(ModalIf.All, PassiveIf.None, mainMsg, moreDetails: allMessages, showSendReport: false);
				}
				// Review: A lot of other stuff happens in Book.Save() and BookStorage.SaveHtml().
				// I doubt we need any of it for current purposes, but later we might.
				XmlHtmlConverter.SaveDOMAsHtml5(dom.RawDom, bookPath);
				book.ReloadFromDisk(null);
				_bookSelection.InvokeSelectionChanged(false);

				// reload the collection so the backups show up.
				// Note: if we come up with some other backup strategy, we can remove this.
				var collection = GetCollectionOfRequest(request);
				ReloadEditableCollection(collection);

			}
			catch (Exception ex)
			{
				var msg = LocalizationManager.GetString("Spreadsheet:ImportFailed", "Import failed: ");
				NonFatalProblem.Report(ModalIf.All, PassiveIf.None, msg + ex.Message, exception: ex, showSendReport: false);
			}
		}

		private void ScheduleRefreshOfOneThumbnail(Book.Book book)
		{
			_libraryModel.UpdateThumbnailAsync(book, new HtmlThumbNailer.ThumbnailOptions(), RefreshOneThumbnail, HandleThumbnailerErrror);
		}

		private void RefreshOneThumbnail(Book.BookInfo bookInfo, Image image)
		{
			// The arguments here are not currently used (method signature is legacy),
			// but may be useful if we optimize.
			// optimize: I think this will reload all of them
			_webSocketServer.SendString("bookImage", "reload",bookInfo.Id);
		}

		private void HandleThumbnailerErrror(Book.BookInfo bookInfo, Exception error)
		{
			string path = Path.Combine(bookInfo.FolderPath, "thumbnail.png");
			try
			{
				Resources.Error70x70.Save(@path, ImageFormat.Png);
			}
			catch (Exception e)
			{
				Logger.WriteError("Could not save error icon for book", e);
			}

			RefreshOneThumbnail(bookInfo, Resources.Error70x70);
		}


		private void HandleSaveAsDotBloom(Book.Book book)
		{
			const string bloomFilter = "Bloom files (*.bloom)|*.bloom|All files (*.*)|*.*";

			HandleBringBookUpToDate(book);

			var srcFolderName = book.StoragePageFolder;

			// Save As dialog, initially proposing My Documents, then defaulting to last target folder
			// Review: Do we need to persist this to some settings somewhere, or just for the current run?
			var dlg = new SaveFileDialog
			{
				AddExtension = true,
				OverwritePrompt = true,
				DefaultExt = "bloom",
				FileName = Path.GetFileName(srcFolderName),
				Filter = bloomFilter,
				InitialDirectory = _previousTargetSaveAs != null ?
					_previousTargetSaveAs :
					Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
			};
			var result = dlg.ShowDialog(Form.ActiveForm);
			if (result != DialogResult.OK)
				return;

			var destFileName = dlg.FileName;
			_previousTargetSaveAs = Path.GetDirectoryName(destFileName);

			if (!_libraryModel.SaveAsBloomFile(srcFolderName, destFileName, out var exception))
			{
				// Purposefully not adding to the L10N burden...
				NonFatalProblem.Report(ModalIf.All, PassiveIf.None,
					shortUserLevelMessage: "The file could not be saved. Make sure it is not open and try again.",
					moreDetails: null,
					exception: exception, showSendReport: false);
			}
		}

		private void HandleBringBookUpToDate(Book.Book book)
		{
			try
			{
				// Currently this works on the current book, so the argument is ignored.
				// That's OK for now as currently the book passed will always be the current one.
				_libraryModel.BringBookUpToDate();
			}
			catch (Exception error)
			{
				var msg = LocalizationManager.GetString("Errors.ErrorUpdating",
					"There was a problem updating the book.  Restarting Bloom may fix the problem.  If not, please report the problem to us.");
				ErrorReport.NotifyUserOfProblem(error, msg);
			}
		}

		// List out all the collections we have loaded
		public void HandleListRequest(ApiRequest request)
		{
			dynamic output = new List<dynamic>();
			_libraryModel.GetBookCollections().ForEach(c =>
			{
				Debug.WriteLine($"collection: {c.Name}-->{c.PathToDirectory}");
				output.Add(
					new
					{
						id = c.PathToDirectory,
						name = c.Name
					});
			});
			request.ReplyWithJson(JsonConvert.SerializeObject(output));
		}
		public void HandleBooksRequest(ApiRequest request)
		{
			var collection = GetCollectionOfRequest(request);
			if (collection == null)
			{
				return; // have already called request failed at this point
			}

			// Note: the winforms version used ImproveAndRefreshBookButtons(), which may load the whole book.

			var infos = collection.GetBookInfos()
				.Select(info =>
				{
					//var book = _libraryModel.GetBookFromBookInfo(info);
					return new
						{id = info.Id, title = info.QuickTitleUserDisplay, collectionId = collection.PathToDirectory, folderName= Path.GetFileName(info.FolderPath) };
				});
			var json = DynamicJson.Serialize(infos);
			request.ReplyWithJson(json);
		}

		private BookCollection GetCollectionOfRequest(ApiRequest request)
		{
			var id = request.RequiredParam("collection-id").Trim();
			var collection = _libraryModel.GetBookCollections().Find(c => c.PathToDirectory == id);
			if (collection == null)
			{
				request.Failed($"Collection named '{id}' was not found.");
			}

			return collection;
		}

		public void HandleThumbnailRequest(ApiRequest request)
		{
			var bookInfo = GetBookInfoFromRequestParam(request);

			// TODO: This is just a hack to get something showing. It can't make new thumbnails
			string path = Path.Combine(bookInfo.FolderPath, "thumbnail.png");
			if (RobustFile.Exists(path))
				request.ReplyWithImage(path);
			else request.Failed("Thumbnail doesn't exist, and making a new thumbnail is not yet implemented.");
		}

		private BookInfo GetBookInfoFromRequestParam(ApiRequest request)
		{
			var bookId = request.RequiredParam("book-id");
			return GetCollectionOfRequest(request).GetBookInfos().FirstOrDefault(info => info.Id == bookId);
		}
		private BookInfo GetBookInfoFromPost(ApiRequest request)
		{
			var bookId = request.RequiredPostString();
			return GetCollectionOfRequest(request).GetBookInfos().FirstOrDefault(info => info.Id == bookId);
		}

		private Book.Book GetBookObjectFromPost(ApiRequest request)
		{
			var info = GetBookInfoFromPost(request);
			return _libraryModel.GetBookFromBookInfo(info);

		}
	}
}
