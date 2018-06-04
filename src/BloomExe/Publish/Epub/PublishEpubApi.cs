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
using Bloom.web.controllers;
using DesktopAnalytics;
using L10NSharp;
using Newtonsoft.Json;

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
			ReportProgress(CurrentView, _webSocketServer, message);
		}

		public static void ReportProgress(Control invokeControl, BloomWebSocketServer server, string message)
		{
			if (invokeControl != null && invokeControl.InvokeRequired)
			{
				invokeControl.Invoke((Action) (() => ReportProgressCore(server, message)));
			}
			else
			{
				ReportProgressCore(server, message);
			}
		}

		private static void ReportProgressCore(BloomWebSocketServer server, string message) {
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
						}
					}

					request.PostSucceeded();
				}
			}, true);

			server.RegisterEndpointHandler(kApiUrlPart + "epubSettings", request =>
			{
				if (request.HttpMethod == HttpMethods.Get)
				{
					request.ReplyWithJson(CurrentView.GetEpubState());
				} else { // post
					var settings = (EpubPublishUiSettings)JsonConvert.DeserializeObject(request.RequiredPostJson(), typeof(EpubPublishUiSettings));
					if (CurrentView != null)
						CurrentView.UpdatePreview(settings, false);
					request.PostSucceeded();
				}
			}, true);
		}

		private void CompleteSave(string savePath)
		{
			ReportProgress(LocalizationManager.GetString("PublishTab.Epub.Saving", "Saving"));
			_epubMaker.FinishEpub(savePath);
			ReportProgress(LocalizationManager.GetString("PublishTab.Epub.Done", "Done"));
			ReportAnalytics("Save ePUB");

			// Tell the accessibility checker about this new thing it can check
			AccessibilityCheckApi.SetEpubPath(savePath);
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
