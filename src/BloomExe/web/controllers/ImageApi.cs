using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Dynamic;
using System.IO;
using Bloom.Api;
using Bloom.Book;
using SIL.Code;
using SIL.Reporting;

namespace Bloom.web.controllers
{
	public class ImageApi
	{
		private readonly BookSelection _bookSelection;

		public ImageApi(BookSelection bookSelection)
		{
			_bookSelection = bookSelection;
		}
		public void RegisterWithServer(EnhancedImageServer server)
		{
			server.RegisterEndpointHandler("image/info", HandleImageInfo);
		}

		/// <summary>
		/// Get a json of stats about the image. It is used to populate a tooltip when you hover over an image container
		/// </summary>
		private void HandleImageInfo(ApiRequest request)
		{
			try
			{
				var fileName = request.RequiredParam("image");
				Guard.AgainstNull(_bookSelection.CurrentSelection, "CurrentBook");
				var path = Path.Combine(_bookSelection.CurrentSelection.FolderPath, fileName);
				if (!File.Exists(path))
				{
					// We can be fed doubly-encoded filenames.  So try to decode a second time and see if that works.
					// See https://silbloom.myjetbrains.com/youtrack/issue/BL-3749.
					fileName = System.Web.HttpUtility.UrlDecode(fileName);
					path = Path.Combine(_bookSelection.CurrentSelection.FolderPath, fileName);
				}
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
				request.ReplyWithJson((object)result);
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
