using System;
using System.Collections.Generic;
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
		// lock for threads that test or change EpubMaker == null, _stagingEpub, EpubMaker.AbortRequested,
		// and _saveAsPath.
		// Normally, every Bloom API request is isolated by a lock from every other. That is, there can't be
		// two api requests from JS land manipulating the DOM (or anything else) at the same time and causing
		// race conditions. So we don't need to worry about locks to guard against different server threads
		// entering this class at the same time.
		// That won't work for aborting an epub generation that is in progress, because the API call to do the
		// abort can't get the lock that is already held by the thread making the obsolete preview.
		// So, that handler is NOT locked relative to all other API calls, including the other ones in this class.
		// Also, it's annoying if the Save button doesn't respond until the preview is complete; so, since that
		// only accesses a couple of private variables in this class, we allow that one to run without
		// the lock and collect the destination file.
		// We want to hold a lock just enough to make sure there are no race conditions involving changes to
		// whether EpubMaker is null, whether we are staging it, and whether its abort flag has been set.
		// That's not a problem for the abort thread, which holds no other lock.
		// But what about the thread that holds the global API lock and is making the preview? If it seeks
		// the EpubMaker lock, we need to be sure that no other thread will ever claim the epubmaker lock
		// and then seek the global API lock. That's OK, because the only things that seek the EpubMaker lock
		// either already have the API lock or never seek it (i.e., this two unlocked handlers).
		// Make sure to only claim this lock in a thread that already holds the API lock or never seeks it.
		// We also need to be careful about this lock and the UI thread. We can't have something holding this
		// lock and waiting for the UI thread, while the UI thread is waiting for the lock. The convention for
		// ensuring this is that, except for the Save action which runs ON the UI thread, nothing that holds
		// this lock is allowed to use the UI thread.
		private object _epubMakerLock = new object();
		private bool _stagingEpub;

		// This goes out with our messages and, on the client side (typescript), messages are filtered
		// down to the context (usualy a screen) that requested them.
		private const string kWebsocketContext = "publish-epub";

		private const string kWebsocketEventId_epubReady = "newEpubReady";

		private string _lastDirectory; // where we saved the most recent previous epub, if any

		// Holds the path the user has selected to save the results of the preview until it is actually saved.
		// If the preview is complete at the time the user selects the file, it will hold it only very briefly
		// while the file is actually saved. If the preview is incomplete, the path remains set until the
		// preview is completed, and then the Save is completed.
		private string _pendingSaveAsPath;


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

		public void RegisterWithApiHandler(BloomApiHandler apiHandler)
		{
			apiHandler.RegisterEndpointHandler(kApiUrlPart + "save", request =>
			{
				{
					string suggestedName = string.Format("{0}-{1}.epub", Path.GetFileName(_bookSelection.CurrentSelection.FolderPath),
						_bookSelection.CurrentSelection.BookData.Language1.GetNameInLanguage("en"));
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
							lock (_epubMakerLock)
							{
								_pendingSaveAsPath = dlg.FileName;
								if (!_stagingEpub)
								{
									// we can do it right now. No need to check version etc., because anything
									// that will change the epub we want to save will immediately trigger a new
									// preview, and we will be staging it until we have it.
									SaveAsEpub();
								}
								// If we ARE in the middle of staging the epub...quite possible since this
								// handler is registered with permission to execute in parallel with other
								// API handlers, the user just has to click Save before the preview is finished...
								// then we need not do any more here. A call to SaveAsEpub at the end of the
								// preview generation process will pick up the pending request in _pendingSaveAsPath
								// and complete the Save.
							}

							ReportProgress(LocalizationManager.GetString("PublishTab.Epub.Done", "Done"));
							ReportAnalytics("Save ePUB");
						}
					}

					request.PostSucceeded();
				}
			}, true, false);

			apiHandler.RegisterEndpointHandler(kApiUrlPart + "epubSettings", request =>
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

			// The backend here was written with an enum that had two choices for how to publish descriptions, but we only ever
			// have used one of them so far in the UI. So this is a boolean api that converts to an enum underlying value.
			apiHandler.RegisterBooleanEndpointHandler(kApiUrlPart + "imageDescriptionSetting",
				request => request.CurrentBook.BookInfo.MetaData.Epub_HowToPublishImageDescriptions == BookInfo.HowToPublishImageDescriptions.OnPage,
				(request, onPage) =>
				{
					request.CurrentBook.BookInfo.MetaData.Epub_HowToPublishImageDescriptions = onPage
						? BookInfo.HowToPublishImageDescriptions.OnPage
						: BookInfo.HowToPublishImageDescriptions.None;
					request.CurrentBook.BookInfo.Save();
					var newSettings = _desiredEpubSettings.Clone();
					newSettings.howToPublishImageDescriptions = request.CurrentBook.BookInfo.MetaData.Epub_HowToPublishImageDescriptions;
					RefreshPreview(newSettings);
				},
				false);

			// Saving a checkbox setting that the user ticks to say "Use my E-reader's font sizes"
			apiHandler.RegisterBooleanEndpointHandler(kApiUrlPart + "removeFontSizesSetting",
				request => request.CurrentBook.BookInfo.MetaData.Epub_RemoveFontSizes,
				(request, booleanSetting) => {
					request.CurrentBook.BookInfo.MetaData.Epub_RemoveFontSizes = booleanSetting;
					request.CurrentBook.BookInfo.Save();
					var newSettings = _desiredEpubSettings.Clone();
					newSettings.removeFontSizes = booleanSetting;
					RefreshPreview(newSettings);
				},
				false);

			apiHandler.RegisterEndpointHandler(kApiUrlPart + "updatePreview", request =>
			{
				RefreshPreview(_desiredEpubSettings);
				request.PostSucceeded();
			}, false); // in fact, must NOT be on UI thread

			apiHandler.RegisterEndpointHandler(kApiUrlPart + "abortPreview", request =>
			{
				AbortMakingEpub();

				request.PostSucceeded();
			}, false, false);
		}

		public void AbortMakingEpub()
		{
			lock (_epubMakerLock)
			{
				if (EpubMaker != null && _stagingEpub)
				{
					// typically will cause some OTHER thread that is making the epub to wind up quickly.
					EpubMaker.AbortRequested = true;
				}
			}
		}

		private void RefreshPreview(EpubPublishUiSettings newSettings)
		{
			// We have seen some exceptions thrown during refresh that cause a pretty yellow
			// dialog box pop up informing the user, e.g., that the program couldn't find
			// "api/publish/epub/updatePreview".  Rather than confuse the user, we catch such
			// exceptions here and retry a limited number of times.
			// See https://issues.bloomlibrary.org/youtrack/issue/BL-6763.
			Exception exception = null;
			for (int i = 0; i < 3; ++i)
			{
				try
				{
					if (UpdatePreview(newSettings, true))
						_webSocketServer.SendString(kWebsocketContext, kWebsocketEventId_epubReady, _previewSrc);
					return;
				}
				catch (Exception e)
				{
					exception = e;	// the original stack trace is rather important for post mortem debugging!
				}
			}
			// Notify the user gently that updating the ePUB preview failed.
			NonFatalProblem.Report(ModalIf.None, PassiveIf.All, "Something went wrong while making the ePUB preview.",
				"Updating the ePUB preview failed: " + exception.Message, exception);
		}

		public void ReportAnalytics(string eventName)
		{
			Analytics.Track(eventName, new Dictionary<string, string>()
			{
				{"BookId", _bookSelection.CurrentSelection.ID},
				{"Country", _collectionSettings.Country}
			});
		}

		internal void PrepareToStageEpub()
		{
			lock (_epubMakerLock)
			{
				if (EpubMaker != null)
				{
					//it has state that we don't want to reuse, so make a new one
					EpubMaker.Dispose();
					EpubMaker = null;
				}

				EpubMaker = new EpubMaker(_thumbNailer, _bookServer);
			}

			EpubMaker.Book = _bookSelection.CurrentSelection;
			EpubMaker.Unpaginated = true; // Enhance: UI?
			EpubMaker.OneAudioPerPage = true;
		}

		internal string StagingDirectory { get { return EpubMaker.BookInStagingFolder; } }

		internal void SaveAsEpub()
		{
			lock (_epubMakerLock)
			{
				if (_pendingSaveAsPath == null)
					return;
				EpubMaker.ZipAndSaveEpub(_pendingSaveAsPath, _progress);
				_pendingSaveAsPath = null;
			}
		}

		public string UpdateEpubControlContent()
		{
			// Enhance: this could be optimized (but it will require changes to EpubMaker, it assumes it only stages once)
			PrepareToStageEpub();
			// Initialize the settings to affect the first epub preview.  See https://issues.bloomlibrary.org/youtrack/issue/BL-7316.
			GetEpubSettingsForCurrentBook(_desiredEpubSettings);
			EpubMaker.PublishImageDescriptions = _desiredEpubSettings.howToPublishImageDescriptions;
			EpubMaker.RemoveFontSizes = _desiredEpubSettings.removeFontSizes;
			return SetupEpubControlContent();
		}

		public string SetupEpubControlContent()
		{
			// This gets called on a background thread but one step needs to happen on the UI thread,
			// so the Maker needs a control to Invoke on. An Api class doesn't naturally have one to give it,
			// so we arrange that this class is given the Bloom main window by the PublishView when the preview
			// window first comes up. In production, this is roughly equivalent to just using
			// Form.ActiveForms.Last(), but that fails when debugging; this is more robust.
			EpubMaker.ControlForInvoke = ControlForInvoke;
			EpubMaker.StageEpub(_progress);
			if (StagingDirectory == null)
				return null; // aborted, hopefully already reported.

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
			// Note October 2018: upgraded to Readium 0.3.2. Doc indicates this definitely should support
			// using any url (that doesn't involve a cross-domain problem) in the ?epub= param.
			// So we could probably now rewrite this to not copy the Readium files over. Not sure it's worth the effort.
			DirectoryUtilities.CopyDirectoryContents(root, tempFolder);

			// Not sure if we will need this. The current UI does not appear to have a way to indicate whether
			// we have a talking book, a book without audio, or one that has audio but it is not being published.
			//var audioSituationClass = "noAudioAvailable";
			//if (EpubMaker.PublishWithoutAudio)
			//	audioSituationClass = "haveAudioButNotMakingTalkingBook";
			//else if (BookHasAudio)
			//	audioSituationClass = "isTalkingBook";

			var targetFile = Path.Combine(tempFolder, "index.html");

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
					{
						EpubMaker.SaveEpub(path, _progress);
						_webSocketServer.SendString(kWebsocketContext, kWebsocketEventId_epubReady, _previewSrc);
					}
				}
			} while (!succeeded && !EpubMaker.AbortRequested); // try until we get a complete epub, not interrupted by user changing something.
		}

		public bool UpdatePreview(EpubPublishUiSettings newSettings, bool force, WebSocketProgress progress = null)
		{
			_progress = progress ?? _standardProgress.WithL10NPrefix("PublishTab.Epub.");
			if (Program.RunningOnUiThread)
			{
				// There's some stuff inside this lock that has to run on the UI thread.
				// If we lock the UI thread here, we can deadlock the whole program.
				throw new ApplicationException(@"Must not attempt to make epubs on UI thread...will produce deadlocks");
			}

			lock (_epubMakerLock)
			{
				if (EpubMaker != null)
					EpubMaker.AbortRequested = false;
				_stagingEpub = true;
			}

			// For some unknown reason, if the accessibility window is showing, some of the browser navigation
			// that is needed to accurately determine which content is visible simply doesn't happen.
			// It would be disconcerting if it popped to the top after we close it and reopen it.
			// So, we just close the window if it is showing when we do this. See BL-7807.
			// Except that opening the Ace Checker tab invokes this code path in a way that works without the
			// deadlock (or whatever causes the failure).  This call can be detected by the progress argument not
			// being null.  The Refresh button on the AccessibilityCheckWindow also uses this code path in the
			// same way, so the next two lines also allow that Refresh button to work.  See BL-9341 for why
			// the original fix is inadequate.
			if (progress == null)
				AccessibilityChecker.AccessibilityCheckWindow.StaticClose();

			try
			{
				_webSocketServer.SendString(kWebsocketContext, "startingEbookCreation", _previewSrc);

				var htmlPath = _bookSelection.CurrentSelection.GetPathHtmlFile();
				var newVersion = Book.Book.MakeVersionCode(File.ReadAllText(htmlPath), htmlPath);
				bool previewIsAlreadyCurrent;
				lock (_epubMakerLock)
				{
					previewIsAlreadyCurrent = _desiredEpubSettings == newSettings && EpubMaker != null && newVersion == _bookVersion &&
												!EpubMaker.AbortRequested && !force;
				}

				if (previewIsAlreadyCurrent)
				{
					SaveAsEpub(); // just in case there's a race condition where we haven't already saved it.
					return true; // preview is already up to date.
				}

				_desiredEpubSettings = newSettings;

				// clear the obsolete preview, if any; this also ensures that when the new one gets done,
				// we will really be changing the src attr in the preview iframe so the display will update.
				_webSocketServer.SendEvent(kWebsocketContext, kWebsocketEventId_epubReady);
				_bookVersion = newVersion;
				ReportProgress(LocalizationManager.GetString("PublishTab.Epub.PreparingPreview", "Preparing Preview"));

				// This three-tries loop is an attempt to recover from a weird state the system sometimes gets into
				// where a browser won't navigate to a temporary page that the EpubMaker uses. I'm not sure it actually
				// helps, once the system gets into this state even a brand new browser seems to have the same problem.
				// Usually there will be no exception, and the loop breaks at the end of the first iteration.
				for (int i = 0; i < 3; i++)
				{
					try
					{
						if (!PublishHelper.InPublishTab)
						{
							return false;
						}
						_previewSrc = UpdateEpubControlContent();
					}
					catch (ApplicationException ex)
					{
						if (i >= 2)
							throw;
						ReportProgress("Something went wrong, trying again");
						continue;
					}

					break; // normal case, no exception
				}

				lock (_epubMakerLock)
				{
					if (EpubMaker.AbortRequested)
						return false; // the code that set the abort flag will request a new preview.
				}
			}
			finally
			{
				lock (_epubMakerLock)
				{
					_stagingEpub = false;
				}
			}

			// Do pending save if the user requested it while the preview was still in progress.
			SaveAsEpub();
			ReportProgress(LocalizationManager.GetString("PublishTab.Epub.Done", "Done"));
			return true;
		}
	}
}
