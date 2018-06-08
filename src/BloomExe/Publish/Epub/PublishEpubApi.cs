using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;
using Bloom.Api;
using Bloom.Book;
using Bloom.Collection;
using Bloom.web;
using DesktopAnalytics;
using L10NSharp;
using Newtonsoft.Json;
using SIL.IO;

namespace Bloom.Publish.Epub
{
	/// <summary>
	/// Handles API requests from the Epub publishing HTML UI
	/// </summary>
	public class PublishEpubApi
	{
		private const string kApiUrlPart = "publish/epub/"; // common prefix of requests this class handles
		private EpubMaker _epubMaker;
		// Autofac singletons we need and get through our constructor
		private BookServer _bookServer;
		private BookThumbNailer _thumbNailer;
		private BookSelection _bookSelection;
		private CollectionSettings _collectionSettings;
		private BloomWebSocketServer _webSocketServer;
		private readonly WebSocketProgress _progress;

		private EpubPublishUiSettings _desiredEpubSettings = new EpubPublishUiSettings();
		private bool _needNewPreview; // Used when asked to update preview while in the middle of using the current one (e.g., to save it).
		private Action<EpubMaker> _doWhenPreviewComplete; // Something to do when the current preview is complete (e.g., save it)
		private BackgroundWorker _previewWorker;
		private string _previewSrc;

		// This constant must match the ID that is used for the listener set up in the React component EpubPreview
		private const string kWebsocketPreviewId = "epubPreview";

		private string _lastDirectory; // where we saved the most recent previous epub, if any


		// This constant must match the ID that is used for the listener set up in the React component ProgressBox
		private const string kWebsocketProgressId = "progress";

		private EpubMaker EpubMaker { get; set; }

		public PublishEpubApi(BookThumbNailer thumbNailer, NavigationIsolator isolator, BookServer bookServer,
			BookSelection bookSelection, CollectionSettings collectionSettings, BloomWebSocketServer webSocketServer)
		{
			_thumbNailer = thumbNailer;
			_bookServer = bookServer;
			_bookSelection = bookSelection;
			_collectionSettings = collectionSettings;
			_webSocketServer = webSocketServer;
			_progress = new WebSocketProgress(_webSocketServer);
		}

		// Message is presumed already localized.
		private void ReportProgress(string message)
		{
			_webSocketServer.Send(kWebsocketProgressId, message);
		}

		public void RegisterWithServer(EnhancedImageServer server)
		{
			server.RegisterEndpointHandler(kApiUrlPart + "save", request =>
			{
				{
					string suggestedName = string.Format("{0}-{1}.epub", Path.GetFileName(_bookSelection.CurrentSelection.FolderPath),
						_collectionSettings.GetLanguage1Name("en"));
					using (var dlg = new DialogAdapters.SaveFileDialogAdapter())
					{
						if (!string.IsNullOrEmpty(_lastDirectory) && Directory.Exists(_lastDirectory))
							dlg.InitialDirectory = _lastDirectory;
						dlg.FileName = suggestedName;
						dlg.Filter = "EPUB|*.epub";
						dlg.OverwritePrompt = true;
						if (DialogResult.OK == dlg.ShowDialog())
						{
							_lastDirectory = Path.GetDirectoryName(dlg.FileName);
							var savePath = dlg.FileName;
							RequestPreviewOutput(maker =>
							{
								_epubMaker = maker;
								CompleteSave(savePath);
							});
						}
					}

					request.PostSucceeded();
				}
			}, true);

			server.RegisterEndpointHandler(kApiUrlPart + "epubSettings", request =>
			{
				if (request.HttpMethod == HttpMethods.Get)
				{
					request.ReplyWithJson(GetEpubSettings());
				}
				else
				{
					// post
					var settings = (EpubPublishUiSettings)JsonConvert.DeserializeObject(request.RequiredPostJson(), typeof(EpubPublishUiSettings));
					// gjm 12 Jun 2018: I wanted to have a separate call to save settings when the React page was shutting down,
					// but nothing I tried called such a function reliably. So we save the checkbox settings to the bookinfo
					// whenever they change.
					UpdateAndSaveBookInfo(settings);
					UpdatePreview(settings, false);

					request.PostSucceeded();
				}
			}, true);

			server.RegisterEndpointHandler(kApiUrlPart + "updatePreview", request =>
			{
				var settings = (EpubPublishUiSettings)JsonConvert.DeserializeObject(request.RequiredPostJson(), typeof(EpubPublishUiSettings));
				UpdatePreview(settings, true);
				request.PostSucceeded();
			}, true);
		}

