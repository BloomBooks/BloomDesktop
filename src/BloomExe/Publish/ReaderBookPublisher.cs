using System;
using Bloom.Book;
using Bloom.Communication;
using SIL.Progress;

namespace Bloom.Publish
{
	public class ReaderBookPublisher
	{
		private readonly IProgress _progress;
		private readonly AndroidDeviceConnection _androidDeviceConnection;

		public ReaderBookPublisher(IProgress progress)
		{
			_progress = progress;
			_androidDeviceConnection = new AndroidDeviceConnection();
		}

		/// <summary>
		/// Attempt to connect to a device
		/// </summary>
		/// <returns>true if connection was successful</returns>
		public bool Connect()
		{
			var unableToConnectMessage = L10NSharp.LocalizationManager.GetString("Publish.ReaderBookPublisher.UnableToConnect",
				"Unable to connect to any Android device which has Bloom Reader.");
			try
			{
				_progress.WriteMessage(L10NSharp.LocalizationManager.GetString("Publish.ReaderBookPublisher.LookingForDevice",
					"Looking for an Android device connected by USB cable and set up for MTP..."));
				if (!_androidDeviceConnection.TryConnect())
				{
					_progress.WriteMessage(unableToConnectMessage);
					return false;
				}
				_progress.WriteMessage(string.Format(L10NSharp.LocalizationManager.GetString(
					"Publish.ReaderBookPublisher.Connected",
					"Connected to {0}...", "{0} is a device name"), _androidDeviceConnection.GetDeviceName()));

				return true;
			}
			catch (ApplicationException ae)
			{
				_progress.WriteError(ae.Message);
				return false;
			}
			catch (Exception e)
			{
				_progress.WriteError(unableToConnectMessage);
				return false;
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
					_androidDeviceConnection.BookExists(bookTitle + BookCompressor.ExtensionForDeviceBloomBook);

				_progress.WriteMessage(string.Format(L10NSharp.LocalizationManager.GetString("Publish.ReaderBookPublisher.PackagingBook",
					"Packaging \"{0}\" for use with Bloom Reader...", "{0} is a book title"), bookTitle));
				var bloomdPath = BookCompressor.CompressBookForDevice(book);

				if (bookExistsOnDevice)
					_progress.WriteMessage(string.Format(L10NSharp.LocalizationManager.GetString("Publish.ReaderBookPublisher.ReplacingBook",
						"Replacing existing \"{0}\"...", "{0} is a book title"), bookTitle));
				else
					_progress.WriteMessage(string.Format(L10NSharp.LocalizationManager.GetString("Publish.ReaderBookPublisher.SendingBook",
						"Sending \"{0}\" to your Android device...", "{0} is a book title"), bookTitle));
				_androidDeviceConnection.SendBook(bloomdPath);

				if (_androidDeviceConnection.BookExists(bookTitle + BookCompressor.ExtensionForDeviceBloomBook))
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
				return false;
			}
		}
	}
}
