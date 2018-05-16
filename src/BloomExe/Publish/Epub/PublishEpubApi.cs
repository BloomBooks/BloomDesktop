using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Bloom.Api;
using Bloom.Book;
using Bloom.Collection;
using DesktopAnalytics;
using L10NSharp;

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
		private NavigationIsolator _isolator;
		private BookThumbNailer _thumbNailer;
		private BookSelection _bookSelection;
		private CollectionSettings _collectionSettings;
		private BloomWebSocketServer _webSocketServer;

		private string _lastDirectory; // where we saved the most recent previous epub, if any
		// This constant must match the ID that is used for the listener set up in the React component ProgressBox
		private const string kWebsocketProgressId = "progress";

		public PublishView CurrentView { get; set; }

		public PublishEpubApi(BookThumbNailer thumbNailer, NavigationIsolator isolator, BookServer bookServer,
			BookSelection bookSelection, CollectionSettings collectionSettings, BloomWebSocketServer webSocketServer)
		{
			_thumbNailer = thumbNailer;
			_isolator = isolator;
			_bookServer = bookServer;
			_bookSelection = bookSelection;
			_collectionSettings = collectionSettings;
			_webSocketServer = webSocketServer;
		}

		// Message is presumed already localized.
		private void ReportProgress(string message)
		{
			ReportProgress(_webSocketServer, message);
		}

		public static void ReportProgress(BloomWebSocketServer server, string message)
		{
			server.Send(kWebsocketProgressId, message);
			// This seems to be necessary for the message to appear reasonably promptly.
			Application.DoEvents();
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
							if (CurrentView != null)
							{
								// Typically, the user has checked the current preview and Save can take advantage of all the work
								// that has already been done to produce it. If not, this waits until an up-to-date preview is ready.
								CurrentView.RequestPreviewOutput(maker =>
								{
									_epubMaker = maker;
									CompleteSave(savePath);
								});
							}
							else
							{
								// Keeping this prior code just in case, but I don't think it is currently used.
								_epubMaker = new EpubMaker(_thumbNailer, _isolator, _bookServer);
								_epubMaker.Book = _bookSelection.CurrentSelection;
								_epubMaker.Unpaginated = true; // Enhance: UI?
								_epubMaker.PublishImageDescriptions = GetPublishMode(request);
								ReportProgress(LocalizationManager.GetString("PublishTab.Epub.PreparingDoc", "Preparing document"));
								_epubMaker.StageEpub();
								CompleteSave(savePath);
							}

						}
					}

					request.PostSucceeded();
				}
			}, true);
			server.RegisterEndpointHandler(kApiUrlPart + "imageDescription", request =>
			{
				var mode = GetPublishMode(request);
				if (CurrentView != null)
					CurrentView.UpdatePreview(mode);
				request.PostSucceeded();
			}, true);
		}

		private void CompleteSave(string savePath)
		{
			ReportProgress(LocalizationManager.GetString("PublishTab.Epub.Saving", "Saving"));
			_epubMaker.FinishEpub(savePath);
			ReportProgress(LocalizationManager.GetString("PublishTab.Epub.Done", "Done"));
			ReportAnalytics("Save ePUB");
		}

		private static EpubMaker.ImageDescriptionPublishing GetPublishMode(ApiRequest request)
		{
			var howToPublishImageDescriptions = request.Parameters["publishImageDescription"];
			switch (howToPublishImageDescriptions)
			{
				case "none":
				default:
					return EpubMaker.ImageDescriptionPublishing.None;
				case "onPage":
					return EpubMaker.ImageDescriptionPublishing.OnPage;
				case "links":
					return EpubMaker.ImageDescriptionPublishing.Links;
			}
		}

		public void ReportAnalytics(string eventName)
		{
			Analytics.Track(eventName, new Dictionary<string, string>()
			{
				{"BookId", _bookSelection.CurrentSelection.ID},
				{"Country", _collectionSettings.Country}
			});
		}
	}
}
