using Bloom.Api;
using Bloom.Publish;
using Bloom.Publish.BloomLibrary;
using Bloom.WebLibraryIntegration;
using Bloom.Workspace;
using SIL.Progress;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;

namespace Bloom.web.controllers
{
	/// <summary>
	/// APIs related to the Library (Web) Publish screen.
	/// </summary>
	class LibraryPublishApi
	{
		public static BloomLibraryPublishModel Model { get; set; }

		// This goes out with our messages and, on the client side (typescript), messages are filtered
		// down to the context (usualy a screen) that requested them.
		private const string kWebSocketContext = "libraryPublish"; // must match what is in LibraryPublishScreen.tsx

		private const string kWebSocketEventId_uploadSuccessful = "uploadSuccessful"; // must match what is in LibraryPublishSteps.tsx

		private PublishView _publishView;
		private IBloomWebSocketServer _webSocketServer;
		private WebSocketProgress _webSocketProgress;
		private IProgress _progress;

		public LibraryPublishApi(BloomWebSocketServer webSocketServer, PublishView publishView)
		{
			_publishView = publishView;
			_webSocketServer = webSocketServer;
			var progress = new WebSocketProgress(_webSocketServer, kWebSocketContext);
			_webSocketProgress = progress.WithL10NPrefix("PublishTab.Upload.");
			_webSocketProgress.LogAllMessages = true;
			_progress = new WebProgressAdapter(_webSocketProgress);
		}

		private string CurrentSignLanguageName
		{
			get
			{
				return Model.Book.CollectionSettings.SignLanguage.Name;
			}
		}

		public void RegisterWithApiHandler(BloomApiHandler apiHandler)
		{
			apiHandler.RegisterEndpointHandler("libraryPublish/upload", HandleUpload, true);
			apiHandler.RegisterEndpointHandler("libraryPublish/uploadCollection", HandleUploadCollection, true);
			apiHandler.RegisterEndpointHandler("libraryPublish/uploadFolderOfCollections", HandleUploadFolderOfCollections, true);
			apiHandler.RegisterEndpointHandler("libraryPublish/getBookInfo", HandleGetBookInfo, true);
			apiHandler.RegisterEndpointHandler("libraryPublish/setSummary", HandleSetSummary, true);
			apiHandler.RegisterEndpointHandler("libraryPublish/useSandbox", HandleUseSandbox, true);
			apiHandler.RegisterEndpointHandler("libraryPublish/cancel", HandleCancel, true);
			apiHandler.RegisterEndpointHandler("libraryPublish/getUploadCollisionInfo", HandleGetUploadCollisionInfo, true);
			apiHandler.RegisterEndpointHandler("libraryPublish/uploadAfterChangingBookId", HandleUploadAfterChangingBookId, true);
		}

		private void HandleGetBookInfo(ApiRequest request)
		{
			dynamic bookInfo = new
			{
				title = Model.Title,
				summary = Model.Summary,
				copyright = Model.Copyright,
				licenseType = Model.LicenseType.ToString(),
				licenseToken = Model.LicenseToken,
				licenseRights = Model.LicenseRights,
				isTemplate = Model.IsTemplate
			};
			request.ReplyWithJson(bookInfo);
		}

		private void HandleSetSummary(ApiRequest request)
		{
			Model.Summary = request.GetPostStringOrNull();
			request.PostSucceeded();
		}

		private void HandleUseSandbox(ApiRequest request)
		{
			request.ReplyWithBoolean(BookUpload.UseSandbox);
		}

		private void HandleCancel(ApiRequest request)
		{
			_progress.CancelRequested = true;
			request.PostSucceeded();
		}

