using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using SIL.CommandLineProcessing;
using SIL.Extensions;
using SIL.IO;
using SIL.Progress;
using SIL.Reporting;
using SIL.Windows.Forms.ClearShare;

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

		public static IEnumerable<string> GetImagePaths(string folderPath)
		{
			foreach (var path in Directory.EnumerateFiles(folderPath).Where(s => s.EndsWith(".png", StringComparison.OrdinalIgnoreCase) || s.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)))
			{
				if ((path.ToLowerInvariant() == "placeholder.png") || path.ToLowerInvariant() == ("license.png")
					|| path.ToLowerInvariant() == ("thumbnail.png"))
					continue;
				yield return path;
			}
		}

		public static void UpdateImgMetdataAttributesToMatchImage(string folderPath, XmlElement imgElement, IProgress progress)
		{
			UpdateImgMetdataAttributesToMatchImage(folderPath, imgElement, progress, null);
		}

		public static void UpdateImgMetdataAttributesToMatchImage(string folderPath, XmlElement imgElement, IProgress progress, Metadata metadata)
		{
			//see also PageEditingModel.UpdateMetadataAttributesOnImage(), which does the same thing but on the browser dom
			var url = HtmlDom.GetImageElementUrl(new ElementProxy(imgElement));
			var end = url.NotEncoded.IndexOf('?');
			string fileName = url.NotEncoded;
			if (end > 0)
			{
				fileName = fileName.Substring(0, end);
			}
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
			var pngoutPath = FileLocator.LocateExecutable("optipng.exe");
			var result = CommandLineRunner.Run(pngoutPath, "\"" + path + "\"", Encoding.UTF8,
				Path.GetDirectoryName(path), 300, progress,
				(s) => progress.WriteMessage(s));
		}
	}
}
