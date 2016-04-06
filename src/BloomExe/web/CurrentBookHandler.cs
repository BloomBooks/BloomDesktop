using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Dynamic;
using System.Globalization;
using System.IO;
using SIL.Code;
using SIL.Reporting;

namespace Bloom.Api
{
	/// <summary>
	/// This class is responsible for handling Server requests that depend on knowledge of the current book.
	/// An exception is some reader tools requests, which have their own handler, though most of them depend
	/// on knowing the current book. This class provides that information to the ReadersHandler.
	/// </summary>
	public static class CurrentBookHandler
	{
		public static Book.Book CurrentBook { set; get; }

		public static void Init(EnhancedImageServer server)
		{
			server.RegisterEndpointHandler("bookSettings", HandleGetBookSettings);
			server.RegisterEndpointHandler("imageInfo", HandleGetImageInfo);
		}

		/// <summary>
		/// Get a json of the book's settings.
		/// </summary>
		private static void HandleGetBookSettings(ApiRequest request)
		{
			dynamic settings = new ExpandoObject();
			settings.unlockShellBook = CurrentBook.TemporarilyUnlocked;
			request.ReplyWithJson((object) settings);
		}

		/// <summary>
		/// Get a json of stats about the image. It is used to populate a tooltip when you hover over an image container
		/// </summary>
		private static void HandleGetImageInfo(ApiRequest request)
		{
			try
			{		
				var fileName = request.RequiredParam("image");
				Guard.AgainstNull(CurrentBook, "CurrentBook");
				var path = Path.Combine(CurrentBook.FolderPath, fileName);
				RequireThat.File(path).Exists();
				var fileInfo = new FileInfo(path);
				dynamic result = new ExpandoObject();
				result.name = fileName;
				result.bytes = fileInfo.Length;

				// Using a stream this way, according to one source,
				// http://stackoverflow.com/questions/552467/how-do-i-reliably-get-an-image-dimensions-in-net-without-loading-the-image,
				// supposedly avoids loading the image into memory when we only want its dimensions
				using (var stream = File.OpenRead(path))
				using (var img = Image.FromStream(stream, false, false))
				{
					result.width = img.Width;
					result.height = img.Height;
					switch (img.PixelFormat)
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
			catch (Exception e)
			{
				Logger.WriteEvent("Error in server imageInfo/: url was " + request.LocalPath());
				Logger.WriteEvent("Error in server imageInfo/: exception is " + e.Message);
				request.Failed(e.Message);
			}
		}
	}
}
