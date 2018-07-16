using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading;
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
		// The usual place to report progress, read by the progress panel in the main epub preview window.
		private readonly WebSocketProgress _standardProgress;
		// The progress socket manager that is actually used to report progress with making epubs.
		// Usually _standardProgress, but when the epub is being generated for another purpose
		// besides the preview in the main epub window (e.g., for Daisy checker), we use that
		// window's progress box.
		private WebSocketProgress _progress;

		private EpubPublishUiSettings _desiredEpubSettings = new EpubPublishUiSettings();
		private bool _needNewPreview; // Used when asked to update preview while in the middle of using the current one (e.g., to save it).
		private string _previewSrc;
		private string _bookVersion;

		// This goes out with our messages and, on the client side (typescript), messages are filtered
		// down to the context (usualy a screen) that requested them. 
		private const string kWebsocketContext = "publish-epub";

		// This constant must match the ID that is used for the listener set up in the React component EpubPreview
		private const string kWebsocketEventId_Preview = "epubPreview";

		private string _lastDirectory; // where we saved the most recent previous epub, if any

		private string _saveAsPath;


		// This constant must match the ID that is used for the listener set up in the React component ProgressBox
		private const string kWebsocketEventId_Progress = "progress";

		public EpubMaker EpubMaker { get; private set; }
		public static Control ControlForInvoke { get; set; }

		public PublishEpubApi(BookThumbNailer thumbNailer, NavigationIsolator isolator, BookServer bookServer,
			BookSelection bookSelection, CollectionSettings collectionSettings, BloomWebSocketServer webSocketServer)
		{
			_thumbNailer = thumbNailer;
			_bookServer = bookServer;
			_bookSelection = bookSelection;
			_collectionSettings = collectionSettings;
			_webSocketServer = webSocketServer;
			_standardProgress = new WebSocketProgress(_webSocketServer, kWebsocketContext);
		}

		// Message is presumed already localized.
		private void ReportProgress(string message)
		{
			_webSocketServer.SendString(kWebsocketContext, kWebsocketEventId_Progress, message);
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
							lock (this)
							{
								_saveAsPath = dlg.FileName;
								if (!EpubMaker.StagingEpub)
								{
									// we can do it right now. No need to check version etc., because anything
									// that will change the epub we want to save will immediately trigger a new
									// preview, and we will be staging it until we have it.
									SaveAsEpub();
								}
							}
							ReportProgress(LocalizationManager.GetString("PublishTab.Epub.Done", "Done"));
							ReportAnalytics("Save ePUB");
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
					// post is deprecated.
					throw new ApplicationException("epubSettings POST is deprecated");
				}
			}, false);

			// At this point, this is a checkbox backed by an enum (YAGNI?) that the user ticks to say
			// "put my image descriptions on the epub page"
			server.RegisterEnumEndpointHandler(kApiUrlPart + "imageDescriptionSetting",
				request => request.CurrentBook.BookInfo.MetaData.Epub_HowToPublishImageDescriptions,
				(request, enumSetting) => {
					request.CurrentBook.BookInfo.MetaData.Epub_HowToPublishImageDescriptions = enumSetting;
					request.CurrentBook.BookInfo.Save();
					var newSettings = _desiredEpubSettings.Clone();
					newSettings.howToPublishImageDescriptions = enumSetting;
					RefreshPreview(newSettings);
				},
				false, false);

			// Saving a checkbox setting that the user ticks to say "Use my E-reader's font sizes"
			server.RegisterBooleanEndpointHandler(kApiUrlPart + "removeFontSizesSetting",
				request => request.CurrentBook.BookInfo.MetaData.Epub_RemoveFontSizes,
				(request, booleanSetting) => {
					request.CurrentBook.BookInfo.MetaData.Epub_RemoveFontSizes = booleanSetting;
					request.CurrentBook.BookInfo.Save();
					var newSettings = _desiredEpubSettings.Clone();
					newSettings.removeFontSizes = booleanSetting;
					RefreshPreview(newSettings);
				},
				false, false);

			server.RegisterEndpointHandler(kApiUrlPart + "updatePreview", request =>
			{
				RefreshPreview(_desiredEpubSettings);
				request.PostSucceeded();
			}, false, false); // in fact, must NOT be on UI thread
		}

		private void RefreshPreview(EpubPublishUiSettings newSettings)
		{
			if (UpdatePreview(newSettings, true))
				ControlForInvoke.Invoke((Action)(() => _webSocketServer.SendString(kWebsocketContext, kWebsocketEventId_Preview, _previewSrc)));
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
			if (_saveAsPath == null)
				return;
			EpubMaker.SaveEpub(_saveAsPath, _progress);
			_saveAsPath = null;
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
			EpubMaker.ControlForInvoke = ControlForInvoke;
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
				GetEpubSettingsForCurrentBook(_desiredEpubSettings);

			return JsonConvert.SerializeObject(_desiredEpubSettings);
		}

		public void GetEpubSettingsForCurrentBook(EpubPublishUiSettings epubPublishUiSettings)
		{
			var info = _bookSelection.CurrentSelection.BookInfo;
			epubPublishUiSettings.howToPublishImageDescriptions = info.MetaData.Epub_HowToPublishImageDescriptions;
			epubPublishUiSettings.removeFontSizes = info.MetaData.Epub_RemoveFontSizes;
		}

		public void UpdateAndSave(EpubPublishUiSettings newSettings, string path, bool force, WebSocketProgress progress = null)
		{
			bool succeeded;
			do
			{
				lock (this)
				{
					succeeded = UpdatePreview(newSettings, force, progress);
					if (succeeded)
						EpubMaker.SaveEpub(path, _progress);
				}
			} while (!succeeded); // try until we get a complete epub, not interrupted by user changing something.
		}

		public bool UpdatePreview(EpubPublishUiSettings newSettings, bool force, WebSocketProgress progress = null)
		{
			_progress = progress ?? _standardProgress;
			if (Program.RunningOnUiThread)
			{
				// There's some stuff inside this lock that has to run on the UI thread.
				// If we lock the UI thread here, we can deadlock the whole program.
				throw new ApplicationException(@"Must not attempt to make epubs on UI thread...will produce deadlocks");
			}

			// This is tricky. Since we're using a long-running lock to make sure that (a) only one thing uses EpubMaker at a time,
			// and (b) that things don't change in the middle of a save, we can't lock while we decide whether to abort;
			// if we do, the abort won't happen, since it typically originates from another thread.
			// We'd like to also abort if the current book version is not the saved one, but I want generating
			// the current version to be inside the lock so we know the epub we make is really for the current version.
			// There is therefore a path where an abort would be desired but not happen: if the user edits, clicks Refresh
			// in the ACE checker, edits again, then switches to epub pane, all while the Refresh is still happening.
			// In that situation, the ACE refresh epub will be fully generated, THEN a new epub will be generated for the
			// preview. I think this is rare enough that we can live with it; the annoying thing is when the user changes
			// the settings and we complete the obsolete epub before starting the right one.
			// There could also be a race condition where staging the obsolete epub completes after this thread
			// tested EpubMaker.StagingEpub and before it set AbortRequested. That's OK...this thread will still go on
			// and build a new epub.
			if (EpubMaker != null && EpubMaker.StagingEpub && _desiredEpubSettings != newSettings)
				EpubMaker.AbortRequested = true; // typically will cause some OTHER thread that has the lock to wind up quickly.

			lock (this)
			{
				if (EpubMaker != null)
					EpubMaker.AbortRequested = false;
				var htmlPath = _bookSelection.CurrentSelection.GetPathHtmlFile();
				var newVersion = Book.Book.MakeVersionCode(File.ReadAllText(htmlPath), htmlPath);
				if (_desiredEpubSettings == newSettings && EpubMaker != null && newVersion == _bookVersion &&
				    !EpubMaker.AbortRequested)
				{
					SaveAsEpub(); // just in case there's a race condition where we haven't already saved it.
					return true; // preview is already up to date.
				}

				_desiredEpubSettings = newSettings;
				// I think the only way _previewWorker can be non-null and EpubMaker is null
				// is when things got messed up because debugging prevented there being an active Bloom
				// form at a critical moment.
				//if (_previewWorker != null && EpubMaker != null)
				//{
				//	if (_desiredEpubSettings != newSettings || EpubMaker.Book != _bookSelection.CurrentSelection) {
				//		// Something changed before we even finished generating the preview! abort the current attempt, which will lead
				//		// to trying again. (If the current request is for the right book and state, just let it finish.)
				//		EpubMaker.AbortRequested = true;
				//	}
				//	return;
				//}

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
					ControlForInvoke.Invoke((Action) (() => PrepareToStageEpub()));
				}

				EpubMaker.PublishImageDescriptions = newSettings.howToPublishImageDescriptions;
				EpubMaker.RemoveFontSizes = newSettings.removeFontSizes;
				// clear the obsolete preview, if any; this also ensures that when the new one gets done,
				// we will really be changing the src attr in the preview iframe so the display will update.
				_webSocketServer.SendEvent(kWebsocketContext, kWebsocketEventId_Preview);
				_bookVersion = newVersion;
				ReportProgress(LocalizationManager.GetString("PublishTab.Epub.PreparingPreview", "Preparing Preview"));
				_previewSrc = UpdateEpubControlContent();
				if (EpubMaker.AbortRequested)
					return false; // the thread that set the abort flag will clean up.
				// Do pending save if the user requested it while the preview was still in progress.
				SaveAsEpub();
				ReportProgress(LocalizationManager.GetString("PublishTab.Epub.Done", "Done"));
				return true;
			}
		}
	}
}
