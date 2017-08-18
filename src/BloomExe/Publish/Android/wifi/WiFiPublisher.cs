using System;
using System.IO;
using System.Net;
using Bloom.Book;
using SIL.IO;
using SIL.Progress;

namespace Bloom.Publish.Android.wifi
{

	/// <summary>
	/// Runs a service on the local net that advertises a book and then delivers it to Androids that request it
	/// </summary>
	public class WiFiPublisher
	{
		/// <summary>
		/// Send the book to a client over local network, typically WiFi (at least on Android end).
		/// This is currently called on the UDPListener thread.
		/// Enhance: if we spin off another thread to do the transfer, especially if we create the file
		/// and read it into memory once and share the content, we can probably serve multiple
		/// requesting devices much faster. Currently, we are only handling one request at a time,
		/// since we don't return to the listening loop until we finish this request.
		/// Haven't tested what will happen if the user switches away from the Android tab while a transfer
		/// is in progress. I _think_ the thread will continue and complete the request. Quitting Bloom
		/// is likely to leave the transfer incomplete.
		/// </summary>
		/// <param name="book"></param>
		/// <param name="androidIpAddress"></param>
		public static void SendBookToClientOnLocalSubNet(Book.Book book, string androidIpAddress, IProgress progress)
		{
			var androidHttpAddress = "http://" + androidIpAddress + ":5914"; // must match BloomReader SyncServer._serverPort.
			progress.WriteMessage($"Sending \"{book.Title}\" to device {androidIpAddress}");

			var publishedFileName = book.Title + BookCompressor.ExtensionForDeviceBloomBook;
			using (var bloomdTempFile = TempFile.WithFilenameInTempFolder(BookStorage.SanitizeNameForFileSystem(publishedFileName)))
			{
				BookCompressor.CompressBookForDevice(bloomdTempFile.Path, book);
				using (WebClient myClient = new WebClient())
				{
					myClient.UploadData(androidHttpAddress + "/putfile?path=" + Uri.EscapeDataString(book.Title) +
										BookCompressor.ExtensionForDeviceBloomBook, File.ReadAllBytes(bloomdTempFile.Path));
					myClient.UploadData(androidHttpAddress + "/notify?message=transferComplete", new byte[] { 0 });
				}
			}
			progress.WriteMessage($"Finished sending \"{book.Title}\" to device {androidIpAddress}");
		}

		public const string ProtocolVersion = "1.0";
	}
}