		private void HandleUpload(ApiRequest request)
		{
			if (request.HttpMethod == HttpMethods.Get)
				return;

			_progress.CancelRequested = false;

			//TODO verify user doesn't select sign language feature without setting language
			// Not sure how to do that until we implement the settings... original just looked at the
			// sign language checkbox. Seems like here we'll be looking for it be persisted? But it appears
			// the original never let it be persisted in this state.
			//Model.IsPublishSignLanguage() ?
			//Model.Book.BookInfo.MetaData.Feature_SignLanguage ?
			//if (_signLanguageCheckBox.Checked
			//	&& string.IsNullOrEmpty(CurrentSignLanguageName))
			//{
			//	// report error in progress and bail
			//	_webSocketProgress.Message("ChooseSignLanguageWarning",
			//		"Please choose the sign language for this book", ProgressKind.Error);
			//	request.PostSucceeded();
			//	return;
			//}

			try
			{
				_webSocketProgress.Message("CheckingVersionEligibility", "Checking Bloom version eligibility...");
				if (!Model.IsThisVersionAllowedToUpload)
				{
					_webSocketProgress.Message(
						"OldVersion",
						"Sorry, this version of Bloom Desktop is not compatible with the current version of BloomLibrary.org. Please upgrade to a newer version.",
						ProgressKind.Error);
					_webSocketProgress.Message("Cancelled", "Upload was cancelled");
					request.PostSucceeded();
					return;
				}

				UploadBook();
			}
			catch (Exception)
			{
				ReportTryAgainDuringUpload();
			}
			request.PostSucceeded();
		}

		private void UploadBook()
		{
			_webSocketProgress.Message("Common.Starting", "Starting...");

			var worker = new BackgroundWorker();
			worker.DoWork += BackgroundUpload;
			worker.RunWorkerCompleted += (_, completedEvent) =>
			{
				// Return all controls to normal state. (Do this first, just in case we get some further exception somehow.)
				// I believe the event is guaranteed to be raised, even if something in the worker thread throws,
				// so there should be no way to get stuck in the state where the tabs etc. are disabled.					
				SetParentControlsState(true);

				if (_progress.CancelRequested)
				{
					_webSocketProgress.Message("Cancelled", "Upload was cancelled", ProgressKind.Error);
					return;
				}

				if (completedEvent.Error != null)
				{
					ReportBasicErrorDuringUpload();
					_webSocketProgress.Exception(completedEvent.Error);
					return;
				}

				(string uploadResult, string parseId) workerResult = ((string uploadResult, string parseId))completedEvent.Result;
				var uploadResult = workerResult.uploadResult;
				var parseId = workerResult.parseId;

				if (uploadResult == "quiet")
				{
					// no more reporting, sufficient message already given.
				}
				else if (string.IsNullOrEmpty(uploadResult))
				{
					// Something went wrong, possibly already reported.
					if (!Model.PdfGenerationSucceeded)
						ReportPdfGenerationFailed();
					else
						ReportTryAgainDuringUpload();
				}
				else
				{
					var url = BloomLibraryUrls.BloomLibraryDetailPageUrlFromBookId(parseId, true);
					Model.AddHistoryRecordForLibraryUpload(url);
					_webSocketServer.SendString(kWebSocketContext, kWebSocketEventId_uploadSuccessful, url);
				}
			};
			SetParentControlsState(false); // Last thing we do before launching the worker, so we can't get stuck in this state.
			worker.RunWorkerAsync(Model);

		}

		void BackgroundUpload(object _, DoWorkEventArgs e)
		{
			// TODO get selected languages
			var languages = new string[0];

			// REVIEW: maybe this whole check should be called from UploadOneBook?
			var checkerResult = Model.CheckBookBeforeUpload(languages);
			if (checkerResult != null)
			{
				_webSocketProgress.MessageWithoutLocalizing(checkerResult, ProgressKind.Error);
				e.Result = "quiet"; // suppress other completion/fail messages
				return;
			}

			//TODO
			Model.UpdateBookMetadataFeatures(
				false, false, false
			);

			//TODO
			var includeBackgroundMusic = false;

			var uploadResult = Model.UploadOneBook(Model.Book, _progress, _publishView, !includeBackgroundMusic, out var parseId);

			e.Result = (uploadResult, parseId);
		}

