using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Dynamic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bloom.Collection;
using Newtonsoft.Json;
using RestSharp.Contrib;
using SIL.Code;
using SIL.Reporting;

namespace Bloom.web
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
			// Note that there is no slash in this one. Typically it is the whole localPath.
			server.RegisterRequestHandler("bookSettings", ReplyWithBookSettings);
			server.RegisterRequestHandler("imageInfo", ReplyWithImageInfo);
			// Note that there is no slash in this one. Typically it is the whole localPath.
			server.RegisterRequestHandler("getNextBookStyle", ReplyWithNextBookStyle);
		}

		/// <summary>
		/// Get a json of the book's settings.
		/// </summary>
		/// <param name="dummy1"></param>
		/// <param name="info"></param>
		/// <param name="dummy2"></param>
		/// <returns></returns>
		private static bool ReplyWithBookSettings(string dummy1, IRequestInfo info, CollectionSettings dummy2)
		{
			info.ContentType = "text/json";
			dynamic settings = new ExpandoObject();
			settings.unlockShellBook = CurrentBook.TemporarilyUnlocked;
			info.WriteCompleteOutput(JsonConvert.SerializeObject(settings));
			return true;
		}

		/// <summary>
		/// Get a json of stats about the image. It is used to populate a tooltip when you hover over an image container
		/// </summary>
		private static bool ReplyWithImageInfo(string localPath, IRequestInfo info, CollectionSettings dummy)
		{
			try
			{
				info.ContentType = "text/json";
				Require.That(info.RawUrl.Contains("?"));
				var query = info.RawUrl.Split('?')[1];
				var args = HttpUtility.ParseQueryString(query);
				Guard.AssertThat(args.Get("image") != null, "problem with image parameter");
				var fileName = args["image"];
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

				info.WriteCompleteOutput(JsonConvert.SerializeObject(result));
				return true;
			}
			catch (Exception e)
			{
				Logger.WriteEvent("Error in server imageInfo/: url was " + localPath);
				Logger.WriteEvent("Error in server imageInfo/: exception is " + e.Message);
			}
			return false;
		}

		/// <summary>
		/// Not sure whether we should keep this one. Code was migrated as part of isolating the server requests that depened on the
		/// current book, but I cannot find anywhere it is used except for a commented-out call in Origami's makeTextFieldClickHandler
		/// method.
		/// </summary>
		/// <param name="localPath"></param>
		/// <param name="info"></param>
		/// <param name="dummy"></param>
		/// <returns></returns>
		private static bool ReplyWithNextBookStyle(string localPath, IRequestInfo info, CollectionSettings dummy)
		{
			info.ContentType = "text/html";
			info.WriteCompleteOutput(CurrentBookHandler.CurrentBook.NextStyleNumber.ToString(CultureInfo.InvariantCulture));
			return true;
		}
	}
}
