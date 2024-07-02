using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using Bloom.SafeXml;
using Bloom.Utils;
using L10NSharp;
using SIL.IO;
using SIL.Progress;
using SIL.Reporting;
using SIL.Windows.Forms.ClearShare;
using SIL.Windows.Forms.ImageToolbox;
using TagLib;

namespace Bloom.Book
{
    public class ImageUpdater
    {
        public static void CopyImageMetadataToWholeBook(
            string folderPath,
            HtmlDom dom,
            Metadata metadata,
            IProgress progress
        )
        {
            var filesWithProblems = new List<string>();
            Exception lastError = null;
            const int kMaxFilesToList = 4;

            progress.WriteStatus("Starting...");

            //First update the images themselves

            int completed = 0;
            var imgElements = GetImagePaths(folderPath);
            foreach (string path in imgElements)
            {
                progress.ProgressIndicator.PercentCompleted = (int)(
                    100.0 * (float)completed / imgElements.Count()
                );
                progress.WriteStatus("Copying to " + Path.GetFileName(path));

                try
                {
                    metadata.WriteIntellectualPropertyOnly(path);
                }
                catch (Exception e)
                {
                    lastError = e;
                    filesWithProblems.Add((Path.GetFileName(path)));
                }
                ++completed;
            }

            if (filesWithProblems.Count > 0)
            {
                // Don't overflow the screen with a needlessly long list of files if for some reason
                // there are huge numbers of failures.
                var namesToList = filesWithProblems.Take(kMaxFilesToList);
                // Purposefully not producing different messages if the list is being trimmed.
                //ErrorReport.ReportNonFatalExceptionWithMessage(lastError, "Bloom was not able to copy the metadata to {0} images: {1}. ref(BL-3214)", filesWithProblems.Count, string.Join(", ", namesToList));
                var list = string.Join(", ", namesToList);
                if (filesWithProblems.Count > kMaxFilesToList)
                {
                    list += ", ...";
                }
                var msg = LocalizationManager.GetString(
                    "Errors.CopyImageMetadata",
                    "Bloom was not able to copy the metadata to {0} image(s): {1}. Try restarting your computer. If that does not fix the problem, please send us a report and we will help you fix it."
                );
                ErrorReport.NotifyUserOfProblem(lastError, msg, filesWithProblems.Count, list);
            }

            //Now update the html attributes which echo some of it, and is used by javascript to overlay displays related to
            //whether the info is there or missing or whatever.

            foreach (SafeXmlElement img in dom.SafeSelectNodes("//img"))
            {
                UpdateImgMetadataAttributesToMatchImage(folderPath, img, progress, metadata);
            }
        }

        private static readonly string[] _imagesThatShouldBeSingletons = new string[]
        {
            "placeholder.png",
            "license.png"
        };

        public static bool IsPlaceholderOrLicense(string fileName)
        {
            return _imagesThatShouldBeSingletons.Contains(fileName.ToLowerInvariant());
        }

        private static readonly string[] ExcludedFiles =
        {
            "placeholder.png",
            "license.png",
            "thumbnail.png",
            "nonPaddedThumbnail.png"
        };

        /// <summary>
        /// We want all the images in the folder, except the above excluded files and any images that come from
        /// an official collection (such as Art of Reading) (BL-4578).
        /// </summary>
        /// <param name="folderPath"></param>
        /// <returns></returns>
        private static IEnumerable<string> GetImagePaths(string folderPath)
        {
            foreach (
                var path in Directory
                    .EnumerateFiles(folderPath)
                    .Where(
                        s =>
                            s.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
                            || s.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
                    )
            )
            {
                if (ExcludedFiles.Contains(Path.GetFileName(path).ToLowerInvariant()))
                    continue;
                var metaData = RobustFileIO.MetadataFromFile(path);
                if (metaData != null && ImageIsFromOfficialCollection(metaData))
                    continue;

                yield return path;
            }
        }

        /// <summary>
        /// Gets the PalasoImage info from the image's filename and folder. If there is a problem, it will return null.
        /// </summary>
        /// <param name="folderPath"></param>
        /// <param name="imageFilePath"></param>
        /// <returns></returns>
        public static PalasoImage GetImageInfoSafelyFromFilePath(
            string folderPath,
            string imageFilePath
        )
        {
            //enhance: this all could be done without loading the image into memory
            //could just deal with the metadata
            //e.g., var metadata = RobustFileIO.MetadataFromFile(path)
            var path = Path.Combine(folderPath, imageFilePath);
            try
            {
                return PalasoImage.FromFileRobustly(path);
            }
            catch (Exception e)
            {
                var msgFmt = LocalizationManager.GetString(
                    "Errors.CorruptImageFile",
                    "The image file {0} is corrupt and needs to be replaced. ({1})"
                );
                string msg = string.Format(msgFmt, Path.GetFileName(path), e.Message);
                Logger.WriteEvent(
                    $"Book.UpdateImgMetadataAttributesToMatchImage() Image {path} is corrupt: {e.Message}"
                );
                NonFatalProblem.Report(
                    ModalIf.All,
                    PassiveIf.None,
                    msg,
                    null,
                    null,
                    false,
                    false,
                    true
                );
                return null;
            }
        }

