#if !__MonoCS__
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using Bloom.Book;
using Bloom.web;
using SIL.Reporting;

namespace Bloom.Publish.BloomPub.usb
{
	public class UsbPublisher
	{
		private readonly BookServer _bookServer;
		public Action Stopped;

		private readonly WebSocketProgress _progress;
		private readonly AndroidDeviceUsbConnection _androidDeviceUsbConnection;
		private DeviceNotFoundReportType _previousDeviceNotFoundReportType;
		private BackgroundWorker _connectionHandler;
		protected string _lastPublishedBloomdSize;

		public UsbPublisher(WebSocketProgress progress, BookServer bookServer)
		{
			_bookServer = bookServer;
			_progress = progress.WithL10NPrefix("PublishTab.Android.Usb.Progress.");
			_androidDeviceUsbConnection = new AndroidDeviceUsbConnection();
		}

		/// <summary>
		/// Attempt to connect to a device
		/// </summary>
		/// <param name="book"></param>
		public void Connect(Book.Book book, Color backColor, BloomPubPublishSettings settings = null)
		{
			try
			{
				// Calls to this come from JavaScript, not sure they will always be on the UI thread.
				// Before I added this, I definitely saw race conditions with more than one thread trying
				// to figure out what was connected.
				lock (this)
				{
					PublishToBloomPubApi.CheckBookLayout(book, _progress);
					if (_connectionHandler != null)
					{
						// we're in an odd state...should only be able to click the button that calls this
						// while stopped.
						// Try to really get into the right state in case the user tries again.
						_androidDeviceUsbConnection.StopFindingDevice();
						return;
					}
					// Create this while locked...once we have it, can't enter the main logic of this method
					// on another thread.
					_connectionHandler = new BackgroundWorker();
				}
				_progress.Message(idSuffix: "LookingForDevice",
					comment: "This is a progress message; MTP is an acronym for the system that allows computers to access files on devices.",
					message: "Looking for an Android device connected by USB cable and set up for file transfer (MTP)...");

				_androidDeviceUsbConnection.OneReadyDeviceFound = HandleOneReadyDeviceFound;
				_androidDeviceUsbConnection.OneReadyDeviceNotFound = HandleOneReadyDeviceNotFound;
				// Don't suppress the first message after (re)starting.
				_previousDeviceNotFoundReportType = DeviceNotFoundReportType.Unknown;

				_connectionHandler.DoWork += (sender, args) => _androidDeviceUsbConnection.ConnectAndSendToOneDevice(book, backColor, settings);
				_connectionHandler.RunWorkerCompleted += (sender, args) =>
				{
					if (args.Error != null)
					{
						UsbFailConnect(args.Error);
					}
					_connectionHandler = null; // now OK to try to connect again.
				};
				_connectionHandler.RunWorkerAsync();
			}
			catch (Exception e)
			{
				UsbFailConnect(e);
			}
		}

		public void Stop(bool disposing)
		{
			if (!disposing)
				_progress.Message(idSuffix: "Stopped", message: "Stopped");
			_androidDeviceUsbConnection.StopFindingDevice();
		}

		private void UsbFailConnect(Exception e)
		{
			Stop(disposing: false);
			_progress.Message(idSuffix: "UnableToConnect",
				 message: "Unable to connect to any Android device.");

			_progress.MessageWithoutLocalizing("\tTechnical details to share with the development team: " + e, ProgressKind.Error);
			Logger.WriteError(e);
			Stopped();
		}

		private void HandleOneReadyDeviceFound(Book.Book book, Color backColor, BloomPubPublishSettings settings = null)
		{
			_progress.MessageWithParams(idSuffix: "Connected",
				message: "Connected to {0} via USB...",
				comment: "{0} is a the name of the device Bloom connected to",
				progressKind: ProgressKind.Progress,
				parameters: _androidDeviceUsbConnection.GetDeviceName());

			SendBookAsync(book, backColor, settings);
		}

