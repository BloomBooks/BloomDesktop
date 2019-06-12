using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using SIL.CommandLineProcessing;
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
		public static void CopyImageMetadataToWholeBook(string folderPath, HtmlDom dom, Metadata metadata, IProgress progress)
		{
			progress.WriteStatus("Starting...");

			//First update the images themselves

			int completed = 0;
			var imgElements = GetImagePaths(folderPath);
			foreach (string path in imgElements)
			{
				progress.ProgressIndicator.PercentCompleted = (int)(100.0 * (float)completed / imgElements.Count());
				progress.WriteStatus("Copying to " + Path.GetFileName(path));

				try
				{
					metadata.WriteIntellectualPropertyOnly(path);
				}
				catch (TagLib.CorruptFileException e)
				{
					NonFatalProblem.Report(ModalIf.Beta, PassiveIf.All,"Image metadata problem", "Bloom had a problem accessing the metadata portion of this image " + path+ "  ref(BL-3214)", e);
				}

				++completed;
			}

			//Now update the html attributes which echo some of it, and is used by javascript to overlay displays related to
			//whether the info is there or missing or whatever.

			foreach (XmlElement img in dom.SafeSelectNodes("//img"))
			{
				UpdateImgMetdataAttributesToMatchImage(folderPath, img, progress, metadata);
			}
		}

		private static readonly string[] ExcludedFiles = { "placeholder.png", "license.png", "thumbnail.png" };

		/// <summary>
		/// We want all the images in the folder, except the above excluded files and any images that come from
		/// an official collection (such as Art of Reading) (BL-4578).
		/// </summary>
		/// <param name="folderPath"></param>
		/// <returns></returns>
		private static IEnumerable<string> GetImagePaths(string folderPath)
		{
			foreach (var path in Directory.EnumerateFiles(folderPath).Where(s => s.EndsWith(".png", StringComparison.OrdinalIgnoreCase) || s.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)))
			{
				if (ExcludedFiles.Contains(path.ToLowerInvariant()))
					continue;
				var metaData = Metadata.FromFile(path);
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
		public static PalasoImage GetImageInfoSafelyFromFilePath(string folderPath, string imageFilePath)
		{
			//enhance: this all could be done without loading the image into memory
			//could just deal with the metadata
			//e.g., var metadata = Metadata.FromFile(path)
			var path = Path.Combine(folderPath, imageFilePath);
			PalasoImage imageInfo = null;
			try
			{
				return PalasoImage.FromFileRobustly(path);
			}
			catch (CorruptFileException e)
			{
				ErrorReport.NotifyUserOfProblem(e,
					"Bloom ran into a problem while trying to read the metadata portion of this image, " + path);
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

		public static void UpdateImgMetdataAttributesToMatchImage(string folderPath, XmlElement imgElement, IProgress progress)
		{
			UpdateImgMetdataAttributesToMatchImage(folderPath, imgElement, progress, null);
		}

		public static void UpdateImgMetdataAttributesToMatchImage(string folderPath, XmlElement imgElement, IProgress progress, Metadata metadata)
		{
			//see also PageEditingModel.UpdateMetadataAttributesOnImage(), which does the same thing but on the browser dom
			var url = HtmlDom.GetImageElementUrl(new ElementProxy(imgElement));
			string fileName = url.PathOnly.NotEncoded;
			if (fileName.ToLowerInvariant() == "placeholder.png" || fileName.ToLowerInvariant() == "license.png")
				return;
			if (string.IsNullOrEmpty(fileName))
			{
				Logger.WriteEvent("Book.UpdateImgMetdataAttributesToMatchImage() Warning: img has no or empty src attribute");
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
					Logger.WriteEvent("Book.UpdateImgMetdataAttributesToMatchImage()  Image " + path + " is missing");
					//Debug.Fail(" (Debug only) Image " + path + " is missing");
					return;
				}
				metadata = RobustIO.MetadataFromFile(path);
			}

			progress.WriteStatus("Writing metadata to HTML for " + fileName);

			imgElement.SetAttribute("data-copyright",
							 String.IsNullOrEmpty(metadata.CopyrightNotice) ? "" : metadata.CopyrightNotice);
			imgElement.SetAttribute("data-creator", String.IsNullOrEmpty(metadata.Creator) ? "" : metadata.Creator);
			imgElement.SetAttribute("data-license", metadata.License == null ? "" : metadata.License.ToString());
		}

		/// <summary>
		/// We mirror several metadata tags in the html for quick access by the UI.
		/// This method makes sure they are all up to date.
		/// </summary>
		/// <param name="progress"> </param>
		public static void UpdateAllHtmlDataAttributesForAllImgElements(string folderPath, HtmlDom dom, IProgress progress)
		{
			//Update the html attributes which echo some of it, and is used by javascript to overlay displays related to
			//whether the info is there or missing or whatever.

			var imgElements = HtmlDom.SelectChildImgAndBackgroundImageElements(dom.RawDom.DocumentElement);
			int completed = 0;
			foreach (XmlElement img in imgElements)
			{
				progress.ProgressIndicator.PercentCompleted = (int)(100.0 * (float)completed / (float)imgElements.Count);
				UpdateImgMetdataAttributesToMatchImage(folderPath, img, progress);
				completed++;
			}
		}

		public static void CompressImages(string folderPath, IProgress progress)
		{
			var imageFiles = Directory.GetFiles(folderPath, "*.png");
			int completed = 0;
			foreach (string path in imageFiles)
			{
				if (Path.GetFileName(path).ToLowerInvariant() == "placeholder.png")
					return;

				progress.ProgressIndicator.PercentCompleted = (int)(100.0 * (float)completed / (float)imageFiles.Length);
				CompressImage(path, progress);
				completed++;
			}
		}

		public static void CompressImage(string path, IProgress progress)
		{
			progress.WriteStatus("Compressing image: " + Path.GetFileName(path));
			var pngoutPath = FileLocationUtilities.LocateExecutable("optipng.exe");
			var result = CommandLineRunner.Run(pngoutPath, "\"" + path + "\"", Encoding.UTF8,
				Path.GetDirectoryName(path), 300, progress,
				(s) => progress.WriteMessage(s));
		}
	}
}
