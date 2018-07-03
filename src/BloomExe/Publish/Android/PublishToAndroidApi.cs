using System;
using System.Collections.Generic;
using System.Drawing;
using System.Diagnostics;
using System.Linq;
using Bloom.Api;
using Bloom.Book;
using Bloom.ImageProcessing;
using Bloom.Properties;
using Bloom.Publish.Android.file;
using SIL.Windows.Forms.Miscellaneous;

#if !__MonoCS__
using Bloom.Publish.Android.usb;
#endif
using Bloom.Publish.Android.wifi;
using Bloom.web;
using DesktopAnalytics;
using SIL.IO;

namespace Bloom.Publish.Android
{
	/// <summary>
	/// Handles api request dealing with the publishing of books to an Android device
	/// </summary>
	public class PublishToAndroidApi
	{
		private const string kApiUrlPart = "publish/android/";
		private const string kWebsocketState_EventId = "publish/android/state";
		private readonly WiFiPublisher _wifiPublisher;
#if !__MonoCS__
		private readonly UsbPublisher _usbPublisher;
#endif
		private readonly BloomWebSocketServer _webSocketServer;
		private readonly BookServer _bookServer;
		private readonly WebSocketProgress _progress;
		private const string kWebSocketContext = "publish-android"; // must match what is in AndroidPublish.tsx
		private Color _thumbnailBackgroundColor = Color.Transparent; // can't be actual book cover color <--- why not?
		private Book.Book _coverColorSourceBook;

		private RuntimeImageProcessor _imageProcessor;

		public PublishToAndroidApi(BloomWebSocketServer bloomWebSocketServer, BookServer bookServer, RuntimeImageProcessor imageProcessor)
		{
			_webSocketServer = bloomWebSocketServer;
			_bookServer = bookServer;
			_imageProcessor = imageProcessor;
			_progress = new WebSocketProgress(_webSocketServer, kWebSocketContext);
			_wifiPublisher = new WiFiPublisher(_progress, _bookServer);
#if !__MonoCS__
			_usbPublisher = new UsbPublisher(_progress, _bookServer)
			{
				Stopped = () => SetState("stopped")
			};
#endif
		}

		private static string ToCssColorString(System.Drawing.Color c)
		{
			return "#" + c.R.ToString("X2") + c.G.ToString("X2") + c.B.ToString("X2");
		}

		private static bool TryCssColorFromString(string input, out Color result)
		{
			result = Color.White; // some default in case of error.
			if (!input.StartsWith("#") || input.Length != 7)
				return false; // arbitrary failure
			try
			{
				result = ColorTranslator.FromHtml(input);
			}
			catch (Exception e)
			{
				return false;
			}
			return true;
		}