		private void UpdateAndSaveBookInfo(EpubPublishUiSettings settings)
		{
			if (_bookSelection == null)
				return;
			var info = _bookSelection.CurrentSelection.BookInfo;
			info.PublishImageDescriptionsInEpub = settings.GetImageDescriptionSettingAsString();
			info.RemoveFontSizesInEpub = settings.removeFontSizes;
			info.Save();
		}

		private void CompleteSave(string savePath)
		{
			_epubMaker.ZipAndSaveEpub(savePath, _progress);
			ReportProgress(LocalizationManager.GetString("PublishTab.Epub.Done", "Done"));
			ReportAnalytics("Save ePUB");
		}

		public void ReportAnalytics(string eventName)
		{
			Analytics.Track(eventName, new Dictionary<string, string>()
			{
				{"BookId", _bookSelection.CurrentSelection.ID},
				{"Country", _collectionSettings.Country}
			});
		}

		/// <summary>
		/// Called by the PublishView when it opens the Epub view on a particular book, to prepare
		/// this API to respond appropriately to requests from the HTML about that book.
		/// In particular we currently need to get rid of the old EpubMaker, if any, and make a new one.
		/// The second half of this could plausibly be done in response to a request from the
		/// HTML side for an initial preview. But at this point that just seems to complicate things.
		/// </summary>
		public void SetupForCurrentBook()
		{
			if (EpubMaker != null)
			{
				EpubMaker.Dispose();
				EpubMaker = null;
			}
			PrepareToStageEpub(); // let's get the epub maker and its browser created on the UI thread
		}

		internal void PrepareToStageEpub()
		{
			if (EpubMaker != null)
			{
				//it has state that we don't want to reuse, so make a new one
				EpubMaker.Dispose();
				EpubMaker = null;
			}
			EpubMaker = new EpubMaker(_thumbNailer, _bookServer);
			EpubMaker.Book = _bookSelection.CurrentSelection;
			EpubMaker.Unpaginated = true; // Enhance: UI?
			EpubMaker.OneAudioPerPage = true;
		}

		internal string StagingDirectory { get { return EpubMaker.BookInStagingFolder; } }

		internal void SaveAsEpub()
		{
			using (var dlg = new DialogAdapters.SaveFileDialogAdapter())
			{
				if (!string.IsNullOrEmpty(_lastDirectory) && Directory.Exists(_lastDirectory))
					dlg.InitialDirectory = _lastDirectory;

				string suggestedName = string.Format("{0}-{1}.epub", Path.GetFileName(_bookSelection.CurrentSelection.FolderPath),
					_collectionSettings.GetLanguage1Name("en"));
				dlg.FileName = suggestedName;
				dlg.Filter = "EPUB|*.epub";
				dlg.OverwritePrompt = true;
				if (DialogResult.OK == dlg.ShowDialog())
				{
					_lastDirectory = Path.GetDirectoryName(dlg.FileName);
					EpubMaker.ZipAndSaveEpub(dlg.FileName, _progress);
					ReportAnalytics("Save ePUB");
				}
			}
		}

		public string UpdateEpubControlContent()
		{
			// Enhance: this could be optimized (but it will require changes to EpubMaker, it assumes it only stages once)
			var publishImageDescriptions = EpubMaker.PublishImageDescriptions; // before we dispose it
			var removeFontSizes = EpubMaker.RemoveFontSizes;
			PrepareToStageEpub();
			EpubMaker.PublishImageDescriptions = publishImageDescriptions; // restore on new one
			EpubMaker.RemoveFontSizes = removeFontSizes;
			return SetupEpubControlContent();
		}

