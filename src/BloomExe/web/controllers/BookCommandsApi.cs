using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Bloom.Api;
using Bloom.Book;
using Bloom.Collection;
using Bloom.CollectionTab;
using Bloom.MiscUI;
using Bloom.Properties;
using Bloom.Spreadsheet;
using Bloom.TeamCollection;
using DesktopAnalytics;
using L10NSharp;
using SIL.IO;
using SIL.Reporting;

namespace Bloom.web.controllers
{
	/// <summary>
	/// Commands that affect entire books, typically menu commands in the Collection tab right-click menu
	/// </summary>
	public class BookCommandsApi
	{
		private readonly LibraryModel _libraryModel;
		private BookSelection _bookSelection;
		private readonly BloomWebSocketServer _webSocketServer;
		private string _previousTargetSaveAs; // enhance: should this be shared with CollectionApi or other save as locations?

		public BookCommandsApi(LibraryModel libraryModel, BloomWebSocketServer webSocketServer, BookSelection bookSelection)
		{
			_libraryModel = libraryModel;
			_webSocketServer = webSocketServer;
			_bookSelection = bookSelection;
		}
		public void RegisterWithApiHandler(BloomApiHandler apiHandler)
		{

			apiHandler.RegisterEndpointHandler("bookCommand/", HandleBookCommand, true);
		}

		private void HandleBookCommand(ApiRequest request)
		{
			var book = GetBookObjectFromPost(request);
			var command = request.LocalPath().Substring((BloomApiHandler.ApiPrefix + "bookCommand/").Length);
			switch (command)
			{
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
				// As currently implemented this would more naturally go in CollectionApi, since it adds a book
				// to the collection (a backup). However, we are probably going to change how backups are handled
				// so this is no longer true.
				case "importSpreadsheetContent":
					HandleImportContentFromSpreadsheet(book);
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
				case "rename":
					HandleRename(book, request);
					break;
			}
			request.PostSucceeded();
		}

		private void HandleRename(Book.Book book, ApiRequest request)
		{
			var newName = request.RequiredParam("name");
			book.SetAndLockBookName(newName);

			_libraryModel.UpdateLabelOfBookInEditableCollection(book);

			BookHistory.AddEvent(book, BookHistoryEventType.Renamed, $"Book renamed to \"{newName}\"");
		}


		// TODO: Delete me after all references removed.
		[Obsolete("Wrapper to allow legacy (WinForms) code to share this code. New code should try to use the API/React-based paradigm instead.")]
		public void HandleMakeBloompackWrapper(Book.Book book) => this.HandleMakeBloompack(book);
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

		// TODO: Delete me after all references removed.
		[Obsolete("Wrapper to allow legacy (WinForms) code to share this code. New code should try to use the API/React-based paradigm instead.")]
		public void HandleExportToSpreadsheetWrapper(Book.Book book) => this.HandleExportToSpreadsheet(book);
		
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
				var exporter = new SpreadsheetExporter(_webSocketServer);

				var initialFolder = !String.IsNullOrWhiteSpace(Settings.Default.ExportImportFileFolder) ? Settings.Default.ExportImportFileFolder : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
				string outputParentFolder = BloomFolderChooser.ChooseFolder(initialFolder);
				string outputFolder = Path.Combine(outputParentFolder, Path.GetFileNameWithoutExtension(bookPath));
				string imagesFolderPath = Path.GetDirectoryName(bookPath);
				exporter.ExportToFolderWithProgress(dom, imagesFolderPath, outputFolder, outputFilePath =>
				{
					if (outputFilePath != null)
						PathUtilities.OpenFileInApplication(outputFilePath);
				});
			}
			catch (Exception ex)
			{
				var msg = LocalizationManager.GetString("Spreadsheet:ExportFailed", "Export failed: ");
				NonFatalProblem.Report(ModalIf.All, PassiveIf.None, msg + ex.Message, exception: ex);
			}
		}

		// TODO: Delete me after all references removed.
		[Obsolete("Wrapper to allow legacy (WinForms) code to share this code. New code should try to use the API/React-based paradigm instead.")]
		public void HandleImportContentFromSpreadsheetWrapper(Book.Book book) => this.HandleImportContentFromSpreadsheet(book);

		private void HandleImportContentFromSpreadsheet(Book.Book book)
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
				_libraryModel.ReloadEditableCollection();

			}
			catch (Exception ex)
			{
				var msg = LocalizationManager.GetString("Spreadsheet:ImportFailed", "Import failed: ");
				NonFatalProblem.Report(ModalIf.All, PassiveIf.None, msg + ex.Message, exception: ex, showSendReport: false);
			}
		}

		private void ScheduleRefreshOfOneThumbnail(Book.Book book)
		{
			_libraryModel.UpdateThumbnailAsync(book);
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
	}
}
