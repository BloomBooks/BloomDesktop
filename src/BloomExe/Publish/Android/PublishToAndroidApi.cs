using System;
using System.Collections.Generic;
using System.Drawing;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Bloom.Api;
using Bloom.Book;
using Bloom.Collection;
using Bloom.ImageProcessing;
using Bloom.MiscUI;
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
using Newtonsoft.Json;
using SIL.Xml;

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
		private readonly CollectionSettings _collectionSettings;
		private readonly BloomWebSocketServer _webSocketServer;
		private readonly BookServer _bookServer;
		private readonly BulkBloomPubCreator _bulkBloomPubCreator;
		private readonly WebSocketProgress _progress;
		private const string kWebSocketContext = "publish-android"; // must match what is in AndroidPublishUI.tsx
		private Color _thumbnailBackgroundColor = Color.Transparent; // can't be actual book cover color <--- why not?
		private Book.Book _coverColorSourceBook;

		private object _lockForLanguages = new object();
		private Dictionary<string, bool> _allLanguages;
		private HashSet<string> _languagesWithAudio = new HashSet<string>();
		private Bloom.Book.Book _bookForLanguagesToPublish = null;

		// This constant must match the ID that is used for the listener set up in the React component AndroidPublishUI
		private const string kWebsocketEventId_Preview = "androidPreview";
		public const string StagingFolder = "PlaceForStagingBook";

		// This constant must match the ID used for the useWatchString called by the React component MethodChooser.
		private const string kWebsocketState_LicenseOK = "publish/licenseOK";

		public static string PreviewUrl { get; set; }
		
		public PublishToAndroidApi(CollectionSettings collectionSettings, BloomWebSocketServer bloomWebSocketServer, BookServer bookServer, BulkBloomPubCreator bulkBloomPubCreator)
		{
			_collectionSettings = collectionSettings;
			_webSocketServer = bloomWebSocketServer;
			_bookServer = bookServer;
			_bulkBloomPubCreator = bulkBloomPubCreator;
			_progress = new WebSocketProgress(_webSocketServer, kWebSocketContext);
			_wifiPublisher = new WiFiPublisher(_progress, _bookServer);
#if !__MonoCS__
			_usbPublisher = new UsbPublisher(_progress, _bookServer)
			{
				Stopped = () => SetState("stopped")
			};
#endif
		}

		/// <summary>
		/// Conceptually, this is where we are currently building a book for preview.
		/// In the current implementation, it is not cleared when we are no longer doing so.
		/// Nor does it include the path to the individual book folder, just the staging
		/// folder. This is not ideal but it serves the current limited purpose of this field.
		/// </summary>
		public static string CurrentPublicationFolder { get; private set; }
		

		private static string ToCssColorString(System.Drawing.Color c)
		{
			return "#" + c.R.ToString("X2") + c.G.ToString("X2") + c.B.ToString("X2");
		}

		private AndroidPublishSettings GetSettings()
		{
			return AndroidPublishSettings.FromBookInfo(_bookForLanguagesToPublish.BookInfo);
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
						request.ReplyWithImage(thumbnail.Path);
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

			apiHandler.RegisterEndpointHandler(kApiUrlPart + "file/bulkSaveBloomPubsParams", request =>
			{ 
				request.ReplyWithJson(JsonConvert.SerializeObject(_collectionSettings.BulkPublishBloomPubSettings));
			}, true);

			apiHandler.RegisterEndpointHandler(kApiUrlPart + "file/bulkSaveBloomPubs", request =>
			{
				// update what's in the collection so that we remember for next time
				_collectionSettings.BulkPublishBloomPubSettings = request.RequiredPostObject<BulkBloomPubPublishSettings>();
				_collectionSettings.Save();

				_bulkBloomPubCreator.PublishAllBooks(_collectionSettings.BulkPublishBloomPubSettings);
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

					Dictionary<string, InclusionSetting> textLangsToPublish = request.CurrentBook.BookInfo.MetaData.TextLangsToPublish.ForBloomPUB;
					Dictionary<string, InclusionSetting> audioLangsToPublish = request.CurrentBook.BookInfo.MetaData.AudioLangsToPublish.ForBloomPUB;
					
					var result = "[" + string.Join(",", _allLanguages.Select(kvp =>
					{
						string langCode = kvp.Key;

						bool includeText = false;
						if (textLangsToPublish != null && textLangsToPublish.TryGetValue(langCode, out InclusionSetting includeTextSetting))
						{
							includeText = includeTextSetting.IsIncluded();
						}

						bool includeAudio = false;
						if (audioLangsToPublish != null && audioLangsToPublish.TryGetValue(langCode, out InclusionSetting includeAudioSetting))
						{
							includeAudio = includeAudioSetting.IsIncluded();
						}

						var value = new LanguagePublishInfo()
						{
							code = kvp.Key,
							name = request.CurrentBook.PrettyPrintLanguage(langCode),
							complete = kvp.Value,
							includeText = includeText,
							containsAnyAudio = _languagesWithAudio.Contains(langCode),							
							includeAudio = includeAudio
						};
						var json = JsonConvert.SerializeObject(value);
						return json;
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
					var includeTextValue = request.GetParamOrNull("includeText");
					if (includeTextValue != null)
					{
						var inclusionSetting = includeTextValue == "true" ? InclusionSetting.Include : InclusionSetting.Exclude;
						request.CurrentBook.BookInfo.MetaData.TextLangsToPublish.ForBloomPUB[langCode] = inclusionSetting;
					}

					var includeAudioValue = request.GetParamOrNull("includeAudio");
					if (includeAudioValue != null)
					{
						var inclusionSetting = includeAudioValue == "true" ? InclusionSetting.Include : InclusionSetting.Exclude;
						request.CurrentBook.BookInfo.MetaData.AudioLangsToPublish.ForBloomPUB[langCode] = inclusionSetting;
					}

					request.CurrentBook.BookInfo.Save();	// We updated the BookInfo, so need to persist the changes. (but only the bookInfo is necessary, not the whole book)
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

				// Note that at one point, we had a check that would bypass most of this function if the book hadn't changed.
				// However, one side effect of this is that any settings behind the if guard would not be updated if the book was edited
				// At one point, whenever a check box changed, the whole Publish screen was regenerated (along with languagesInBook being retrieved again),
				// but this is no longer the case.
				// So, we no longer have any bypass... Instead we recompute the values so that they can be updated
				_bookForLanguagesToPublish = request.CurrentBook;

				_languagesWithAudio = request.CurrentBook.GetLanguagesWithAudio();

				InitializeLanguagesInBook(_bookForLanguagesToPublish.BookInfo, _allLanguages, request.CurrentCollectionSettings);
			}
		}

		// Precondition: If any locking is required, the caller should handle it.
		internal static void InitializeLanguagesInBook(BookInfo bookInfo, Dictionary<string, bool> allLanguages, CollectionSettings collectionSettings)
		{
			Debug.Assert(bookInfo?.MetaData != null, "Precondition: MetaData must not be null");

			if (bookInfo.MetaData.TextLangsToPublish == null)
			{
				bookInfo.MetaData.TextLangsToPublish = new LangsToPublishSetting();
			}

			if (bookInfo.MetaData.TextLangsToPublish.ForBloomPUB == null)
			{
				bookInfo.MetaData.TextLangsToPublish.ForBloomPUB = new Dictionary<string, InclusionSetting>();
			}

			// reinitialize our list of which languages to publish, defaulting to the ones
			// that are complete.
			foreach (var kvp in allLanguages)
			{
				var langCode = kvp.Key;

				// First, check if the user has already explicitly set the value. If so, we'll just use that value and be done.
				if (bookInfo.MetaData.TextLangsToPublish.ForBloomPUB.TryGetValue(langCode, out InclusionSetting checkboxValFromSettings))
				{
					if (checkboxValFromSettings.IsSpecified())
					{
						continue;
					}
				}

				// Nope, either no value exists or the value was some kind of default value.
				// Compute (or recompute) what the value should default to.
				bool isChecked = kvp.Value ||
					// We always select L1 by default because we assume the user wants to publish the language he is currently working on.
					// It may be incomplete if he just wants to preview his work so far.
					// If he really doesn't want to publish L1, he can deselect it.
					// See BL-9587.
					langCode == collectionSettings?.Language1Iso639Code;

				var newInitialValue = isChecked ? InclusionSetting.IncludeByDefault : InclusionSetting.ExcludeByDefault;
				bookInfo.MetaData.TextLangsToPublish.ForBloomPUB[langCode] = newInitialValue;
			}

			// Initialize the Talking Book Languages settings
			if (bookInfo.MetaData.AudioLangsToPublish == null)
			{
				bookInfo.MetaData.AudioLangsToPublish = new LangsToPublishSetting();
			}

			if (bookInfo.MetaData.AudioLangsToPublish.ForBloomPUB == null)
			{
				bookInfo.MetaData.AudioLangsToPublish.ForBloomPUB = new Dictionary<string, InclusionSetting>();
				var allLangCodes = allLanguages.Select(x => x.Key);
				foreach (var langCode in allLangCodes)
				{
					bookInfo.MetaData.AudioLangsToPublish.ForBloomPUB[langCode] = InclusionSetting.IncludeByDefault;
				}					
			}

			// The metadata may have been changed, so saved it.
			// Note - If you want, you could check whether or not it was actually changed, but that might be premature optimization.
			bookInfo.Save();
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
			PreviewUrl = MakeBloomPubForPreview(request.CurrentBook, _bookServer, _progress, _thumbnailBackgroundColor, _lastSettings);
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
			progress.MessageUsingTitle("PackagingBook", "Packaging \"{0}\" for use with Bloom Reader...", bookTitle, ProgressKind.Progress);


			// REVIEW: Why is this here in this method? We do a bunch of things to convert a book, but this one thing, audio, was
			// put here instead in BloomReaderFileMaker along with all the other operations.


			// Compress audio if needed, with progress message
			if (AudioProcessor.IsAnyCompressedAudioMissing(book.FolderPath, book.RawDom))
			{
				progress.Message("CompressingAudio", "Compressing audio files");
				AudioProcessor.TryCompressingAudioAsNeeded(book.FolderPath, book.RawDom);
			}
			var publishedFileName = Path.GetFileName(book.FolderPath) + BookCompressor.BloomPubExtensionWithDot;
			if (startingMessageFunction != null)
				progress.MessageWithoutLocalizing(startingMessageFunction(publishedFileName, bookTitle));
			if (destFileName == null)
			{
				// wifi or usb...make the .bloomd in a temp folder.
				using (var bloomdTempFile = TempFile.WithFilenameInTempFolder(publishedFileName))
				{
					BloomPubMaker.CreateBloomPub(bloomdTempFile.Path, book, bookServer,  progress, settings);
					sendAction(publishedFileName, bloomdTempFile.Path);
					if (confirmFunction != null && !confirmFunction(publishedFileName))
						throw new ApplicationException("Book does not exist after write operation.");
					progress.MessageUsingTitle("BookSent", "You can now read \"{0}\" in Bloom Reader!", bookTitle, ProgressKind.Note);
				}
			}
			else
			{
				// save file...user has supplied name, there is no further action.
				Debug.Assert(sendAction == null, "further actions are not supported when passing a path name");
				BloomPubMaker.CreateBloomPub(destFileName, book, bookServer,  progress, settings);
				progress.Message("PublishTab.Epub.Done", "Done", useL10nIdPrefix: false);	// share message string with epub publishing
			}

		}

		private static TemporaryFolder _stagingFolder;

		/// <summary>
		/// Generates a .bloomd file (bloompub) from the book
		/// </summary>
		/// <returns>A valid, well-formed URL on localhost that points to the bloomd</returns>
		public string MakeBloomPubForPreview(Book.Book book, BookServer bookServer, WebSocketProgress progress, Color backColor, AndroidPublishSettings settings = null)
		{
			progress.Message("PublishTab.Epub.PreparingPreview", "Preparing Preview");	// message shared with Epub publishing
			if (settings?.LanguagesToInclude != null)
			{
				var message = new LicenseChecker().CheckBook(book, settings.LanguagesToInclude.ToArray());
				if (message != null)
				{
					progress.MessageWithoutLocalizing(message, ProgressKind.Error);
					_webSocketServer.SendString(kWebSocketContext, kWebsocketState_LicenseOK, "false");
					return null;
				}
			}
			_webSocketServer.SendString(kWebSocketContext, kWebsocketState_LicenseOK, "true");

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
			// I'd prefer this to include the book folder, but we need it before PrepareBookForBloomReader returns.
			// I believe we only ever have one book being made there, so it works.
			CurrentPublicationFolder = _stagingFolder.FolderPath;
			var modifiedBook = BloomPubMaker.PrepareBookForBloomReader(book.FolderPath, bookServer, _stagingFolder, progress,book.IsTemplateBook, settings: settings);
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
			// Books with overlays don't get their layout switched, because it would mess them up too badly
			// So this warning is not appropriate for comics or other overlays. We might one day consider a
			// milder warning along the lines that legibility might suffer, especially if there is
			// a large difference in page size.
			if (layout.SizeAndOrientation.PageSizeName != desiredLayoutSize && !book.HasOverlayPages)
			{
				// The progress object has been initialized to use an id prefix.  So we'll access L10NSharp explicitly here.  We also want to make the string blue,
				// which requires a special argument.
//				var msgFormat = L10NSharp.LocalizationManager.GetString("Common.Note",
//					"Note", "A heading shown above some messages.");
//				progress.MessageWithoutLocalizing(msgFormat, ProgressKind.Note);
				 var msgFormat = L10NSharp.LocalizationManager.GetString("PublishTab.Android.WrongLayout.Message",
					"The layout of this book is currently \"{0}\". Bloom Reader will display it using \"{1}\", so text might not fit. To see if anything needs adjusting, go back to the Edit Tab and change the layout to \"{1}\".",
					"{0} and {1} are book layout tags.");
				var desiredLayout = desiredLayoutSize + layout.SizeAndOrientation.OrientationName;
				var msg = String.Format(msgFormat, layout.SizeAndOrientation.ToString(), desiredLayout, Environment.NewLine);
				progress.MessageWithoutLocalizing(msg, ProgressKind.Note);
			}
		}
	}
}