		public void RegisterWithServer(EnhancedImageServer server)
		{
			// This is just for storing the user preference of method
			// If we had a couple of these, we could just have a generic preferences api
			// that browser-side code could use.
			server.RegisterEndpointHandler(kApiUrlPart + "method", request =>
			{
				if(request.HttpMethod == HttpMethods.Get)
				{
					var method = Settings.Default.PublishAndroidMethod;
					if(!new string[]{"wifi", "usb", "file"}.Contains(method))
					{
						method = "wifi";
					}
					request.ReplyWithText(method);
				}
				else // post
				{
					Settings.Default.PublishAndroidMethod = request.RequiredPostString();
#if __MonoCS__
					if (Settings.Default.PublishAndroidMethod == "usb")
					{
						_progress.MessageWithoutLocalizing("Sorry, this method is not available on Linux yet.");
					}
#endif
					request.PostSucceeded();
				}
			}, true);

			server.RegisterEndpointHandler(kApiUrlPart + "backColor", request =>
			{
				if (request.HttpMethod == HttpMethods.Get)
				{
					if (request.CurrentBook != _coverColorSourceBook)
					{
						_coverColorSourceBook = request.CurrentBook;
						TryCssColorFromString(request.CurrentBook?.GetCoverColor()??"", out _thumbnailBackgroundColor);
					}
					request.ReplyWithText(ToCssColorString(_thumbnailBackgroundColor));
				}
				else // post
				{
					// ignore invalid colors (very common while user is editing hex)
					Color newColor;
					var newColorAsString = request.RequiredPostString();
					if (TryCssColorFromString(newColorAsString, out newColor))
					{
						_thumbnailBackgroundColor = newColor;
						request.CurrentBook.SetCoverColor(newColorAsString);
					}
					request.PostSucceeded();
				}
			}, true);


			server.RegisterEndpointHandler(kApiUrlPart + "motionBookMode", request =>
			{
				if (request.HttpMethod == HttpMethods.Get)
				{
					// this is temporary, just trying to get support for full screen pan & zoom out quickly in 4.2
					request.ReplyWithText(request.CurrentBook.UseMotionModeInBloomReader.ToString()
						.ToLowerInvariant());  // "false", not "False"
				}
				else // post
				{
					request.CurrentBook.UseMotionModeInBloomReader = bool.Parse(request.RequiredPostString());
					request.PostSucceeded();
				}
			}, true);
			

			server.RegisterEndpointHandler(kApiUrlPart + "thumbnail", request =>
			{
				var coverImage = request.CurrentBook.GetCoverImagePath();
				if (coverImage == null)
					request.Failed("no cover image");
				else
				{
					// We don't care as much about making it resized as making its background transparent.
					using(var thumbnail = TempFile.CreateAndGetPathButDontMakeTheFile())
					{
						if(_thumbnailBackgroundColor == Color.Transparent)
						{
							TryCssColorFromString(request.CurrentBook?.GetCoverColor(), out _thumbnailBackgroundColor);
						}
						RuntimeImageProcessor.GenerateEBookThumbnail(coverImage, thumbnail.Path, 256, 256, _thumbnailBackgroundColor);
						request.ReplyWithImage( thumbnail.Path);
					}
				}
			}, true);

			server.RegisterEndpointHandler(kApiUrlPart + "usb/start", request =>
			{
#if !__MonoCS__
				SetState("UsbStarted");
				_usbPublisher.Connect(request.CurrentBook, _thumbnailBackgroundColor);
#endif
				request.PostSucceeded();
			}, true);

			server.RegisterEndpointHandler(kApiUrlPart + "usb/stop", request =>
			{
#if !__MonoCS__
				_usbPublisher.Stop();
				SetState("stopped");
#endif
				request.PostSucceeded();
			}, true);
			server.RegisterEndpointHandler(kApiUrlPart + "wifi/start", request =>
			{
				_wifiPublisher.Start(request.CurrentBook, request.CurrentCollectionSettings, _thumbnailBackgroundColor);
				SetState("ServingOnWifi");
				request.PostSucceeded();
			}, true);

			server.RegisterEndpointHandler(kApiUrlPart + "wifi/stop", request =>
			{
				_wifiPublisher.Stop();
				SetState("stopped");
				request.PostSucceeded();
			}, true);

			server.RegisterEndpointHandler(kApiUrlPart + "file/save", request =>
			{
				FilePublisher.Save(request.CurrentBook, _bookServer, _thumbnailBackgroundColor, _progress);
				SetState("stopped");
				request.PostSucceeded();
			}, true);

			server.RegisterEndpointHandler(kApiUrlPart + "cleanup", request =>
			{
				Stop();
				request.PostSucceeded();
			}, true);

			server.RegisterEndpointHandler(kApiUrlPart + "textToClipboard", request =>
			{
				PortableClipboard.SetText(request.RequiredPostString());
				request.PostSucceeded();
			}, true);
		}

		public void Stop()
		{
#if !__MonoCS__
			_usbPublisher.Stop();
#endif
			_wifiPublisher.Stop();
			SetState("stopped");
		}

		private void SetState(string state)
		{
			_webSocketServer.SendString(kWebSocketContext, kWebsocketState_EventId, state);
		}

		public static void ReportAnalytics(string mode, Book.Book book)
		{
			Analytics.Track("Publish Android", new Dictionary<string, string>() {{"mode", mode}, {"BookId", book.ID}, { "Country", book.CollectionSettings.Country}, {"Language", book.CollectionSettings.Language1Iso639Code}});
		}

