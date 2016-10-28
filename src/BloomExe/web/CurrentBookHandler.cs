using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Dynamic;
using System.IO;
using Bloom.Book;
using SIL.Code;
using SIL.IO;
using SIL.Reporting;

namespace Bloom.Api
{
	/// <summary>
	/// This class is responsible for handling Server requests that depend on knowledge of the current book.
	/// An exception is some reader tools requests, which have their own handler, though most of them depend
	/// on knowing the current book.
	/// </summary>
	public class CurrentBookHandler
	{
		private readonly BookSelection _bookSelection;
		private readonly PageRefreshEvent _pageRefreshEvent;

		public CurrentBookHandler(BookSelection bookSelection, PageRefreshEvent pageRefreshEvent)
		{
			_bookSelection = bookSelection;
			_pageRefreshEvent = pageRefreshEvent;
		}

		public void RegisterWithServer(EnhancedImageServer server)
		{
			server.RegisterEndpointHandler("bookSettings", HandleBookSettings);
			server.RegisterEndpointHandler("imageInfo", HandleImageInfo);
		}

		/// <summary>
		/// Get a json of the book's settings.
		/// </summary>
		private  void HandleBookSettings(ApiRequest request)
		{
			switch (request.HttpMethod)
			{
				case HttpMethods.Get:
					dynamic settings = new ExpandoObject();
					settings.isRecordedAsLockedDown = _bookSelection.CurrentSelection.RecordedAsLockedDown;
					settings.unlockShellBook = _bookSelection.CurrentSelection.TemporarilyUnlocked;
					settings.currentToolBoxTool = _bookSelection.CurrentSelection.BookInfo.CurrentTool;
					request.ReplyWithJson((object)settings);
					break;
				case HttpMethods.Post:
					//note: since we only have this one value, it's not clear yet whether the panel involved here will be more of a
					//an "edit settings", or a "book settings", or a combination of them.
					settings = DynamicJson.Parse(request.RequiredPostJson());
					_bookSelection.CurrentSelection.TemporarilyUnlocked = settings["unlockShellBook"];
					_pageRefreshEvent.Raise(PageRefreshEvent.SaveBehavior.SaveBeforeRefresh);
					request.Succeeded();
					break;
				default:
					throw new ArgumentOutOfRangeException();
			}
		}

		/// <summary>
		/// Get a json of stats about the image. It is used to populate a tooltip when you hover over an image container
		/// </summary>
		private void HandleImageInfo(ApiRequest request)
		{
			try
			{
				var fileName = request.RequiredFileNameOrPath("image");
				Guard.AgainstNull(_bookSelection.CurrentSelection, "CurrentBook");
				var plainfilename = fileName.NotEncoded;
				// The fileName might be URL encoded.  See https://silbloom.myjetbrains.com/youtrack/issue/BL-3901.
				var path = UrlPathString.GetFullyDecodedPath(_bookSelection.CurrentSelection.FolderPath, ref plainfilename);
				RequireThat.File(path).Exists();
				var fileInfo = new FileInfo(path);
				dynamic result = new ExpandoObject();
				result.name = plainfilename;
				result.bytes = fileInfo.Length;

				// Using a stream this way, according to one source,
				// http://stackoverflow.com/questions/552467/how-do-i-reliably-get-an-image-dimensions-in-net-without-loading-the-image,
				// supposedly avoids loading the image into memory when we only want its dimensions
				using(var stream = RobustFile.OpenRead(path))
				using(var img = Image.FromStream(stream, false, false))
				{
					result.width = img.Width;
					result.height = img.Height;
					switch(img.PixelFormat)
					{
						case PixelFormat.Format32bppArgb:
						case PixelFormat.Format32bppRgb:
						case PixelFormat.Format32bppPArgb:
							result.bitDepth = "32";
							break;
						case PixelFormat.Format24bppRgb:
							result.bitDepth = "24";
							break;
						case PixelFormat.Format16bppArgb1555:
						case PixelFormat.Format16bppGrayScale:
							result.bitDepth = "16";
							break;
						case PixelFormat.Format8bppIndexed:
							result.bitDepth = "8";
							break;
						case PixelFormat.Format1bppIndexed:
							result.bitDepth = "1";
							break;
						default:
							result.bitDepth = "unknown";
							break;
					}
				}
				request.ReplyWithJson((object) result);
			}
			catch(Exception e)
			{
				Logger.WriteEvent("Error in server imageInfo/: url was " + request.LocalPath());
				Logger.WriteEvent("Error in server imageInfo/: exception is " + e.Message);
				request.Failed(e.Message);
				NonFatalProblem.Report(ModalIf.None, PassiveIf.Alpha, "Request Error", request.LocalPath(), e);
			}
		}
	}
}
