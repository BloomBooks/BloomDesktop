using System;
using System.ComponentModel;
using System.IO;
using System.Net;
using Bloom.Book;
using Bloom.Communication;
using L10NSharp;
using SIL.IO;
using SIL.Progress;
using SIL.Reporting;

namespace Bloom.Publish
{
	public class BloomReaderPublisher
	{
		public event EventHandler Connected;
		public event EventHandler ConnectionFailed;

		private readonly IProgress _progress;
		private readonly IAndroidDeviceUsbConnection _androidDeviceUsbConnection;
		private DeviceNotFoundReportType _previousDeviceNotFoundReportType;

		public BloomReaderPublisher(IProgress progress)
		{
			_progress = progress;
#if !__MonoCS__
			_androidDeviceUsbConnection = new AndroidDeviceUsbConnection();
#else
			_androidDeviceUsbConnection = new UnimplementedAndroidDeviceUsbConnection();
#endif
		}

		/// <summary>
		/// Attempt to connect to a device
		/// </summary>
		public void Connect()
		{
			try
			{
				_progress.WriteMessage(LocalizationManager.GetString("Publish.BloomReaderPublisher.LookingForDevice",
					"Looking for an Android device connected by USB cable and set up for MTP..."));

				_androidDeviceUsbConnection.OneReadyDeviceFound += OneReadyDeviceFound;
				_androidDeviceUsbConnection.OneReadyDeviceNotFound += OneReadyDeviceNotFound;

				var backgroundWorker = new BackgroundWorker();
				backgroundWorker.DoWork += (sender, args) => _androidDeviceUsbConnection.FindDevice();
				backgroundWorker.RunWorkerCompleted += (sender, args) =>
				{
					if (args.Error != null)
					{
						FailConnect(args.Error);
					}
				};
				backgroundWorker.RunWorkerAsync();
			}
			catch (Exception e)
			{
				FailConnect(e);
			}
		}

		public void CancelConnect()
		{
			_androidDeviceUsbConnection.StopFindingDevice();

			_androidDeviceUsbConnection.OneReadyDeviceFound -= OneReadyDeviceFound;
			_androidDeviceUsbConnection.OneReadyDeviceNotFound -= OneReadyDeviceNotFound;
		}

		private void FailConnect(Exception e)
		{
			var unableToConnectMessage = LocalizationManager.GetString("Publish.BloomReaderPublisher.UnableToConnect",
				"Unable to connect to any Android device which has Bloom Reader.");
			CancelConnect();
			_progress.WriteError(unableToConnectMessage);
			_progress.WriteError("\tTechnical details to share with the development team: " + e);
			Logger.WriteError(e);
			ConnectionFailed?.Invoke(this, new EventArgs());
		}

		private void OneReadyDeviceFound(object sender, EventArgs args)
		{
			_androidDeviceUsbConnection.OneReadyDeviceFound -= OneReadyDeviceFound;
			_androidDeviceUsbConnection.OneReadyDeviceNotFound -= OneReadyDeviceNotFound;

			_progress.WriteMessage(String.Format(LocalizationManager.GetString(
				"Publish.BloomReaderPublisher.Connected",
				"Connected to {0}...", "{0} is a device name"), _androidDeviceUsbConnection.GetDeviceName()));

			Connected?.Invoke(this, new EventArgs());
		}

		private void OneReadyDeviceNotFound(object sender, OneReadyDeviceNotFoundEventArgs eventArgs)
		{
			// Don't report the same thing over and over
			if (_previousDeviceNotFoundReportType == eventArgs.ReportType)
				return;

			_previousDeviceNotFoundReportType = eventArgs.ReportType;

			switch (eventArgs.ReportType)
			{
				case DeviceNotFoundReportType.NoDeviceFound:
					_progress.WriteWarning(LocalizationManager.GetString("Publish.BloomReaderPublisher.NoDeviceFound",
						"No device found. Still looking..."));
					break;
				case DeviceNotFoundReportType.NoBloomDirectory:
					_progress.WriteWarning(LocalizationManager.GetString("Publish.BloomReaderPublisher.DeviceWithoutBloomReader",
						"The following devices are connected but do not seem to have Bloom Reader installed."));
					foreach (var deviceName in eventArgs.DeviceNames)
						_progress.WriteWarning($"\t{deviceName}");
					break;
				case DeviceNotFoundReportType.MoreThanOneReadyDevice:
					_progress.WriteWarning(LocalizationManager.GetString("Publish.BloomReaderPublisher.MoreThanOne",
						"The following connected devices all have Bloom Reader installed. Please connect only one of these devices."));
					foreach (var deviceName in eventArgs.DeviceNames)
						_progress.WriteWarning($"\t{deviceName}");
					break;
			}
		}

