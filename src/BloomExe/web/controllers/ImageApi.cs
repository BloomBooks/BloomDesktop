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
using SIL.Code;
using SIL.Extensions;
using SIL.Reporting;
using SIL.Windows.Forms.ClearShare;
using L10NSharp;
using SIL.IO;
using SIL.Xml;
using Bloom.SafeXml;

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
            // Handles all except placeholder images. They can show up with digits after them, so we do them differently.
            var imageFiles = new HashSet<string> { "license.png" };
            var brandingDirectory = BloomFileLocator.GetBrowserDirectory("branding");
            foreach (var brandDirectory in Directory.GetDirectories(brandingDirectory))
            {
                imageFiles.AddRange(
                    Directory
                        .EnumerateFiles(brandDirectory)
                        .Where(IsSvgOrPng)
                        .Select(Path.GetFileName)
                );
            }
            return imageFiles;
        }

        private static bool IsSvgOrPng(string filename)
        {
            var lcFilename = filename.ToLowerInvariant();
            return Path.GetExtension(lcFilename) == ".svg"
                || Path.GetExtension(lcFilename) == ".png";
        }

        public void RegisterWithApiHandler(BloomApiHandler apiHandler)
        {
            // These are both just retrieving information about files, apart from using _bookSelection.CurrentSelection.FolderPath.
            apiHandler.RegisterEndpointHandler("image/info", HandleImageInfo, false);
            apiHandler.RegisterEndpointHandler(
                "image/imageCreditsForWholeBook",
                HandleCopyImageCreditsForWholeBook,
                false
            );
        }

        /// <summary>
        /// Returns a Dictionary keyed on image name that references an ordered set of page numbers where that image is used.
        /// We use string for the page numbers both for non-decimal numeral systems and because xmatter images will be
        /// referenced by page label (e.g. 'Front Cover').
        /// Public for testing.
        /// </summary>
        /// <param name="domBody"></param>
        /// <returns></returns>
        public Dictionary<string, List<string>> GetFilteredImageNameToPagesDictionary(
            SafeXmlNode domBody,
            IEnumerable<string> langs = null
        )
        {
            var result = new Dictionary<string, List<string>>();
            result.AddRange(
                GetWhichImagesAreUsedOnWhichPages(domBody, langs)
                    .Where(kvp => !DoNotPasteCreditsImages(kvp.Key))
            );
            return result;
        }

        private void HandleCopyImageCreditsForWholeBook(ApiRequest request)
        {
            // This method is called on a fileserver thread. To minimize the chance that the current selection somehow
            // changes while it is running, we capture the things that depend on it in variables right at the start.
            var domBody = _bookSelection.CurrentSelection.RawDom.DocumentElement.SelectSingleNode(
                "//body"
            );
            var currentSelectionFolderPath = _bookSelection.CurrentSelection.FolderPath;
            IEnumerable<string> langs;
            if (request.CurrentCollectionSettings != null)
                langs =
                    _bookSelection.CurrentSelection.GetLanguagePrioritiesForLocalizedTextOnPage();
            else
                langs = new List<string> { "en" }; // emergency fall back -- probably never used.
            var imageNameToPages = GetFilteredImageNameToPagesDictionary(domBody, langs);
            var credits = new Dictionary<string, List<string>>();
            var missingCredits = new List<string>();
            foreach (var kvp in imageNameToPages)
            {
                var path = currentSelectionFolderPath.CombineForPath(kvp.Key);
                if (!RobustFile.Exists(path))
                    continue;

                var meta = RobustFileIO.MetadataFromFile(path);
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
            var collectedCredits = CollectFormattedCredits(credits, langs);
            var total = collectedCredits.Aggregate(
                new StringBuilder(),
                (all, credit) =>
                {
                    all.AppendFormat("<p>{0}</p>{1}", credit, Environment.NewLine);
                    return all;
                }
            );
            // Notify the user of images with missing credits.
            if (missingCredits.Count > 0)
            {
                string dummyId;
                var missing = LocalizationManager.GetString(
                    "EditTab.FrontMatter.PasteMissingCredits",
                    "Missing credits:",
                    "",
                    langs,
                    out dummyId
                );
                var missingImage = LocalizationManager.GetString(
                    "EditTab.FrontMatter.ImageCreditMissing",
                    " {0} (page {1})",
                    "The {0} is replaced by the filename of an image.  The {1} is replaced by a reference to the first page in the book where that image occurs.",
                    langs,
                    out dummyId
                );
                total.AppendFormat("<p>{0}", missing);
                for (var i = 0; i < missingCredits.Count; ++i)
                {
                    if (i > 0)
                        total.Append(",");
                    total.AppendFormat(
                        missingImage,
                        missingCredits[i],
                        imageNameToPages[missingCredits[i]].First()
                    );
                }
                total.AppendFormat("</p>{0}", System.Environment.NewLine);
            }
            request.ReplyWithText(total.ToString());
        }

        /// <summary>
        /// Internal for testing.
        /// </summary>
        /// <param name="credits"></param>
        /// <returns></returns>
        internal static IEnumerable<string> CollectFormattedCredits(
            Dictionary<string, List<string>> credits,
            IEnumerable<string> langs = null
        )
        {
            if (langs == null)
                langs = new string[] { "en" };
            string dummyId;
            // Dictionary Key is metadata.MinimalCredits, Value is the list of pages that have images this credits string applies to.
            // Generate a formatted credit string like:
            //   Image on page 2 by John Doe, Copyright John Doe, 2016, CC-BY-NC. or
            //   Images on pages 1, 3-4 by Art of Reading, Copyright SIL International 2017, CC-BY-SA.
            // The goal is to return one string for each credit source listing the pages that apply.
            if (credits.Keys.Count == 1)
            {
                // If all the images are credited to the same illustrator, don't bother listing page numbers.
                // Like: Images by John Doe, Copyright John Doe, 2016, CC-BY-NC.
                var bookSingleCredit = LocalizationManager.GetString(
                    "EditTab.FrontMatter.BookSingleCredit",
                    "Images by {0}.",
                    "",
                    langs,
                    out dummyId
                );
                var key = credits.Keys.First();
                yield return string.Format(bookSingleCredit, key);
                yield break;
            }
            var singleCredit = LocalizationManager.GetString(
                "EditTab.FrontMatter.SingleImageCredit",
                "Image on page {0} by {1}.",
                "",
                langs,
                out dummyId
            );
            var multipleCredit = LocalizationManager.GetString(
                "EditTab.FrontMatter.MultipleImageCredit",
                "Images on pages {0} by {1}.",
                "",
                langs,
                out dummyId
            );
            foreach (var kvp in credits) // we assume here that credits is built in page order
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
                    var mpr = CreateMultiplePageReference(pages);
                    yield return string.Format(multipleCredit, mpr, credit);
                }
            }
        }

        private static string CreateMultiplePageReference(List<string> pages)
        {
            const string ndash = "\u2013";
            const string commaSpace = ", ";
            bool workingOnDash = false;
            var sb = new StringBuilder();
            sb.Append(pages[0]);
            int previousPage;
            if (!Int32.TryParse(pages[0], out previousPage))
                previousPage = -1;
            for (var i = 1; i < pages.Count; i++)
            {
                var thisPage = pages[i];
                if (PageShouldBeGroupedWithPreviousPage(previousPage, thisPage))
                {
                    if (!workingOnDash)
                    {
                        workingOnDash = true;
                        sb.Append(ndash);
                    }
                }
                else
                {
                    if (workingOnDash)
                    {
                        sb.Append(previousPage.ToString());
                        workingOnDash = false;
                    }
                    sb.Append(commaSpace + thisPage);
                }
                if (!Int32.TryParse(thisPage, out previousPage))
                    previousPage = -1;
            }
            if (workingOnDash)
            {
                sb.Append(previousPage.ToString());
            }
            return sb.ToString();
        }

        private static bool PageShouldBeGroupedWithPreviousPage(int prevPage, string page)
        {
            int nextPageNum;
            if (!Int32.TryParse(page, out nextPageNum))
            {
                return false;
            }
            return nextPageNum == prevPage + 1;
        }

        private static void BuildCreditsDictionary(
            Dictionary<string, List<string>> credits,
            string credit,
            List<string> listOfPageUsages
        )
        {
            // need to see if 'credit' string is already in dict
            // if not, add it along with the associated 'List<string> listOfPageUsages'
            // if yes, aggregate the new List<string> pages with what's already on file in the dict.
            List<string> oldPagesList;
            if (credits.TryGetValue(credit, out oldPagesList))
            {
                oldPagesList.AddRange(listOfPageUsages);
                credits[credit] = oldPagesList;
            }
            else
            {
                credits.Add(credit, listOfPageUsages);
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
            return _doNotPasteArray.Contains(name.ToLowerInvariant())
                || name.ToLowerInvariant().StartsWith("placeholder");
        }

        /// <summary>
        /// Returns a Dictionary that contains each image name as a key and a list of page
        /// numbers that contain that image (with no duplicates and in order of occurrence).
        /// </summary>
        /// <param name="domBody"></param>
        public static Dictionary<string, List<string>> GetWhichImagesAreUsedOnWhichPages(
            SafeXmlNode domBody,
            IEnumerable<string> langs
        )
        {
            var imageNameToPages = new Dictionary<string, List<string>>();
            foreach (
                SafeXmlElement img in HtmlDom.SelectChildImgAndBackgroundImageElements(
                    domBody as SafeXmlElement
                )
            )
            {
                if (IsImgNotInAPage(img))
                    continue;
                if (IsImgInsideBrandingElement(img))
                    continue;
                var name = HtmlDom.GetImageElementUrl(img).PathOnly.NotEncoded;
                var pageNum = HtmlDom.GetNumberOrLabelOfPageWhereElementLives(img, langs);
                if (string.IsNullOrWhiteSpace(pageNum))
                    continue; // This image is on a page with no pagenumber or something is drastically wrong.
                List<string> currentList;
                if (imageNameToPages.TryGetValue(name, out currentList))
                {
                    if (currentList.Contains(pageNum))
                        continue; // already got this image on this page

                    currentList.Add(pageNum);
                }
                else
                {
                    imageNameToPages.Add(name, new List<string> { pageNum });
                }
            }
            return imageNameToPages;
        }

        private static bool IsImgNotInAPage(SafeXmlElement imgElement)
        {
            return !(
                imgElement.SelectSingleNode("ancestor-or-self::div[contains(@class,'bloom-page')]")
                is SafeXmlElement
            );
        }

        private static bool IsImgInsideBrandingElement(SafeXmlElement imgElement)
        {
            return imgElement.SelectSingleNode(
                    "ancestor-or-self::div[contains(@data-book,'branding')]"
                ) is SafeXmlElement;
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
                while (!RobustFile.Exists(path) && fileName.Contains('%'))
                {
                    var fileName1 = fileName;
                    // We can be fed doubly-encoded filenames.  So try to decode a second time and see if that works.
                    // See https://silbloom.myjetbrains.com/youtrack/issue/BL-3749.
                    // Effectively triple-encoded filenames have cropped up for particular books.  Such files are
                    // already handled okay by BloomServer.ProcessAnyFileContent().  This code can handle
                    // any depth of url-encoding.
                    // See https://silbloom.myjetbrains.com/youtrack/issue/BL-5757.
                    fileName = System.Web.HttpUtility.UrlDecode(fileName);
                    if (fileName == fileName1)
                        break;
                    path = Path.Combine(_bookSelection.CurrentSelection.FolderPath, fileName);
                }
                dynamic result = new ExpandoObject();
                result.name = fileName;
                if (!RobustFile.Exists(path))
                {
                    result.bytes = -1;
                    result.width = -1;
                    result.height = -1;
                    result.bitDepth = "unknown";
                }
                else if (path.ToLowerInvariant().EndsWith("svg"))
                {
                    result.bytes = -1;
                    result.width = -1;
                    result.height = -1;
                    result.bitDepth = "unknown";
                }
                else
                {
                    var fileInfo = new FileInfo(path);
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