		/// <summary>
		/// This is the core of sending a book to a device. We need a book and a bookServer in order to come up
		/// with the .bloomd file.
		/// We are either simply saving the .bloomd to destFileName, or else we will make a temporary .bloomd file and
		/// actually send it using sendAction.
		/// We report important progress on the progress control. This includes reporting that we are starting
		/// the actual transmission using startingMessageAction, which is passed the safe file name (for checking pre-existence
		/// in UsbPublisher) and the book title (typically inserted into the message).
		/// If a confirmAction is passed (currently only by UsbPublisher), we use it check for a successful transfer
		/// before reporting completion (except for file save, where the current message is inappropriate).
		/// This is an awkward case where the three ways of publishing are similar enough that
		/// it's annoying and dangerous to have three entirely separate methods but somewhat awkward to combine them.
		/// Possibly we could eventually make them more similar, e.g., it would simplify things if they all said
		/// "Sending X to Y", though I'm not sure that would be good i18n if Y is sometimes a device name
		/// and sometimes a path.
		/// </summary>
		/// <param name="book"></param>
		/// <param name="destFileName"></param>
		/// <param name="sendAction"></param>
		/// <param name="progress"></param>
		/// <param name="bookServer"></param>
		/// <param name="startingMessageFunction"></param>
		public static void SendBook(Book.Book book, BookServer bookServer, string destFileName, Action<string, string> sendAction, WebSocketProgress progress, Func<string, string, string> startingMessageFunction,
			Func<string, bool> confirmFunction, Color backColor)
		{
			var bookTitle = book.Title;
			progress.MessageUsingTitle("PackagingBook", "Packaging \"{0}\" for use with Bloom Reader...", bookTitle);

			// compress audio if needed, with progress message
			if (AudioProcessor.IsAnyCompressedAudioMissing(book.FolderPath, book.RawDom))
			{
				progress.Message("CompressingAudio", "Compressing audio files");
				AudioProcessor.TryCompressingAudioAsNeeded(book.FolderPath, book.RawDom);
			}
			var publishedFileName = BookStorage.SanitizeNameForFileSystem(bookTitle) + BookCompressor.ExtensionForDeviceBloomBook;
			if (startingMessageFunction != null)
				progress.MessageWithoutLocalizing(startingMessageFunction(publishedFileName, bookTitle));
			if (destFileName == null)
			{
				// wifi or usb...make the .bloomd in a temp folder.
				using (var bloomdTempFile = TempFile.WithFilenameInTempFolder(publishedFileName))
				{
					BookCompressor.CompressBookForDevice(bloomdTempFile.Path, book, bookServer, backColor, progress);
					sendAction(publishedFileName, bloomdTempFile.Path);
					if (confirmFunction != null && !confirmFunction(publishedFileName))
						throw new ApplicationException("Book does not exist after write operation.");
					progress.MessageUsingTitle("BookSent", "You can now read \"{0}\" in Bloom Reader!", bookTitle);
				}
			} else
			{
				// save file...user has supplied name, there is no further action.
				Debug.Assert(sendAction == null, "further actions are not supported when passing a path name");
				BookCompressor.CompressBookForDevice(destFileName, book, bookServer, backColor, progress);
			}

		}

		/// <summary>
		/// Check for either "Device16x9Portrait" or "Device16x9Landscape" layout.
		/// Complain to the user if another layout is currently chosen.
		/// </summary>
		/// <remarks>
		/// See https://issues.bloomlibrary.org/youtrack/issue/BL-5274.
		/// </remarks>
		public static void CheckBookLayout(Bloom.Book.Book book, Bloom.web.WebSocketProgress progress)
		{
			var layout = book.GetLayout();
			var desiredLayoutSize = "Device16x9";
			if (layout.SizeAndOrientation.PageSizeName != desiredLayoutSize)
			{
				// The progress object has been initialized to use an id prefix.  So we'll access L10NSharp explicitly here.  We also want to make the string blue,
				// which requires a special argument.
				var msgFormat = L10NSharp.LocalizationManager.GetString("PublishTab.Android.WrongLayout.Message",
					"The layout of this book is currently \"{0}\". Bloom Reader will display it using \"{1}\", so text might not fit. To see if anything needs adjusting, go back to the Edit Tab and change the layout to \"{1}\".",
					"{0} and {1} are book layout tags.");
				var desiredLayout = desiredLayoutSize + layout.SizeAndOrientation.OrientationName;
				var msg = String.Format(msgFormat, layout.SizeAndOrientation.ToString(), desiredLayout, Environment.NewLine);
				progress.MessageWithStyleWithoutLocalizing(msg, "color:blue");
			}
		}
	}
}
