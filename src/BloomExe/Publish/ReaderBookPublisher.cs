using System;
using System.ComponentModel;
using Bloom.Book;
using Bloom.Communication;
using SIL.Progress;

namespace Bloom.Publish
{
	public class ReaderBookPublisher
	{
		public EventHandler Connected;

		private readonly IProgress _progress;
		private readonly AndroidDeviceUsbConnection _androidDeviceUsbConnection;
		private bool _moreThanOneReported;

		public ReaderBookPublisher(IProgress progress)
		{
			_progress = progress;
			_androidDeviceUsbConnection = new AndroidDeviceUsbConnection();
		}

		/// <summary>
		/// Attempt to connect to a device
		/// </summary>
		public void Connect()
		{
			var unableToConnectMessage = L10NSharp.LocalizationManager.GetString("Publish.ReaderBookPublisher.UnableToConnect",
				"Unable to connect to any Android device which has Bloom Reader.");
			try
			{
				_progress.WriteMessage(L10NSharp.LocalizationManager.GetString("Publish.ReaderBookPublisher.LookingForDevice",
					"Looking for an Android device connected by USB cable and set up for MTP..."));

				_androidDeviceUsbConnection.OneApplicableDeviceFound += OneApplicableDeviceFound;
				_androidDeviceUsbConnection.MoreThanOneApplicableDeviceFound += MoreThanOneApplicableDeviceFound;

				var backgroundWorker = new BackgroundWorker();
				backgroundWorker.DoWork += (sender, args) => _androidDeviceUsbConnection.FindDevice();
				backgroundWorker.RunWorkerCompleted += (sender, args) =>
				{
					if (args.Error != null)
					{
						_androidDeviceUsbConnection.StopFindingDevice();
						_progress.WriteError(unableToConnectMessage);
						SIL.Reporting.Logger.WriteError(args.Error);
					}
				};
				backgroundWorker.RunWorkerAsync();
			}
			catch (Exception e)
			{
				_androidDeviceUsbConnection.StopFindingDevice();
				_progress.WriteError(unableToConnectMessage);
				SIL.Reporting.Logger.WriteError(e);
			}
		}

		private void OneApplicableDeviceFound(object sender, EventArgs args)
		{
			_progress.WriteMessage(string.Format(L10NSharp.LocalizationManager.GetString(
				"Publish.ReaderBookPublisher.Connected",
				"Connected to {0}...", "{0} is a device name"), _androidDeviceUsbConnection.GetDeviceName()));

			EventHandler handler = Connected;
			handler?.Invoke(this, null);
		}

		private void MoreThanOneApplicableDeviceFound(object sender, MoreThanOneApplicableDeviceFoundEventArgs eventArgs)
		{
			if (_moreThanOneReported)
				return;

			_moreThanOneReported = true;

			_progress.WriteWarning(L10NSharp.LocalizationManager.GetString("Publish.ReaderBookPublisher.MoreThanOne",
				"The following connected devices all have Bloom Reader installed. Please connect only one of these devices."));
			foreach (var deviceName in eventArgs.DeviceNames)
			{
				_progress.WriteWarning($"\t{deviceName}");
			}
		}

		/// <summary>
		/// Attempt to send the book to the device
		/// </summary>
		/// <param name="book"></param>
		/// <returns>true if book was sent successfully</returns>
		public bool SendBook(Book.Book book)
		{
			string generalFailureMessage = L10NSharp.LocalizationManager.GetString("Publish.ReaderBookPublisher.FailureToSend",
				"An error occurred and the book was not sent to your Android device.");
			try
			{
				var bookTitle = book.Title;
				_progress.WriteMessage(string.Format(L10NSharp.LocalizationManager.GetString("Publish.ReaderBookPublisher.LookingForExisting",
					"Looking for an existing \"{0}\"...", "{0} is a book title"), bookTitle));
				var bookExistsOnDevice =
					_androidDeviceUsbConnection.BookExists(bookTitle + BookCompressor.ExtensionForDeviceBloomBook);

				_progress.WriteMessage(string.Format(L10NSharp.LocalizationManager.GetString("Publish.ReaderBookPublisher.PackagingBook",
					"Packaging \"{0}\" for use with Bloom Reader...", "{0} is a book title"), bookTitle));
				var bloomdPath = BookCompressor.CompressBookForDevice(book);

				if (bookExistsOnDevice)
					_progress.WriteMessage(string.Format(L10NSharp.LocalizationManager.GetString("Publish.ReaderBookPublisher.ReplacingBook",
						"Replacing existing \"{0}\"...", "{0} is a book title"), bookTitle));
				else
					_progress.WriteMessage(string.Format(L10NSharp.LocalizationManager.GetString("Publish.ReaderBookPublisher.SendingBook",
						"Sending \"{0}\" to your Android device...", "{0} is a book title"), bookTitle));
				_androidDeviceUsbConnection.SendBook(bloomdPath);

				if (_androidDeviceUsbConnection.BookExists(bookTitle + BookCompressor.ExtensionForDeviceBloomBook))
				{
					_progress.WriteMessage(string.Format(L10NSharp.LocalizationManager.GetString(
						"Publish.ReaderBookPublisher.BookSent",
						"You can now read \"{0}\" in Bloom Reader!", "{0} is a book title"), bookTitle));
					return true;
				}
				_progress.WriteError(generalFailureMessage);
				return false;
			}
			catch (Exception e)
			{
				_progress.WriteError(generalFailureMessage);
				SIL.Reporting.Logger.WriteError(e);
				return false;
			}
		}
	}
}
