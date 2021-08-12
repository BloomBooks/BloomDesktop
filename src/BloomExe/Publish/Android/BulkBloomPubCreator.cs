using System.IO;
using System.Text;
using Bloom.Api;
using Bloom.Book;
using Bloom.CollectionTab;
using Bloom.MiscUI;
using Bloom.web;
using BloomTemp;
using Newtonsoft.Json;
using SIL.Extensions;
using SIL.IO;
using SIL.Program;

namespace Bloom.Publish.Android
{
	public class BulkBloomPubCreator
	{
		private readonly BookServer _bookServer;
		private readonly LibraryModel _libraryModel;
		private readonly BloomWebSocketServer _webSocketServer;

		public delegate BulkBloomPubCreator Factory(BookServer bookServer, LibraryModel collectionModel, BloomWebSocketServer webSocketServer);//autofac uses this

		public BulkBloomPubCreator(BookServer bookServer, LibraryModel libraryModel, BloomWebSocketServer webSocketServer)
		{
			_bookServer = bookServer;
			_libraryModel = libraryModel;
			_webSocketServer = webSocketServer;
		}
		public void PublishAllBooks(PublishToAndroidApi.BulkBloomPUBPublishSettings bulkSaveSettings)
		{
			BrowserProgressDialog.DoWorkWithProgressDialog(_webSocketServer, "Bulk Save BloomPubs",
				(progress, worker) =>
				{
					var dest = new TemporaryFolder("BloomPubs");
					progress.MessageWithoutLocalizing($"Creating files in {dest.FolderPath}...");
					if (bulkSaveSettings.makeBookshelfFile)
					{
						// see https://docs.google.com/document/d/1UUvwxJ32W2X5CRgq-TS-1HmPj7gCKH9Y9bxZKbmpdAI

						progress.MessageWithoutLocalizing($"Creating bloomshelf file...");
						
						// OK I know this looks lame but trust me, using jsconvert to make that trivial label array is way too verbose.
						var template =
							"{ 'label': [{ 'en': 'bookshelf-name'}], 'id': 'id-of-the-bookshelf', 'color': 'hex-color-value'}";
						var json = template.Replace('\'', '"')
							.Replace("bookshelf-name", bulkSaveSettings.bookshelfLabel.Replace('"','\''))
							.Replace("id-of-the-bookshelf", _libraryModel.CollectionSettings.DefaultBookshelf)
							.Replace("hex-color-value", bulkSaveSettings.bookshelfColor);
						var filename = _libraryModel.CollectionSettings.DefaultBookshelf +".bloomshelf";
						filename = filename.SanitizeFilename(' ',true);
						var bloomShelfPath = Path.Combine(dest.FolderPath,filename);
						RobustFile.WriteAllText(bloomShelfPath, json, Encoding.UTF8);
					}

					foreach (var bookInfo in _libraryModel.TheOneEditableCollection.GetBookInfos())
					{
						if (worker.CancellationPending)
						{
							progress.MessageWithoutLocalizing("Cancelled.");
							return true;
						}
						progress.MessageWithoutLocalizing($"Making BloomPUB for {bookInfo.QuickTitleUserDisplay}...", ProgressKind.Heading);
						var settings = AndroidPublishSettings.FromBookInfo(bookInfo);
						settings.DistributionTag = bulkSaveSettings.distributionTag;
						BloomPubMaker.CreateBloomPub(bookInfo, settings, dest.FolderPath, _bookServer, progress);
					}
					progress.MessageWithoutLocalizing("Done.",ProgressKind.Heading);
					Process.SafeStart(dest.FolderPath);
					// true means wait for the user, don't close automatically
					return true;
				});
		}
	}
}
