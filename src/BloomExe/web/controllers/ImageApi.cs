using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Dynamic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using Bloom.Api;
using Bloom.Book;
using Bloom.Collection;
using SIL.Code;
using SIL.Extensions;
using SIL.Reporting;
using SIL.Windows.Forms.ClearShare;
using L10NSharp;
using SIL.IO;
using SIL.Xml;

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

		/// <summary>
		/// Returns a Dictionary keyed on image name that references a sorted set of page numbers where that image is used.
		/// We use string for the page numbers both for non-decimal numeral systems and because xmatter images will be
		/// referenced by page label (e.g. 'Front Cover').
		/// Public for testing.
		/// </summary>
		/// <param name="domBody"></param>
		/// <returns></returns>
		public Dictionary<string, SortedSet<string>> GetFilteredImageNameToPagesDictionary(XmlNode domBody)
		{
			var result = new Dictionary<string, SortedSet<string>>();
			result.AddRange(GetWhichImagesAreUsedOnWhichPages(domBody).Where(kvp => !DoNotPasteCreditsImages(kvp.Key)));
			return result;
		}

		private void HandleCopyImageCreditsForWholeBook(ApiRequest request)
		{
			// This method is called on a fileserver thread. To minimize the chance that the current selection somehow
			// changes while it is running, we capture the things that depend on it in variables right at the start.
			var domBody = _bookSelection.CurrentSelection.RawDom.DocumentElement.SelectSingleNode("//body");
			var imageNameToPages = GetFilteredImageNameToPagesDictionary(domBody);
			var currentSelectionFolderPath = _bookSelection.CurrentSelection.FolderPath;
			IEnumerable<string> langs;
			if (request.CurrentCollectionSettings != null)
				langs = request.CurrentCollectionSettings.LicenseDescriptionLanguagePriorities;
			else
				langs = new List<string> { "en" };		// emergency fall back -- probably never used.
			var credits = new Dictionary<string, SortedSet<string>>();
			var missingCredits = new List<string>();
			foreach (var kvp in imageNameToPages)
			{
				var path = currentSelectionFolderPath.CombineForPath(kvp.Key);
				if (!RobustFile.Exists(path))
					continue;

				var meta = Metadata.FromFile(path);
				string dummy;
				var credit = meta.MinimalCredits(langs, out dummy);
				if (string.IsNullOrEmpty(credit))
					missingCredits.Add(kvp.Key);
				if (!string.IsNullOrEmpty(credit))
				{
					var pageList = kvp.Value;
					BuildCreditsDictionary(credits, credit, pageList);
				}
			}
			var collectedCredits = CollectFormattedCredits(credits);
			var total = collectedCredits.Aggregate(new StringBuilder(), (all,credit) => {
				all.AppendFormat("<p>{0}</p>{1}", credit, System.Environment.NewLine);
				return all;
			});
			// Notify the user of images with missing credits.
			if (missingCredits.Count > 0)
			{
				var missing = LocalizationManager.GetString("EditTab.FrontMatter.PasteMissingCredits", "Missing credits:");
				var missingImage = LocalizationManager.GetString("EditTab.FrontMatter.ImageCreditMissing", " {0} (page {1})",
					"The {0} is replaced by the filename of an image.  The {1} is replaced by a reference to the first page in the book where that image occurs.");
				total.AppendFormat("<p>{0}", missing);
				for (var i = 0; i < missingCredits.Count; ++i)
				{
					if (i > 0)
						total.Append(",");
					total.AppendFormat(missingImage, missingCredits[i], imageNameToPages[missingCredits[i]].First());
				}
				total.AppendFormat("</p>{0}", System.Environment.NewLine);
			}
			request.ReplyWithText(total.ToString());
		}

		private IEnumerable<string> CollectFormattedCredits(Dictionary<string, SortedSet<string>> credits)
		{
			// Dictionary Key is metadata.MinimalCredits, Value is the list of pages that have images this credits string applies to.
			// Generate a formatted credit string like:
			//   Image on page 2 by John Doe, Copyright John Doe, 2016, CC-BY-NC. or
			//   Images on pages 1, 3, 4 by Art of Reading, Copyright SIL International 2017, CC-BY-SA.
			// The goal is to return one string for each credit source listing the pages that apply.
			var singleCredit = LocalizationManager.GetString("EditTab.FrontMatter.SingleImageCredit", "Image on page {0} by {1}.");
			var multipleCredit = LocalizationManager.GetString("EditTab.FrontMatter.MultipleImageCredit", "Images on pages {0} by {1}.");
			foreach (var kvp in credits.OrderBy(kvp => kvp.Value.First())) // sort by the earliest page number
			{
				var pages = kvp.Value.ToList();
				var credit = kvp.Key;
				if (pages.Count == 1)
				{
					// use singleCredit format
					yield return string.Format(singleCredit, pages[0], credit);
				}
				else
				{
					// use multipleCredit format
					var sb = new StringBuilder();
					for (var i = 0; i < pages.Count; i++)
					{
						if (i < pages.Count - 1)
						{
							sb.Append(pages[i] + ", ");
						}
						else
						{
							sb.Append(pages[i]);
						}
					}
					yield return string.Format(multipleCredit, sb, credit);
				}
			}
		}

		private static void BuildCreditsDictionary(Dictionary<string, SortedSet<string>> credits, string credit,
				SortedSet<string> setOfPageUsages)
		{
			// need to see if 'credit' string is already in dict
			// if not, add it along with the associated 'SortedSet<int> setOfPageUsages'
			// if yes, aggregate the new SortedSet<int> pages with what's already on file in the dict.
			SortedSet<string> oldPagesList;
			if (credits.TryGetValue(credit, out oldPagesList))
			{
				oldPagesList.AddRange(setOfPageUsages);
				credits[credit] = oldPagesList;
			}
			else
			{
				credits.Add(credit, setOfPageUsages);
			}
		}

		/// <summary>
		/// Determine whether or not a particular image should have its credits pasted.
		/// </summary>
		/// <param name="name"></param>
		/// <returns></returns>
		private bool DoNotPasteCreditsImages(string name)
		{
			// returns 'true' if 'name' is among the list of ones we don't want to paste image credits for
			// includes CC license image, placeholder and branding images
			return _doNotPasteArray.Contains(name.ToLowerInvariant());
		}

		/// <summary>
		/// Returns a Dictionary&lt;string, SortedSet&lt;string&gt;&gt; that contains each image name and a sorted set of page
		/// numbers that contain that image (with no duplicates).
		/// We use a sorted set of strings in order to handle other numeral systems.
		/// </summary>
		/// <param name="domBody"></param>
		public static Dictionary<string, SortedSet<string>> GetWhichImagesAreUsedOnWhichPages(XmlNode domBody)
		{
			var imageNameToPages = new Dictionary<string, SortedSet<string>>();
			foreach (XmlElement img in HtmlDom.SelectChildImgAndBackgroundImageElements(domBody as XmlElement))
			{
				var name = HtmlDom.GetImageElementUrl(img).PathOnly.NotEncoded;
				var pageNum = GetPageNumberForImageElement(img);
				if (string.IsNullOrWhiteSpace(pageNum))
					continue; // This image is on a page with no pagenumber or something is drastically wrong.
				SortedSet<string> currentSet;
				if (imageNameToPages.TryGetValue(name, out currentSet))
				{
					if (currentSet.Contains(pageNum))
						continue; // already got this image on this page

					currentSet.Add(pageNum);
				}
				else
				{
					var comparer = new NaturalSortComparer<string>();
					imageNameToPages.Add(name, new SortedSet<string>(comparer) { pageNum });
				}
			}
			return imageNameToPages;
		}

		private static string GetPageNumberForImageElement(XmlElement img)
		{
			const string pgNbrXpath = "ancestor::div[@data-page-number]";
			var pageNumNode = img.SelectSingleNode(pgNbrXpath);
			var pgNbr = pageNumNode?.GetStringAttribute("data-page-number");
			if (!string.IsNullOrWhiteSpace(pgNbr) && !IsBackMatter(pageNumNode))
			{
				return pgNbr;
			}
			const string pgLabelXpath = "ancestor::div/div[@class='pageLabel']";
			var labelNode = img.SelectSingleNode(pgLabelXpath);
			return labelNode?.InnerText.Trim();
		}

		private static bool IsBackMatter(XmlNode node)
		{
			return ((XmlElement) node).Attributes["class"].InnerText.Contains("bloom-backMatter");
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
