using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Dynamic;
using System.IO;
using System.Linq;
using System.Text;
using Bloom.Api;
using Bloom.Book;
using SIL.Code;
using SIL.Extensions;
using SIL.Reporting;
using SIL.Windows.Forms.ClearShare;
using L10NSharp;
using SIL.IO;

namespace Bloom.web.controllers
{
	public class ImageApi
	{
		private readonly BookSelection _bookSelection;
		private readonly string[] _doNotPasteArray;

		public ImageApi(BookSelection bookSelection)
		{
			_bookSelection = bookSelection;
			// The following is a list of image files that we don't want to paste image credits for.
			// It includes CC license image, placeholder and branding images.
			_doNotPasteArray = GetImageFilesToNotPasteCreditsFor().ToArray();
		}

		private static IEnumerable<string> GetImageFilesToNotPasteCreditsFor()
		{
			var imageFiles = new HashSet<string> {"license.png", "placeholder.png"};
			var brandingDirectory = FileLocator.GetDirectoryDistributedWithApplication("branding");
			foreach (var brandDirectory in Directory.GetDirectories(brandingDirectory))
			{
				imageFiles.AddRange(Directory.EnumerateFiles(brandDirectory).Where(IsSvgOrPng).Select(Path.GetFileName));
			}
			return imageFiles;
		}

		private static bool IsSvgOrPng(string filename)
		{
			var lcFilename = filename.ToLowerInvariant();
			return Path.GetExtension(lcFilename) == ".svg" || Path.GetExtension(lcFilename) == ".png";
		}

		public void RegisterWithServer(EnhancedImageServer server)
		{
			// These are both just retrieving information about files, apart from using _bookSelection.CurrentSelection.FolderPath.
			server.RegisterEndpointHandler("image/info", HandleImageInfo, false);
			server.RegisterEndpointHandler("image/imageCreditsForWholeBook", HandleCopyImageCreditsForWholeBook, false);
		}

		private void HandleCopyImageCreditsForWholeBook(ApiRequest request)
		{
			// This method is called on a fileserver thread. To minimize the chance that the current selection somehow
			// changes while it is running, we capture the things that depend on it in variables right at the start.
			var names = BookStorage.GetImagePathsRelativeToBook(_bookSelection.CurrentSelection.RawDom.DocumentElement);
			var currentSelectionFolderPath = _bookSelection.CurrentSelection.FolderPath;
			IEnumerable<string> langs = null;
			if (request.CurrentCollectionSettings != null)
				langs = request.CurrentCollectionSettings.LicenseDescriptionLanguagePriorities;
			else
				langs = new List<string> { "en" };		// emergency fall back -- probably never used.
			var credits = new List<string>();
			var missingCredits = new List<string>();
			foreach (var name in names)
			{
				var path = currentSelectionFolderPath.CombineForPath(name);
				if (RobustFile.Exists(path))
				{
					var meta = Metadata.FromFile(path);
					string id;
					var credit = meta.MinimalCredits(langs, out id);
					if (string.IsNullOrEmpty(credit) && !DoNotPasteCreditsImages(name))
						missingCredits.Add(name);
					if (!string.IsNullOrEmpty(credit) && !credits.Contains(credit))
						credits.Add(credit);
				}
			}
			var total = credits.Aggregate(new StringBuilder(), (all,credit) => {
				all.AppendFormat("<p>{0}</p>{1}", credit, System.Environment.NewLine);
				return all;
			});
			// Notify the user of images with missing credits.
			if (missingCredits.Count > 0)
			{
				var missing = LocalizationManager.GetString("EditTab.FrontMatter.PasteMissingCredits", "Missing credits:");
				total.AppendFormat("<p>{0}", missing);
				for (var i = 0; i < missingCredits.Count; ++i)
				{
					if (i > 0)
						total.Append(",");
					total.AppendFormat(" {0}", missingCredits[i]);
				}
				total.AppendFormat("</p>{0}", System.Environment.NewLine);
			}
			request.ReplyWithText(total.ToString());
		}

		private bool DoNotPasteCreditsImages(string name)
		{
			// returns 'true' if 'name' is among the list of ones we don't want to paste image credits for
			// includes CC license image, placeholder and branding images
			return _doNotPasteArray.Contains(name.ToLowerInvariant());
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
				if (!RobustFile.Exists(path))
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
				using (var stream = RobustFile.OpenRead(path))
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
				Logger.WriteError("Error in server imageInfo/: url was " + request.LocalPath(), e);
				request.Failed(e.Message);
			}
		}
	}
}
