using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows;
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

namespace Bloom.Publish.Android
{
	/// <summary>
	/// Handles api request dealing with the publishing of books to an Android device
	/// </summary>
	public class PublishToAndroidApi
	{
		private const string kApiUrlPart = "publish/android/";
		private const string kWebsocketStateId = "publish/android/state";
		private readonly WiFiPublisher _wifiPublisher;
#if !__MonoCS__
		private readonly UsbPublisher _usbPublisher;
#endif
		private readonly BloomWebSocketServer _webSocketServer;
		private readonly BookServer _bookServer;
		private readonly WebSocketProgress _progress;

		private Color _thumbnailBackgroundColor = Color.Transparent; // can't be actual book cover color
		private Book.Book _coverColorSourceBook;

		private RuntimeImageProcessor _imageProcessor;

		public PublishToAndroidApi(BloomWebSocketServer bloomWebSocketServer, BookServer bookServer, RuntimeImageProcessor imageProcessor)
		{
			_webSocketServer = bloomWebSocketServer;
			_bookServer = bookServer;
			_imageProcessor = imageProcessor;
			_progress = new WebSocketProgress(_webSocketServer);
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


			server.RegisterEndpointHandler(kApiUrlPart + "coverImage", request =>
			{
				var coverImage = request.CurrentBook.GetCoverImagePath();
				if (coverImage == null)
					request.Failed("no cover image");
				else
				{
					// We don't care as much about making it resized as making its background transparent.
					request.ReplyWithImage(_imageProcessor.GetPathToResizedImage(coverImage));
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
				FilePublisher.Save(request.CurrentBook, _bookServer, _thumbnailBackgroundColor);
				SetState("stopped");
				request.PostSucceeded();
			}, true);

			server.RegisterEndpointHandler(kApiUrlPart + "cleanup", request =>
			{
#if !__MonoCS__
				_usbPublisher.Stop();
#endif
				_wifiPublisher.Stop();
				SetState("stopped");
				request.PostSucceeded();
			}, true);

			server.RegisterEndpointHandler(kApiUrlPart + "textToClipboard", request =>
			{
				PortableClipboard.SetText(request.RequiredPostString());
				request.PostSucceeded();
			}, true);
		}

		private void SetState(string state)
		{
			_webSocketServer.Send(kWebsocketStateId, state);
		}

		public static void ReportAnalytics(string mode, Book.Book book)
		{
			Analytics.Track("Publish Android", new Dictionary<string, string>() {{"mode", mode}, {"BookId", book.ID}, { "Country", book.CollectionSettings.Country}, {"Language", book.CollectionSettings.Language1Iso639Code}});
		}
	}
}
