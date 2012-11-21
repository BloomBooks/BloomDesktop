using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using Palaso.CommandLineProcessing;
using Palaso.Extensions;
using Palaso.IO;
using Palaso.Progress;
using Palaso.Reporting;
using Palaso.UI.WindowsForms.ClearShare;
using Palaso.UI.WindowsForms.ImageToolbox;
using Palaso.Xml;

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
				using (var image = PalasoImage.FromFile(path))
				{
					image.Metadata = metadata;
					image.SaveUpdatedMetadataIfItMakesSense();
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
				if ((path.ToLower() == "placeholder.png") || path.ToLower() == ("license.png") || path.ToLower() == ("thumbnail.png"))
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
			var fileName = imgElement.GetOptionalStringAttribute("src", string.Empty).ToLower();
			if (fileName == "placeholder.png" || fileName == "license.png")
				return;

			if (string.IsNullOrEmpty(fileName))
			{
				Logger.WriteEvent("Book.UpdateImgMetdataAttributesToMatchImage() Warning: img has no or empty src attribute");
				//Debug.Fail(" (Debug only) img has no or empty src attribute");
				return; // they have bigger problems, which aren't appropriate to deal with here.
			}
			if (metadata == null)
			{
				progress.WriteStatus("Reading metadata from " + fileName);
				var path = folderPath.CombineForPath(fileName);
				if (!File.Exists(path)) // they have bigger problems, which aren't appropriate to deal with here.
				{
					imgElement.RemoveAttribute("data-copyright");
					imgElement.RemoveAttribute("data-creator");
					imgElement.RemoveAttribute("data-license");
					Logger.WriteEvent("Book.UpdateImgMetdataAttributesToMatchImage()  Image " + path + " is missing");
					Debug.Fail(" (Debug only) Image " + path + " is missing");
					return;
				}
				using (var image = PalasoImage.FromFile(path))
				{
					metadata = image.Metadata;
				}
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
		public static void UpdateAllHtmlDataAttributesForAllImgElements(string folderPath, XmlDocument dom, IProgress progress)
		{
			//Update the html attributes which echo some of it, and is used by javascript to overlay displays related to
			//whether the info is there or missing or whatever.

			var imgElements = dom.SafeSelectNodes("//img");
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
				progress.ProgressIndicator.PercentCompleted = (int)(100.0 * (float)completed / (float)imageFiles.Length);
				CompressImage(path, progress);
				completed++;
			}
		}

		public static void CompressImage(string path, IProgress progress)
		{
			progress.WriteStatus("Compressing image: " + Path.GetFileName(path));
			var pngoutPath = FileLocator.GetFileDistributedWithApplication("optipng.exe");
			var result = CommandLineRunner.Run(pngoutPath, "\"" + path + "\"", Encoding.UTF8, Path.GetDirectoryName(path), 300, progress,
											   (s) => progress.WriteMessage(s));
		}
	}
}