		public string SetupEpubControlContent()
		{
			// This gets called on a background thread but one step needs to happen on the UI thread,
			// so the Maker needs a control to Invoke on.
			EpubMaker.ControlForInvoke = Form.ActiveForm;
			EpubMaker.StageEpub(_progress);

			var fileLocator = _bookSelection.CurrentSelection.GetFileLocator();
			var root = fileLocator.LocateDirectoryWithThrow("Readium");
			var tempFolder = Path.GetDirectoryName(StagingDirectory);
			// This is kludge. I hope it can be improved. To make a preview we currently need all the Readium
			// files in a folder that is a parent of the staging folder containing the book content.
			// This allows us to tell Readium about the book by passing the name of the folder using the ?ePUB=
			// URL parameter. It doesn't work to use the original Readium file and make the parameter a full path.
			// It's possible that there is some variation that would work, e.g., make the param a full file:/// url
			// to the book folder. It's also possible we could get away with only copying the HTML file itself,
			// if we modified it to have localhost: links to the JS and CSS. Haven't tried this yet. The current
			// approach at least works.
			DirectoryUtilities.CopyDirectoryContents(root, tempFolder);

			// Not sure if we will need this. The current UI does not appear to have a way to indicate whether
			// we have a talking book, a book without audio, or one that has audio but it is not being published.
			//var audioSituationClass = "noAudioAvailable";
			//if (EpubMaker.PublishWithoutAudio)
			//	audioSituationClass = "haveAudioButNotMakingTalkingBook";
			//else if (BookHasAudio)
			//	audioSituationClass = "isTalkingBook";

			var targetFile = Path.Combine(tempFolder, "readium-cloudreader.htm");

			var iframeSource = targetFile.ToLocalhost() + "?epub=" + Path.GetFileName(StagingDirectory);
			return iframeSource;
		}

		internal string GetEpubSettings()
		{
			if (_bookSelection != null)
			{
				var info = _bookSelection.CurrentSelection.BookInfo;
				_desiredEpubSettings.howToPublishImageDescriptions = EpubPublishUiSettings.GetImageDescriptionSettingFromString(info.PublishImageDescriptionsInEpub);
				_desiredEpubSettings.removeFontSizes = info.RemoveFontSizesInEpub;
			}

			return JsonConvert.SerializeObject(_desiredEpubSettings);
		}

		public void UpdatePreview(EpubPublishUiSettings newSettings, bool force)
		{
			lock (this)
			{
				if (_desiredEpubSettings == newSettings && !force)
					return; // getting a request really from the browser, and already in that state.
				_desiredEpubSettings = newSettings;
				if (_previewWorker != null)
				{
					// Something changed before we even finished generating the preview! abort the current attempt, which will lead
					// to trying again.
					EpubMaker.AbortRequested = true;
					return;
				}

				if (_doWhenPreviewComplete != null)
				{
					// We're committed to doing something with a completed preview...and we're done making the preview...
					// so probably we're in the middle of doing the completed preview action.
					// We need to let it complete; THEN we should update the preview again.
					_needNewPreview = true;
					return;
				}
				_previewWorker = new BackgroundWorker();
			}

			// If we've changed books we can't reuse this EpubMaker.
			if (EpubMaker != null && EpubMaker.Book != _bookSelection.CurrentSelection)
			{
				EpubMaker.Dispose();
				EpubMaker = null;
			}

			// I believe initialization of the EpubMaker needs to happen on the UI thread,
			// something to do with navigating its embedded browser.
			if (EpubMaker == null)
			{
				if (Form.ActiveForm == null) // this is null when we are off debugging in firefox or chrome, not winforms
					return;

				Form.ActiveForm.Invoke((Action) (() => PrepareToStageEpub()));
			}

			EpubMaker.PublishImageDescriptions = newSettings.howToPublishImageDescriptions;
			EpubMaker.RemoveFontSizes = newSettings.removeFontSizes;
			// clear the obsolete preview, if any; this also ensures that when the new one gets done,
			// we will really be changing the src attr in the preview iframe so the display will update.
			_webSocketServer.Send(kWebsocketPreviewId, "");
			ReportProgress(LocalizationManager.GetString("PublishTab.Epub.PreparingPreview", "Preparing Preview"));
			_previewWorker.RunWorkerCompleted += _previewWorker_RunWorkerCompleted;
			_previewWorker.DoWork += (sender, args) =>
			{
				_previewSrc = UpdateEpubControlContent();
			};
			_previewWorker.RunWorkerAsync();
		}