		private void HandleOneReadyDeviceNotFound(DeviceNotFoundReportType reportType, List<string> deviceNames)
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
				case DeviceNotFoundReportType.MoreThanOneReadyDevice:
					_progress.Message(idSuffix: "MoreThanOne",
						message: "Please connect only one of the following devices.");
					foreach (var deviceName in deviceNames)
						_progress.MessageWithoutLocalizing($"\t{deviceName}");
					break;
			}
		}

		/// <summary>
		/// Attempt to send the book to the device
		/// </summary>
		public void SendBookAsync(Book.Book book, Color backColor, BloomPubPublishSettings settings = null)
		{
			try
			{
				var backgroundWorker = new BackgroundWorker();
				backgroundWorker.DoWork += (sender, args) => { SendBookDoWork(book, backColor, settings); };

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

		protected static string GetSizeOfBloomdFile(string pathToBloomdFile)
		{
			var size = 0.0m;
			if (!string.IsNullOrEmpty(pathToBloomdFile))
			{
				var info = new FileInfo(pathToBloomdFile);
				size = info.Length / 1048576m; // file length is in bytes and 1Mb = 1024 * 1024 bytes
			}
			return size.ToString("F1");
		}

		protected virtual void SendBookDoWork(Book.Book book, Color backColor, BloomPubPublishSettings settings = null)
		{
			PublishToBloomPubApi.SendBook(book, _bookServer,
				null, (publishedFileName, path) =>
				{
					_lastPublishedBloomdSize = GetSizeOfBloomdFile(path);
					_androidDeviceUsbConnection.SendBook(path);
				},
				_progress,
				(publishedFileName, bookTitle) =>
					_androidDeviceUsbConnection.BookExists(publishedFileName) ?
						_progress.GetTitleMessage("ReplacingBook", "Replacing existing \"{0}\"...", bookTitle) :
						_progress.GetTitleMessage("SendingBook", "Sending \"{0}\" to your Android device...", bookTitle),
				publishedFileName => _androidDeviceUsbConnection.BookExists(publishedFileName),
				backColor,
				settings:settings);
			PublishToBloomPubApi.ReportAnalytics("usb", book);
		}

		private void FailSendBook(Exception e)
		{
			if (IsDeviceFullOrHung(e))
			{
				SendOutOfStorageSpaceMessage(e);
			}
			else
			{
				_progress.Message(idSuffix: "FailureToSend",
					message: "An error occurred and the book was not sent to your Android device.",
					progressKind: ProgressKind.Error);
				if (e != null)
				{
					//intentionally not localizable (each of these strings costs effort by each translation team)
					_progress.MessageWithoutLocalizing("\tTechnical details to share with the development team: ", ProgressKind.Error);
					_progress.Exception(e);
					Logger.WriteError(e);
				}
				Stopped();
			}
		}

		protected const int HR_ERROR_HANDLE_DISK_FULL = unchecked((int)0x80070027);
		protected const int HR_ERROR_DISK_FULL = unchecked((int)0x80070070);
		protected const int HR_E_WPD_DEVICE_IS_HUNG = unchecked((int)0x802A0006);

		private static bool IsDeviceFullOrHung(Exception ex)
		{
			if (!(ex is IOException || ex is COMException))
				return false;

			return ex.HResult == HR_ERROR_HANDLE_DISK_FULL
			       || ex.HResult == HR_ERROR_DISK_FULL
			       || ex.HResult == HR_E_WPD_DEVICE_IS_HUNG;
		}

		private void SendOutOfStorageSpaceMessage(Exception e)
		{
			// {0} is the size of the book that Bloom is trying to copy over to the Android device.
			_progress.Message("DeviceOutOfSpace",
				string.Format("The device reported that it does not have enough space for this book. The book is {0} MB.",
					_lastPublishedBloomdSize ?? "of unknown"),
				ProgressKind.Error);
			Logger.WriteError(e);
			Stopped();
		}
	}
}
#endif