		private void ReportBasicErrorDuringUpload()
		{
			_webSocketProgress.MessageUsingTitle(
				"ErrorUploading",
				"Sorry, there was a problem uploading {0}. Some details follow. You may need technical help.",
				Model.Title,
				ProgressKind.Error);
		}

		private void ReportPdfGenerationFailed()
		{
			ReportBasicErrorDuringUpload();
			_webSocketProgress.Message("BadPdfShort", "Bloom had a problem making a PDF of this book.", ProgressKind.Error);
		}
		private void ReportTryAgainDuringUpload()
		{
			_webSocketProgress.MessageUsingTitle(
				"FinalUploadFailureNotice",
				"Sorry, \"{0}\" was not successfully uploaded. Sometimes this is caused by temporary problems with the servers we use. It's worth trying again in an hour or two. If you regularly get this problem please report it to us.",
				Model.Title,
				ProgressKind.Error);
		}
		private void SetParentControlsState(bool enable)
		{
			_publishView.SetStateOfNonUploadRadios(enable);

			var parent = _publishView.Parent;
			while (parent != null && !(parent is WorkspaceView))
				parent = parent.Parent;
			((WorkspaceView)parent)?.SetStateOfNonPublishTabs(enable);
		}

		private void HandleUploadCollection(ApiRequest request)
		{
			if (!ValidateBookshelfBeforeBulkUpload()) { request.PostSucceeded(); return; }

			Model.BulkUpload(Model.Book.CollectionSettings.FolderPath, _progress);
			request.PostSucceeded();
		}

		private void HandleUploadFolderOfCollections(ApiRequest request)
		{
			if (!ValidateBookshelfBeforeBulkUpload()) { request.PostSucceeded(); return; }

			var folderPath = request.RequiredPostString();
			if (!string.IsNullOrEmpty(folderPath) && Directory.Exists(folderPath))
				Model.BulkUpload(folderPath, _progress);

			request.PostSucceeded();
		}

		private bool ValidateBookshelfBeforeBulkUpload()
		{
			// for now, we're limiting this to projects that have set up a default bookshelf
			// so that all their books go to the correct place.
			if (string.IsNullOrEmpty(Model.Book.CollectionSettings.DefaultBookshelf))
			{
				// Intentionally not localized ( because it's complicated, rare, and generally advanced )
				_webSocketProgress.MessageWithoutLocalizing(
					"Before sending all of your books to BloomLibrary.org, you probably want to tell Bloom which bookshelf this collection belongs in. Please go to Collection Tab : Settings : Book Making and set the \"Bloom Library Bookshelf\".",
					ProgressKind.Error);

				return false;
			}
			return true;
		}

		private void HandleGetUploadCollisionInfo(ApiRequest request)
		{			
			_webSocketProgress.Message("CheckingExistingCopy", "Checking for existing copy on server...");

			dynamic collisionDialogInfo;
			if (Model.BookIsAlreadyOnServer)
			{
				// TODO
				bool signLanguageFeatureSelected = false;

				collisionDialogInfo = Model.GetUploadCollisionDialogProps(GetLanguagesToUpload(), signLanguageFeatureSelected);
			}
			else
			{
				collisionDialogInfo = new
				{
					shouldShow = false
				};
			}

			request.ReplyWithJson(collisionDialogInfo);
		}

		private IEnumerable<string> GetLanguagesToUpload()
		{
			// TODO waiting to see how we do the settings before I know if this is correct.
			// If it is, we should make it a property on the model.
			return
				Model.Book.BookInfo.PublishSettings.BloomLibrary.TextLangs
				.Where(l => l.Value.IsIncluded())
				.Select(l => l.Key);
		}

		private void HandleUploadAfterChangingBookId(ApiRequest request)
		{
			Model.ChangeBookId(_progress);
			HandleUpload(request);
		}
	}
}
