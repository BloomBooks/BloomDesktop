using System;
using System.Collections.Generic;
using System.Drawing;
using System.Diagnostics;
using System.IO;
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
using BloomTemp;
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
		private const string kWebSocketContext = "publish-android"; // must match what is in AndroidPublishUI.tsx
		private Color _thumbnailBackgroundColor = Color.Transparent; // can't be actual book cover color <--- why not?
		private Book.Book _coverColorSourceBook;

		private object _lockForLanguages = new object();
		private Dictionary<string, bool> _allLanguages;
		private HashSet<string> _languagesToPublish = new HashSet<string>();
		private Bloom.Book.Book _bookForLanguagesToPublish = null;

		private RuntimeImageProcessor _imageProcessor;

		// This constant must match the ID that is used for the listener set up in the React component AndroidPublishUI
		private const string kWebsocketEventId_Preview = "androidPreview";
		public const string StagingFolder = "PlaceForStagingBook";

		public static string PreviewUrl { get; set; }

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

		private AndroidPublishSettings GetSettings()
		{
			// We need a copy of the hashset, so that if _languagesToPublish changes, this settings object won't.
			return new AndroidPublishSettings() {LanguagesToInclude = new HashSet<string>(_languagesToPublish)};
		}

		public void RegisterWithApiHandler(BloomApiHandler apiHandler)
		{
			// This is just for storing the user preference of method
			// If we had a couple of these, we could just have a generic preferences api
			// that browser-side code could use.
			apiHandler.RegisterEndpointHandler(kApiUrlPart + "method", request =>
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

			apiHandler.RegisterEndpointHandler(kApiUrlPart + "backColor", request =>
			{
				if (request.HttpMethod == HttpMethods.Get)
				{
					if (request.CurrentBook != _coverColorSourceBook)
					{
						_coverColorSourceBook = request.CurrentBook;
						ImageUtils.TryCssColorFromString(request.CurrentBook?.GetCoverColor()??"", out _thumbnailBackgroundColor);
					}
					request.ReplyWithText(ToCssColorString(_thumbnailBackgroundColor));
				}
				else // post
				{
					// ignore invalid colors (very common while user is editing hex)
					Color newColor;
					var newColorAsString = request.RequiredPostString();
					if (ImageUtils.TryCssColorFromString(newColorAsString, out newColor))
					{
						_thumbnailBackgroundColor = newColor;
						request.CurrentBook.SetCoverColor(newColorAsString);
					}
					request.PostSucceeded();
				}
			}, true);

			apiHandler.RegisterBooleanEndpointHandler(kApiUrlPart + "motionBookMode",
				readRequest =>
				{
					// If the user has taken off all possible motion, force not having motion in the
					// Bloom Reader book.  See https://issues.bloomlibrary.org/youtrack/issue/BL-7680.
					if (!readRequest.CurrentBook.HasMotionPages)
						readRequest.CurrentBook.MotionMode = false;
					return readRequest.CurrentBook.MotionMode;
				},
				(writeRequest, value) =>
				{
					writeRequest.CurrentBook.MotionMode = value;
				}
			, true);

			apiHandler.RegisterEndpointHandler(kApiUrlPart + "updatePreview", request =>
			{
				if (request.HttpMethod == HttpMethods.Post)
				{
					// This is already running on a server thread, so there doesn't seem to be any need to kick off
					// another background one and return before the preview is ready. But in case something in C#
					// might one day kick of a new preview, or we find we do need a background thread,
					// I've made it a websocket broadcast when it is ready.
					// If we've already left the publish tab...we can get a few of these requests queued up when
					// a tester rapidly toggles between views...abandon the attempt
					if (!PublishHelper.InPublishTab)
					{
						request.Failed("aborted, no longer in publish tab");
						return;
					}
					try
					{
						UpdatePreview(request);
						request.PostSucceeded();
					}
					catch (Exception e)
					{
						request.Failed("Error while updating preview. Message: " + e.Message);
						NonFatalProblem.Report(ModalIf.Alpha, PassiveIf.All, "Error while updating preview.", null, e, true);
					}
				}
			}, false);


			apiHandler.RegisterEndpointHandler(kApiUrlPart + "thumbnail", request =>
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
							ImageUtils.TryCssColorFromString(request.CurrentBook?.GetCoverColor(), out _thumbnailBackgroundColor);
						}
						RuntimeImageProcessor.GenerateEBookThumbnail(coverImage, thumbnail.Path, 256, 256, _thumbnailBackgroundColor);
						request.ReplyWithImage( thumbnail.Path);
					}
				}
			}, true);

			apiHandler.RegisterEndpointHandler(kApiUrlPart + "usb/start", request =>
			{
#if !__MonoCS__

				SetState("UsbStarted");
				UpdatePreviewIfNeeded(request);
				_usbPublisher.Connect(request.CurrentBook, _thumbnailBackgroundColor, GetSettings());
#endif
				request.PostSucceeded();
			}, true);

			apiHandler.RegisterEndpointHandler(kApiUrlPart + "usb/stop", request =>
			{
#if !__MonoCS__
				_usbPublisher.Stop();
				SetState("stopped");
#endif
				request.PostSucceeded();
			}, true);
			apiHandler.RegisterEndpointHandler(kApiUrlPart + "wifi/start", request =>
			{
				SetState("ServingOnWifi");
				UpdatePreviewIfNeeded(request);
				_wifiPublisher.Start(request.CurrentBook, request.CurrentCollectionSettings, _thumbnailBackgroundColor, GetSettings());
				
				request.PostSucceeded();
			}, true);

			apiHandler.RegisterEndpointHandler(kApiUrlPart + "wifi/stop", request =>
			{
				_wifiPublisher.Stop();
				SetState("stopped");
				request.PostSucceeded();
			}, true);

			apiHandler.RegisterEndpointHandler(kApiUrlPart + "file/save", request =>
			{
				UpdatePreviewIfNeeded(request);
				FilePublisher.Save(request.CurrentBook, _bookServer, _thumbnailBackgroundColor, _progress, GetSettings());
				SetState("stopped");
				request.PostSucceeded();
			}, true);

			apiHandler.RegisterEndpointHandler(kApiUrlPart + "cleanup", request =>
			{
				Stop();
				request.PostSucceeded();
			}, true);

			apiHandler.RegisterEndpointHandler(kApiUrlPart + "textToClipboard", request =>
			{
				PortableClipboard.SetText(request.RequiredPostString());
				request.PostSucceeded();
			}, true);

			apiHandler.RegisterBooleanEndpointHandler(kApiUrlPart + "canHaveMotionMode",
				request =>
				{
					return request.CurrentBook.HasMotionPages;
				},
				null, // no write action
				false,
				true); // we don't really know, just safe default

			apiHandler.RegisterBooleanEndpointHandler(kApiUrlPart + "canRotate",
				request =>
				{
					return request.CurrentBook.MotionMode && request.CurrentBook.HasMotionPages;
				},
				null, // no write action
				false,
				true); // we don't really know, just safe default

			apiHandler.RegisterBooleanEndpointHandler(kApiUrlPart + "defaultLandscape",
				request =>
				{
					return request.CurrentBook.GetLayout().SizeAndOrientation.IsLandScape;
				},
				null, // no write action
				false,
				true); // we don't really know, just safe default
			apiHandler.RegisterEndpointHandler(kApiUrlPart + "languagesInBook", request =>
			{
				try
				{
					InitializeLanguagesInBook(request);
					var result = "[" + string.Join(",", _allLanguages.Select(kvp =>
					{
						var complete = kvp.Value ? "true" : "false";
						var include = _languagesToPublish.Contains(kvp.Key) ? "true" : "false";
						return $"{{\"code\":\"{kvp.Key}\", \"name\":\"{request.CurrentBook.PrettyPrintLanguage((kvp.Key))}\",\"complete\":{complete},\"include\":{include}}}";
					})) + "]";

					request.ReplyWithText(result);
				}
				catch (Exception e)
				{
					request.Failed("Error while determining languages in book. Message: " + e.Message);
					NonFatalProblem.Report(ModalIf.Alpha, PassiveIf.All, "Error determining which languages are in the book.", null, e, true);
				}
			}, false);
			apiHandler.RegisterEndpointHandler(kApiUrlPart + "includeLanguage", request =>
			{
				var langCode = request.RequiredParam("langCode");
				if (request.HttpMethod == HttpMethods.Post)
				{
					var val = request.RequiredParam("include") == "true";
					if (val)
					{
						_languagesToPublish.Add(langCode);
					}
					else
					{
						_languagesToPublish.Remove(langCode);
					}
					request.PostSucceeded();
				}
				// We don't currently need a get...it's subsumed in the 'include' value returned from allLanguages...
				// but if we ever do this is what it would look like.
				//else
				//{
				//	request.ReplyWithText(_languagesToPublish.Contains(langCode) ? "true" : "false");
				//}
			}, false);
		}

		private AndroidPublishSettings _lastSettings;
		private Color _lastThumbnailBackgroundColor;

		/// <summary>
		/// The book language data needs to be initialized before handling updatePreview requests, but the
		/// languagesInBook request comes in after the updatePreview request.  So we call this method in
		/// both places with a lock to prevent stepping on each other.  This results in duplicate
		/// calls for AllLanguages, but is safest since the user could leave the publish tab, change the
		/// languages in the book, and then come back to the publish tab with the same book.
		/// </summary>
		private void InitializeLanguagesInBook(ApiRequest request)
		{
			lock (_lockForLanguages)
			{
				_allLanguages = request.CurrentBook.AllPublishableLanguages(includeLangsOccurringOnlyInXmatter: true);
				if (_bookForLanguagesToPublish != request.CurrentBook)
				{
					// reinitialize our list of which languages to publish, defaulting to the ones
					// that are complete.
					// Enhance: persist this somehow.
					// Currently the whole Publish screen is regenerated (and languagesInBook retrieved again)
					// whenever a check box is changed, so it's very important not to do this set-to-default
					// code when we haven't changed books.
					_bookForLanguagesToPublish = request.CurrentBook;
					_languagesToPublish.Clear();
					foreach (var kvp in _allLanguages)
					{
						if (kvp.Value)
							_languagesToPublish.Add(kvp.Key);
					}
				}
			}
		}

		/// <summary>
		/// Updates the BloomReader preview. The URL of the BloomReader preview will be sent over the web socket.
		/// The format of the URL is a valid ("single" encoded) URL.
		/// If the caller wants to insert this URL as a query parameter to another URL (e.g. like what is often done with Bloom Player),
		/// it's the caller's responsibility to apply another layer of URL encoding to make the URL suitable to be passed as data inside another URL.
		/// </summary>
		private void UpdatePreview(ApiRequest request)
		{
			InitializeLanguagesInBook(request);
			_lastSettings = GetSettings();
			_lastThumbnailBackgroundColor = _thumbnailBackgroundColor;
			PreviewUrl = StageBloomD(request.CurrentBook, _bookServer, _progress, _thumbnailBackgroundColor, _lastSettings);
			_webSocketServer.SendString(kWebSocketContext, kWebsocketEventId_Preview, PreviewUrl);
		}

		private void UpdatePreviewIfNeeded(ApiRequest request)
		{
			var newSettings = GetSettings();
			if (newSettings.Equals(_lastSettings) && _thumbnailBackgroundColor == _lastThumbnailBackgroundColor)
			{
				return;
			}
			UpdatePreview(request);
		}

		public void Stop()
		{
#if !__MonoCS__
			_usbPublisher.Stop();
#endif
			_wifiPublisher.Stop();
			SetState("stopped");
			_stagingFolder?.Dispose();
		}

		private void SetState(string state)
		{
			_webSocketServer.SendString(kWebSocketContext, kWebsocketState_EventId, state);
		}

		public static void ReportAnalytics(string mode, Book.Book book)
		{
			Analytics.Track("Publish Android", new Dictionary<string, string>() {{"mode", mode}, {"BookId", book.ID}, { "Country", book.CollectionSettings.Country}, {"Language", book.BookData.Language1.Iso639Code}});
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
			Func<string, bool> confirmFunction, Color backColor, AndroidPublishSettings settings = null)
		{
			var bookTitle = book.Title;
			progress.MessageUsingTitle("PackagingBook", "Packaging \"{0}\" for use with Bloom Reader...", bookTitle, MessageKind.Progress);

			// compress audio if needed, with progress message
			if (AudioProcessor.IsAnyCompressedAudioMissing(book.FolderPath, book.RawDom))
			{
				progress.Message("CompressingAudio", "Compressing audio files");
				AudioProcessor.TryCompressingAudioAsNeeded(book.FolderPath, book.RawDom);
			}
			var publishedFileName = Path.GetFileName(book.FolderPath) + BookCompressor.ExtensionForDeviceBloomBook;
			if (startingMessageFunction != null)
				progress.MessageWithoutLocalizing(startingMessageFunction(publishedFileName, bookTitle));
			if (destFileName == null)
			{
				// wifi or usb...make the .bloomd in a temp folder.
				using (var bloomdTempFile = TempFile.WithFilenameInTempFolder(publishedFileName))
				{
					BloomReaderFileMaker.CreateBloomDigitalBook(bloomdTempFile.Path, book, bookServer, backColor, progress, settings);
					sendAction(publishedFileName, bloomdTempFile.Path);
					if (confirmFunction != null && !confirmFunction(publishedFileName))
						throw new ApplicationException("Book does not exist after write operation.");
					progress.MessageUsingTitle("BookSent", "You can now read \"{0}\" in Bloom Reader!", bookTitle, MessageKind.Note);
				}
			}
			else
			{
				// save file...user has supplied name, there is no further action.
				Debug.Assert(sendAction == null, "further actions are not supported when passing a path name");
				BloomReaderFileMaker.CreateBloomDigitalBook(destFileName, book, bookServer, backColor, progress, settings);
				progress.Message("PublishTab.Epub.Done", "Done", useL10nIdPrefix: false);	// share message string with epub publishing
			}

		}

		private static TemporaryFolder _stagingFolder;

		/// <summary>
		/// Generates a .bloomd file (bloompub) from the book
		/// </summary>
		/// <param name="book"></param>
		/// <param name="bookServer"></param>
		/// <param name="progress"></param>
		/// <param name="backColor"></param>
		/// <param name="settings"></param>
		/// <returns>A valid, well-formed URL on localhost that points to the bloomd</returns>
		public static string StageBloomD(Book.Book book, BookServer bookServer, WebSocketProgress progress, Color backColor, AndroidPublishSettings settings = null)
		{
			progress.Message("PublishTab.Epub.PreparingPreview", "Preparing Preview");	// message shared with Epub publishing
			if (settings?.LanguagesToInclude != null)
			{
				var message = new LicenseChecker().CheckBook(book, settings.LanguagesToInclude.ToArray());
				if (message != null)
				{
					progress.MessageWithoutLocalizing(message, MessageKind.Error);
					return null;
				}
			}

			_stagingFolder?.Dispose();
			if (AudioProcessor.IsAnyCompressedAudioMissing(book.FolderPath, book.RawDom))
			{
				progress.Message("CompressingAudio", "Compressing audio files");
				AudioProcessor.TryCompressingAudioAsNeeded(book.FolderPath, book.RawDom);
			}
			// BringBookUpToDate() will already have been done on the original book on entering the Publish tab.

			// We don't use the folder found here, but this method does some checks we want done.
			BookStorage.FindBookHtmlInFolder(book.FolderPath);
			_stagingFolder = new TemporaryFolder(StagingFolder);
			var modifiedBook = BloomReaderFileMaker.PrepareBookForBloomReader(book.FolderPath, bookServer, _stagingFolder, progress, settings: settings);
			progress.Message("Common.Done", "Shown in a list of messages when Bloom has completed a task.", "Done");
			return modifiedBook.FolderPath.ToLocalhost();
		}

		/// <summary>
		/// Check for either "Device16x9Portrait" or "Device16x9Landscape" layout.
		/// Complain to the user if another layout is currently chosen.
		/// </summary>
		/// <remarks>
		/// See https://issues.bloomlibrary.org/youtrack/issue/BL-5274.
		/// </remarks>
		public static void CheckBookLayout(Bloom.Book.Book book, WebSocketProgress progress)
		{
			var layout = book.GetLayout();
			var desiredLayoutSize = "Device16x9";
			if (layout.SizeAndOrientation.PageSizeName != desiredLayoutSize)
			{
				// The progress object has been initialized to use an id prefix.  So we'll access L10NSharp explicitly here.  We also want to make the string blue,
				// which requires a special argument.
//				var msgFormat = L10NSharp.LocalizationManager.GetString("Common.Note",
//					"Note", "A heading shown above some messages.");
//				progress.MessageWithoutLocalizing(msgFormat, MessageKind.Note);
				 var msgFormat = L10NSharp.LocalizationManager.GetString("PublishTab.Android.WrongLayout.Message",
					"The layout of this book is currently \"{0}\". Bloom Reader will display it using \"{1}\", so text might not fit. To see if anything needs adjusting, go back to the Edit Tab and change the layout to \"{1}\".",
					"{0} and {1} are book layout tags.");
				var desiredLayout = desiredLayoutSize + layout.SizeAndOrientation.OrientationName;
				var msg = String.Format(msgFormat, layout.SizeAndOrientation.ToString(), desiredLayout, Environment.NewLine);
				progress.MessageWithoutLocalizing(msg, MessageKind.Note);
			}
		}
	}
}