		private void _previewWorker_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
		{
			bool abortRequested;
			// I'm not absolutely sure that this and UpdatePreview will always run on the UI thread.
			// So there is a possible race condition:
			// while we are running this method, the user does something which results in
			// a new UpdatePreview on another thread.
			// If updatePreview gets the lock first, it will set the abort flag and quit;
			// this method will clean up and call UpdatePreview again.
			// If this method gets the lock first, it will proceed to update the preview
			// with the successful results it obtained. The new update will proceed in the background.
			// That should be OK, though there is probably a rare pathological case where progress shows
			// two Preparing messages followed by two Done messages.
			lock (this)
			{
				_previewWorker.Dispose();
				_previewWorker = null; // allows UpdatePrevew to know nothing is in progress
				abortRequested = EpubMaker.AbortRequested;
				// Either we just made a successful preview, or we're just about to try again.
				// Either way, we don't need yet another new one later (unless another change happens)
				_needNewPreview = false;
			}

			if (abortRequested || EpubMaker.PublishImageDescriptions != _desiredEpubSettings.howToPublishImageDescriptions
				|| EpubMaker.RemoveFontSizes != _desiredEpubSettings.removeFontSizes)
			{
				UpdatePreview(_desiredEpubSettings, true);
				return;
			}

			if (_doWhenPreviewComplete != null)
			{
				Debug.Assert(!EpubMaker.AbortRequested);
				Debug.Assert(EpubMaker.PublishImageDescriptions == _desiredEpubSettings.howToPublishImageDescriptions);
				Debug.Assert(EpubMaker.RemoveFontSizes == _desiredEpubSettings.removeFontSizes);
				_doWhenPreviewComplete(EpubMaker);
				_doWhenPreviewComplete = null;

				if (_needNewPreview)
				{
					// We got a request somewhere in the process of running the action.
					UpdatePreview(_desiredEpubSettings, true);
					return;
				}
			}

			_webSocketServer.Send(kWebsocketPreviewId, _previewSrc);
			ReportProgress(LocalizationManager.GetString("PublishTab.Epub.Done", "Done"));
		}

		/// <summary>
		/// Perform the requested action (currently the only example is, save the epub) when we have an up-to-date
		/// preview. Pass it the EpubMaker that generated the preview.
		/// </summary>
		/// <param name="doWhenReady"></param>
		public void RequestPreviewOutput(Action<EpubMaker> doWhenReady)
		{
			lock (this)
			{
				_doWhenPreviewComplete = doWhenReady;
				if (_previewWorker != null)
					return; // in process of making, can't do it now; will be done in _previewWorker_RunWorkerCompleted.
			}

			Debug.Assert(!EpubMaker.AbortRequested);
			Debug.Assert(EpubMaker.PublishImageDescriptions == _desiredEpubSettings.howToPublishImageDescriptions);
			Debug.Assert(EpubMaker.RemoveFontSizes == _desiredEpubSettings.removeFontSizes);
			_doWhenPreviewComplete(EpubMaker);

			_doWhenPreviewComplete = null;

			if (_needNewPreview) // we got a request during action processing
				UpdatePreview(_desiredEpubSettings, true);
		}
	}
}
