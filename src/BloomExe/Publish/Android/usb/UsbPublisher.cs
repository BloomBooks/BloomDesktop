#if !__MonoCS__
using System;
using System.Collections.Generic;
using System.ComponentModel;
using Bloom.Book;
using Bloom.web;
using SIL.IO;
using SIL.Progress;
using SIL.Reporting;

namespace Bloom.Publish.Android.usb
{
	public class UsbPublisher
	{
		public Action Stopped;

		private readonly WebSocketProgress _progress;
		private readonly AndroidDeviceUsbConnection _androidDeviceUsbConnection;
		private DeviceNotFoundReportType _previousDeviceNotFoundReportType;

		public UsbPublisher(WebSocketProgress progress)
		{
			_progress = progress.WithL10NPrefix("PublishTab.Android.Usb.Progress.");
			_androidDeviceUsbConnection = new AndroidDeviceUsbConnection();
		}

		/// <summary>
		/// Attempt to connect to a device
		/// </summary>
		/// <param name="book"></param>
		public void Connect(Book.Book book)
		{
			try
			{
				_progress.Message(id: "LookingForDevice",
					message: "Looking for an Android device connected by USB cable and set up for MTP...",
					comment: "This is a progress message; MTP is an acronym for the system that allows computers to access files on devices.");

				_androidDeviceUsbConnection.OneReadyDeviceFound = HandleFoundAReadyDevice;
				_androidDeviceUsbConnection.OneReadyDeviceNotFound = HandeFoundOneNonReadyDevice;

				var backgroundWorker = new BackgroundWorker();
				backgroundWorker.DoWork += (sender, args) => _androidDeviceUsbConnection.ConnectAndSendToOneDevice(book);
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
			_progress.Message(id: "Stopped", message: "Stopped");
			_androidDeviceUsbConnection.StopFindingDevice();
		}

		private void UsbFailConnect(Exception e)
		{
			Stop();
			_progress.Message(id: "UnableToConnect",
				message: "Unable to connect to any Android device which has Bloom Reader.");

			_progress.ErrorWithoutLocalizing("\tTechnical details to share with the development team: " + e);
			Logger.WriteError(e);
			Stopped();
		}

		private void HandleFoundAReadyDevice(Book.Book book)
		{
			_progress.MessageWithParams(id: "Connected",
				message: "Connected to {0} via USB...",
				comment: "{0} is a the name of the device Bloom connected to",
				parameters: _androidDeviceUsbConnection.GetDeviceName());

			SendBookAsync(book);
		}

		private void HandeFoundOneNonReadyDevice(DeviceNotFoundReportType reportType, List<string> deviceNames)
		{
			// Don't report the same thing over and over
			if (_previousDeviceNotFoundReportType == reportType)
				return;

			_previousDeviceNotFoundReportType = reportType;

			switch (reportType)
			{
				case DeviceNotFoundReportType.NoDeviceFound:
					_progress.Message("NoDeviceFound", "No device found. Still looking...");
					break;
				case DeviceNotFoundReportType.NoBloomDirectory:
					// I made this "running" instead of "installed" because I'm assuming
					// we wouldn't get a bloom directory just from installing. We don't actually need it to be
					// running, but this keeps the instructions simple.
					_progress.Message(id: "DeviceWithoutBloomReader",
						message: "The following devices are connected but do not seem to have Bloom Reader running:");
					foreach (var deviceName in deviceNames)
						_progress.MessageWithoutLocalizing($"\t{deviceName}");
					break;
				case DeviceNotFoundReportType.MoreThanOneReadyDevice:
					_progress.Message(id: "MoreThanOne",
						message: "The following connected devices all have Bloom Reader installed. Please connect only one of these devices.");
					foreach (var deviceName in deviceNames)
						_progress.MessageWithoutLocalizing($"\t{deviceName}");
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
			_progress.MessageUsingTitle("LookingForExisting", "Looking for an existing \"{0}\"...", bookTitle);
			var publishedFileName = bookTitle + BookCompressor.ExtensionForDeviceBloomBook;
			var bookExistsOnDevice = _androidDeviceUsbConnection.BookExists(publishedFileName);

			_progress.MessageUsingTitle("PackagingBook", "Packaging \"{0}\" for use with Bloom Reader...", bookTitle);

			using (var bloomdTempFile = TempFile.WithFilenameInTempFolder(publishedFileName))
			{
				BookCompressor.CompressBookForDevice(bloomdTempFile.Path, book);

				if (bookExistsOnDevice)
					_progress.MessageUsingTitle("ReplacingBook", "Replacing existing \"{0}\"...", bookTitle);
				else
					_progress.MessageUsingTitle("SendingBook", "Sending \"{0}\" to your Android device...",  bookTitle);
				_androidDeviceUsbConnection.SendBook(bloomdTempFile.Path);
				PublishToAndroidApi.ReportAnalytics("usb", book);
			}

			if (_androidDeviceUsbConnection.BookExists(bookTitle + BookCompressor.ExtensionForDeviceBloomBook))
			{
				_progress.MessageUsingTitle("BookSent", "You can now read \"{0}\" in Bloom Reader!", bookTitle);
			}
			else
			{
				throw new ApplicationException("Book does not exist after write operation.");
			}
		}

		private void FailSendBook(Exception e)
		{
			_progress.Error(id: "FailureToSend",
				message: "An error occurred and the book was not sent to your Android device.");
			if (e != null)
			{
				//intentionally not localizable (each of these strings costs effort by each translation team)
				_progress.ErrorWithoutLocalizing("\tTechnical details to share with the development team: ");
				_progress.Exception(e);
				Logger.WriteError(e);
			}
			Stopped();
		}
	}
}
#endif