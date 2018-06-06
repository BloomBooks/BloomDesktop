using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Bloom.Api;
using Bloom.Book;
using Bloom.Collection;
using Bloom.Edit;
using Bloom.Publish.Epub;
using L10NSharp;
using Newtonsoft.Json;
using SIL.IO;

namespace Bloom.Publish
{
	/// <summary>
	/// This class provides backend support for the React component epubPublishUI.tsx, which implements the EPUB option
	/// in Bloom's Publish tab. It cooperates with EpubPublishUI, which implements direct support for the messages
	/// the React component sends to the server. The methods in this class are generally to do with interactions
	/// between the EpubPublishUI, the PublishView, the PublishModel, and the EpubMaker which actually produces
	/// the preview and the final epub document.
	/// Enhance: I'd like to encapsulate this better, especially make it not need access to the PublishModel.
	/// (That's why I've made various things it needs that the model has be passed to it rather than getting
	/// them directly.) Unfortunately the model's lastDirectory is supposed to be updated as well as read.
	/// If we got serious about testing this, and wanted better isolation,
	/// we could make an interface for reading and writing the last directory,
	/// and pass it the model in that form.
	/// </summary>
	public class EpubPublishPanel : HtmlPublishPanel
	{
		public EpubMaker EpubMaker { get; set; }

		public BookSelection BookSelection { get; set; }

		private BookServer _bookServer;
		private BookThumbNailer _thumbNailer;
		private PublishModel _model;
		private PublishView _view;

		// This constant must match the ID that is used for the listener set up in the React component EpubPreview
		private const string kWebsocketPreviewId = "epubPreview";

		private EpubPublishUiSettings _desiredEpubSettings = new EpubPublishUiSettings();
		private bool _needNewPreview; // Used when asked to update preview while in the middle of using the current one (e.g., to save it).
		private Action<EpubMaker> _doWhenPreviewComplete; // Something to do when the current preview is complete (e.g., save it)
		private BackgroundWorker _previewWorker;
		private string _previewSrc;

		public EpubPublishPanel(string pathToHtmlFile, PublishModel model,
			BookThumbNailer thumbNailer, BookServer bookServer) : base(NavigationIsolator._sTheOneNavigationIsolator, pathToHtmlFile)
		{
			_model = model;
			_bookServer = bookServer;
			_thumbNailer = thumbNailer;
		}

		public CollectionSettings CollectionSettings { get; set; }

		public BloomWebSocketServer WebSocketServer;

		protected override void Dispose(bool disposing)
		{
			base.Dispose(disposing);
			if (EpubMaker != null)
			{
				EpubMaker.Dispose();
				EpubMaker = null;
			}
		}

		public void Initialize()
		{
			bool firstTime = true;
			Browser.WebBrowser.DocumentCompleted += (sender, args) =>
			{
				// Wait until the document is sufficiently initialized to receive websocket broadcasts
				if (firstTime)
				{
					// We get multiple DocumentCompleted events, e.g., when setting a new preview.
					// Just do this the first time.
					firstTime = false;
					PublishEpubApi.ReportProgress(this, WebSocketServer,
						LocalizationManager.GetString("PublishTab.Epub.PreparingPreview", "Preparing Preview"));
				}
			};
			PrepareToStageEpub(); // let's get the epub maker and its browser created on the UI thread
			EpubMaker.PublishImageDescriptions = _desiredEpubSettings.howToPublishImageDescriptions;
			EpubMaker.RemoveFontSizes = _desiredEpubSettings.removeFontSizes;
			_previewWorker = new BackgroundWorker();
			_previewWorker.RunWorkerCompleted += _previewWorker_RunWorkerCompleted;
			_previewWorker.DoWork += (sender, args) => SetupEpubPreview();
			_previewWorker.RunWorkerAsync();
		}

		internal void PrepareToStageEpub()
		{
			if (EpubMaker != null)
			{
				//it has state that we don't want to reuse, so make a new one
				EpubMaker.Dispose();
				EpubMaker = null;
			}
			EpubMaker = new EpubMaker(_thumbNailer, NavigationIsolator._sTheOneNavigationIsolator, _bookServer);
			EpubMaker.Book = BookSelection.CurrentSelection;
			EpubMaker.Unpaginated = true; // Enhance: UI?
			EpubMaker.OneAudioPerPage = true;
		}

		internal string StagingDirectory { get { return EpubMaker.BookInStagingFolder; } }

		internal void SaveAsEpub()
		{
			using (var dlg = new DialogAdapters.SaveFileDialogAdapter())
			{
				if (!string.IsNullOrEmpty(_model.LastDirectory) && Directory.Exists(_model.LastDirectory))
					dlg.InitialDirectory = _model.LastDirectory;

				string suggestedName = string.Format("{0}-{1}.epub", Path.GetFileName(BookSelection.CurrentSelection.FolderPath),
					CollectionSettings.GetLanguage1Name("en"));
				dlg.FileName = suggestedName;
				dlg.Filter = "EPUB|*.epub";
				dlg.OverwritePrompt = true;
				if (DialogResult.OK == dlg.ShowDialog())
				{
					_model.LastDirectory = Path.GetDirectoryName(dlg.FileName);
					EpubMaker.FinishEpub(dlg.FileName);
					_model.ReportAnalytics("Save ePUB");
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
			EpubMaker.ControlForInvoke = this;
			EpubMaker.StageEpub();

			var fileLocator = BookSelection.CurrentSelection.GetFileLocator();
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

		internal string GetEpubState()
		{
			return JsonConvert.SerializeObject(_desiredEpubSettings);
		}

		public void UpdatePreview(EpubPublishUiSettings newSettings, bool retry)
		{
			lock (this)
			{
				if (_desiredEpubSettings == newSettings && !retry)
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

			EpubMaker.PublishImageDescriptions = newSettings.howToPublishImageDescriptions;
			EpubMaker.RemoveFontSizes = newSettings.removeFontSizes;
			// clear the obsolete preview, if any; this also ensures that when the new one gets done,
			// we will really be changing the src attr in the preview iframe so the display will update.
			WebSocketServer.Send(kWebsocketPreviewId, "");
			PublishEpubApi.ReportProgress(this, WebSocketServer,
				LocalizationManager.GetString("PublishTab.Epub.PreparingPreview", "Preparing Preview"));
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

			Invoke((Action)(() =>
			{
				WebSocketServer.Send(kWebsocketPreviewId, _previewSrc);
				PublishEpubApi.ReportProgress(this, WebSocketServer,
					LocalizationManager.GetString("PublishTab.Epub.Done", "Done"));
			}));
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

		private void SetupEpubPreview()
		{
			_model.DoAnyNeededAudioCompression();
			_previewSrc = SetupEpubControlContent();
		}
	}
}
