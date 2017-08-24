using System;
using System.ComponentModel;
using Bloom.Book;
using Bloom.Communication;
using L10NSharp;
using SIL.IO;
using SIL.Progress;
using SIL.Reporting;

namespace Bloom.Publish.Android
{
	public class UsbPublisher
	{
		public Action Stopped;

		private readonly IProgress _progress;
		private readonly IAndroidDeviceUsbConnection _androidDeviceUsbConnection;
		private DeviceNotFoundReportType _previousDeviceNotFoundReportType;

		public UsbPublisher(IProgress progress)
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
				_progress.WriteMessage(LocalizationManager.GetString("Publish.Android.Usb.LookingForDevice",
					"Looking for an Android device connected by USB cable and set up for MTP..."));

				_androidDeviceUsbConnection.OneReadyDeviceFound += OneReadyDeviceFound;
				_androidDeviceUsbConnection.OneReadyDeviceNotFound += OneReadyDeviceNotFound;

				var backgroundWorker = new BackgroundWorker();
				backgroundWorker.DoWork += (sender, args) => _androidDeviceUsbConnection.FindDevice();
				backgroundWorker.RunWorkerCompleted += (sender, args) =>
				{
					if (args.Error != null)
					{
						UsbFailConnect(args.Error);
					}
				};
				backgroundWorker.RunWorkerAsync();
			}
			catch (Exception e)
			{
				UsbFailConnect(e);
			}
		}

		public void Stop()
		{
			_progress.WriteMessage("Stopped");
			_androidDeviceUsbConnection.StopFindingDevice();
			_androidDeviceUsbConnection.OneReadyDeviceFound -= OneReadyDeviceFound;
			_androidDeviceUsbConnection.OneReadyDeviceNotFound -= OneReadyDeviceNotFound;
		}

		private void UsbFailConnect(Exception e)
		{
			var unableToConnectMessage = LocalizationManager.GetString("Publish.Android.Usb.UnableToConnect",
				"Unable to connect to any Android device which has Bloom Reader.");
			Stop();
			_progress.WriteError(unableToConnectMessage);
			_progress.WriteError("\tTechnical details to share with the development team: " + e);
			Logger.WriteError(e);
			Stopped();
		}

		private void OneReadyDeviceFound(object sender, EventArgs args)
		{
			_androidDeviceUsbConnection.OneReadyDeviceFound -= OneReadyDeviceFound;
			_androidDeviceUsbConnection.OneReadyDeviceNotFound -= OneReadyDeviceNotFound;

			_progress.WriteMessage(String.Format(LocalizationManager.GetString(
				"Publish.Android.Usb.UsbConnected",
				"UsbConnected to {0}...", "{0} is a device name"), _androidDeviceUsbConnection.GetDeviceName()));

			Stopped();
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
					_progress.WriteWarning(LocalizationManager.GetString("Publish.Android.Usb.NoDeviceFound",
						"No device found. Still looking..."));
					break;
				case DeviceNotFoundReportType.NoBloomDirectory:
					_progress.WriteWarning(LocalizationManager.GetString("Publish.Android.Usb.DeviceWithoutBloomReader",
						// I made this "running" instead of "installed" because I'm assuming
						// we wouldn't get a bloom directory just from installing. We don't actually need it to be
						// running, but this keeps the instructions simple.
						"The following devices are connected but do not seem to have Bloom Reader running:"));
					foreach (var deviceName in eventArgs.DeviceNames)
						_progress.WriteWarning($"\t{deviceName}");
					break;
				case DeviceNotFoundReportType.MoreThanOneReadyDevice:
					_progress.WriteWarning(LocalizationManager.GetString("Publish.Android.Usb.MoreThanOne",
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
		public void SendBookAsync(Book.Book book)
		{
			try
			{
				var backgroundWorker = new BackgroundWorker();
				backgroundWorker.DoWork += (sender, args) => { SendBookDoWork(book); };

				backgroundWorker.RunWorkerCompleted += (sender, args) =>
				{
					if (args.Error != null)
						FailSendBook(args.Error);
					else
						Stopped();
				};
				backgroundWorker.RunWorkerAsync();
			}
			catch (Exception e)
			{
				FailSendBook(e);
			}
		}

		private void SendBookDoWork(Book.Book book)
		{
			var bookTitle = book.Title;
			_progress.WriteMessage(String.Format(LocalizationManager.GetString(
				"Publish.Android.Usb.LookingForExisting",
				"Looking for an existing \"{0}\"...", "{0} is a book title"), bookTitle));
			var publishedFileName = bookTitle + BookCompressor.ExtensionForDeviceBloomBook;
			var bookExistsOnDevice = _androidDeviceUsbConnection.BookExists(publishedFileName);

			_progress.WriteMessage(String.Format(LocalizationManager.GetString("Publish.Android.Usb.PackagingBook",
				"Packaging \"{0}\" for use with Bloom Reader...", "{0} is a book title"), bookTitle));
			using (var bloomdTempFile = TempFile.WithFilenameInTempFolder(publishedFileName))
			{
				BookCompressor.CompressBookForDevice(bloomdTempFile.Path, book);

				if (bookExistsOnDevice)
					_progress.WriteMessage(String.Format(LocalizationManager.GetString("Publish.Android.Usb.ReplacingBook",
						"Replacing existing \"{0}\"...", "{0} is a book title"), bookTitle));
				else
					_progress.WriteMessage(String.Format(LocalizationManager.GetString("Publish.Android.Usb.SendingBook",
						"Sending \"{0}\" to your Android device...", "{0} is a book title"), bookTitle));
				_androidDeviceUsbConnection.SendBook(bloomdTempFile.Path);
			}

			if (_androidDeviceUsbConnection.BookExists(bookTitle + BookCompressor.ExtensionForDeviceBloomBook))
			{
				_progress.WriteMessage(String.Format(LocalizationManager.GetString(
					"Publish.Android.Usb.BookSent",
					"You can now read \"{0}\" in Bloom Reader!", "{0} is a book title"), bookTitle));
			}
			else
			{
				throw new ApplicationException("Book does not exist after write operation.");
			}
		}

		private void FailSendBook(Exception e)
		{
			var generalFailureMessage = LocalizationManager.GetString("Publish.Android.Usb.FailureToSend",
				"An error occurred and the book was not sent to your Android device.");
			_progress.WriteError(generalFailureMessage);
			if (e != null)
			{
				_progress.WriteError("\tTechnical details to share with the development team: " + e);
				Logger.WriteError(e);
			}
			Stopped();
		}
	}
}