		/// <summary>
		/// Attempt to send the book to the device
		/// </summary>
		/// <param name="book"></param>
		/// <returns>true if book was sent successfully</returns>
		public bool SendBook(Book.Book book)
		{
			try
			{
				var bookTitle = book.Title;
				_progress.WriteMessage(String.Format(LocalizationManager.GetString("Publish.BloomReaderPublisher.LookingForExisting",
					"Looking for an existing \"{0}\"...", "{0} is a book title"), bookTitle));
				var publishedFileName = bookTitle + BookCompressor.ExtensionForDeviceBloomBook;
				var bookExistsOnDevice = _androidDeviceUsbConnection.BookExists(publishedFileName);

				_progress.WriteMessage(String.Format(LocalizationManager.GetString("Publish.BloomReaderPublisher.PackagingBook",
					"Packaging \"{0}\" for use with Bloom Reader...", "{0} is a book title"), bookTitle));
				using (var bloomdTempFile = TempFile.WithFilenameInTempFolder(publishedFileName))
				{
					BookCompressor.CompressBookForDevice(bloomdTempFile.Path, book);

					if (bookExistsOnDevice)
						_progress.WriteMessage(String.Format(LocalizationManager.GetString("Publish.BloomReaderPublisher.ReplacingBook",
							"Replacing existing \"{0}\"...", "{0} is a book title"), bookTitle));
					else
						_progress.WriteMessage(String.Format(LocalizationManager.GetString("Publish.BloomReaderPublisher.SendingBook",
							"Sending \"{0}\" to your Android device...", "{0} is a book title"), bookTitle));
					_androidDeviceUsbConnection.SendBook(bloomdTempFile.Path);
				}

				if (_androidDeviceUsbConnection.BookExists(bookTitle + BookCompressor.ExtensionForDeviceBloomBook))
				{
					_progress.WriteMessage(String.Format(LocalizationManager.GetString(
						"Publish.BloomReaderPublisher.BookSent",
						"You can now read \"{0}\" in Bloom Reader!", "{0} is a book title"), bookTitle));
					return true;
				}
				FailSend(null);
				return false;
			}
			catch (Exception e)
			{
				FailSend(e);
				return false;
			}
		}

		private void FailSend(Exception e)
		{
			string generalFailureMessage = LocalizationManager.GetString("Publish.BloomReaderPublisher.FailureToSend",
				"An error occurred and the book was not sent to your Android device.");
			_progress.WriteError(generalFailureMessage);
			if (e != null)
			{
				_progress.WriteError("\tTechnical details to share with the development team: " + e);
				Logger.WriteError(e);
			}
		}

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
		public void SendBookToClientOnLocalSubNet(Book.Book book, string androidIpAddress)
		{
			var androidHttpAddress = "http://" + androidIpAddress + ":5914"; // must match BloomReader SyncServer._serverPort.
			_progress.WriteMessage($"Sending \"{book.Title}\" to device {androidIpAddress}");

			var publishedFileName = book.Title + BookCompressor.ExtensionForDeviceBloomBook;
			using (var bloomdTempFile = TempFile.WithFilenameInTempFolder(BookStorage.SanitizeNameForFileSystem(publishedFileName)))
			{
				BookCompressor.CompressBookForDevice(bloomdTempFile.Path, book);
				using (WebClient myClient = new WebClient())
				{
					myClient.UploadData(androidHttpAddress + "/putfile?path=" + Uri.EscapeDataString(book.Title) +
										BookCompressor.ExtensionForDeviceBloomBook, File.ReadAllBytes(bloomdTempFile.Path));
					myClient.UploadData(androidHttpAddress + "/notify?message=transferComplete", new byte[] {0});
				}
			}
			_progress.WriteMessage($"Finished sending \"{book.Title}\" to device {androidIpAddress}");
		}

		public const string ProtocolVersion = "1.0";
	}
}