        /// <summary>
        /// Returns true if image metadata has an official collectionUri.
        /// </summary>
        /// <param name="metadata"></param>
        /// <returns></returns>
        internal static bool ImageIsFromOfficialCollection(Metadata metadata)
        {
            return !String.IsNullOrEmpty(metadata.CollectionUri);
        }

        internal static bool ImageHasMetadata(PalasoImage imageInfo)
        {
            return !(imageInfo.Metadata == null || imageInfo.Metadata.IsEmpty);
        }

        public static void UpdateImgMetadataAttributesToMatchImage(
            string folderPath,
            SafeXmlElement imgElement,
            IProgress progress
        )
        {
            UpdateImgMetadataAttributesToMatchImage(folderPath, imgElement, progress, null);
        }

        public static void UpdateImgMetadataAttributesToMatchImage(
            string folderPath,
            SafeXmlElement imgElement,
            IProgress progress,
            Metadata metadata
        )
        {
            //see also PageEditingModel.UpdateMetadataAttributesOnImage(), which does the same thing but on the browser dom
            var url = HtmlDom.GetImageElementUrl(imgElement);
            string fileName = url.PathOnly.NotEncoded;
            if (IsPlaceholderOrLicense(fileName))
                return;
            if (string.IsNullOrEmpty(fileName))
            {
                Logger.WriteEvent(
                    "Book.UpdateImgMetdataAttributesToMatchImage() Warning: img has no or empty src attribute"
                );
                //Debug.Fail(" (Debug only) img has no or empty src attribute");
                return; // they have bigger problems, which aren't appropriate to deal with here.
            }

            if (metadata == null)
            {
                // The fileName might be URL encoded.  See https://silbloom.myjetbrains.com/youtrack/issue/BL-3901.
                var path = UrlPathString.GetFullyDecodedPath(folderPath, ref fileName);
                progress.WriteStatus("Reading metadata from " + fileName);
                if (!RobustFile.Exists(path)) // they have bigger problems, which aren't appropriate to deal with here.
                {
                    imgElement.RemoveAttribute("data-copyright");
                    imgElement.RemoveAttribute("data-creator");
                    imgElement.RemoveAttribute("data-license");
                    Logger.WriteEvent(
                        "Book.UpdateImgMetdataAttributesToMatchImage()  Image "
                            + path
                            + " is missing"
                    );
                    //Debug.Fail(" (Debug only) Image " + path + " is missing");
                    return;
                }
                try
                {
                    metadata = RobustFileIO.MetadataFromFile(path);
                }
                catch (UnauthorizedAccessException e)
                {
                    throw new BloomUnauthorizedAccessException(path, e);
                }
                catch (Exception e)
                {
                    imgElement.RemoveAttribute("data-copyright");
                    imgElement.RemoveAttribute("data-creator");
                    imgElement.RemoveAttribute("data-license");
                    Logger.WriteEvent(
                        $"Book.UpdateImgMetadataAttributesToMatchImage() Image {path} is corrupt: {e.Message}"
                    );
                    var msgFmt = LocalizationManager.GetString(
                        "Errors.CorruptImageFile",
                        "The image file {0} is corrupt and needs to be replaced. ({1})"
                    );
                    string msg = string.Format(msgFmt, fileName, e.Message);
                    NonFatalProblem.Report(
                        ModalIf.All,
                        PassiveIf.None,
                        msg,
                        null,
                        null,
                        false,
                        false,
                        true
                    );
                    return;
                }
            }

            progress.WriteStatus("Writing metadata to HTML for " + fileName);

            imgElement.SetAttribute(
                "data-copyright",
                String.IsNullOrEmpty(metadata.CopyrightNotice) ? "" : metadata.CopyrightNotice
            );
            imgElement.SetAttribute(
                "data-creator",
                String.IsNullOrEmpty(metadata.Creator) ? "" : metadata.Creator
            );
            imgElement.SetAttribute(
                "data-license",
                metadata.License == null ? "" : metadata.License.ToString()
            );
        }

        /// <summary>
        /// We mirror several metadata tags in the html for quick access by the UI.
        /// This method makes sure they are all up to date.
        /// </summary>
        /// <param name="progress"> </param>
        public static void UpdateAllHtmlDataAttributesForAllImgElements(
            string folderPath,
            HtmlDom dom,
            IProgress progress
        )
        {
            //Update the html attributes which echo some of it, and is used by javascript to overlay displays related to
            //whether the info is there or missing or whatever.

            var imgElements = HtmlDom.SelectChildImgAndBackgroundImageElements(
                dom.RawDom.DocumentElement
            );
            int completed = 0;
            foreach (SafeXmlElement img in imgElements)
            {
                progress.ProgressIndicator.PercentCompleted = (int)(
                    100.0 * (float)completed / (float)imgElements.Length
                );
                UpdateImgMetadataAttributesToMatchImage(folderPath, img, progress);
                completed++;
            }
        }
    }
}
