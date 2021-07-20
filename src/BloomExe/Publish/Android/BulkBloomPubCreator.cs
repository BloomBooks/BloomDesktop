using Bloom.Api;
using Bloom.Book;
using Bloom.CollectionTab;
using Bloom.MiscUI;
using BloomTemp;
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
		public void PublishAllBooks()
		{
			BrowserProgressDialog.DoWorkWithProgressDialog(_webSocketServer, "Bulk Save BloomPubs",
				progress =>
				{
					var dest = new TemporaryFolder("BloomPubs");
					foreach (var bookInfo in _libraryModel.TheOneEditableCollection.GetBookInfos())
					{
						progress.MessageWithoutLocalizing($"Creating {bookInfo.FolderPath}...");
						BloomPubMaker.CreateBloomPub(bookInfo, dest.FolderPath, _bookServer, progress);
					}
					Process.SafeStart(dest.FolderPath);
					// true means wait for the user, don't close automatically
					return true;
				});
		}
	}
}
