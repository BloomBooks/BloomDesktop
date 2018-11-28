using System;
using System.IO;
using Bloom.Book;
using Bloom.ImageProcessing;
using SIL.IO;
using SIL.Progress;
using SIL.Windows.Forms.ImageToolbox;

namespace Bloom.Edit
{
	public class PageEditingModel
	{
		/// <summary>
		///
		/// </summary>
		/// <param name="bookFolderPath"></param>
		/// <param name="imgOrDivWithBackgroundImage">Can be an XmlElement (during testing)</param>
		/// <param name="imageInfo"></param>
		/// <param name="progress"></param>
		public void ChangePicture(string bookFolderPath, ElementProxy imgOrDivWithBackgroundImage, PalasoImage imageInfo,
			IProgress progress)
		{
			var isSameFile = IsSameFilePath(bookFolderPath, HtmlDom.GetImageElementUrl(imgOrDivWithBackgroundImage), imageInfo);
			var imageFileName = ImageUtils.ProcessAndSaveImageIntoFolder(imageInfo, bookFolderPath, isSameFile);
			HtmlDom.SetImageElementUrl(imgOrDivWithBackgroundImage,
				UrlPathString.CreateFromUnencodedString(imageFileName, true));
			UpdateMetadataAttributesOnImage(imgOrDivWithBackgroundImage, imageInfo);
		}

		/// <summary>
		/// Check whether the new image file is the same as the one we already have chosen.
		/// (or at least the same pathname in the filesystem)
		/// </summary>
		/// <remarks>
		/// See https://silbloom.myjetbrains.com/youtrack/issue/BL-2776.
		/// If the user goes out of his way to choose the exact same picture file from the
		/// original location again, a copy will still be created with a slightly revised
		/// name.  Cropping a picture also results in a new copy of the file with the
		/// revised name.  We still need a tool to remove unused picture files from a
		/// book's folder.  (ie, BL-2351)
		/// </remarks>
		private bool IsSameFilePath(string bookFolderPath, UrlPathString src, PalasoImage imageInfo)
		{
			if (src!=null)
			{
				var path = Path.Combine(bookFolderPath, src.NotEncoded);
				if (path == imageInfo.OriginalFilePath)
					return true;
			}
			return false;
		}

		public static void UpdateMetadataAttributesOnImage(ElementProxy imgOrDivWithBackgroundImage, PalasoImage imageInfo)
		{
			//see also Book.UpdateMetadataAttributesOnImage(), which does the same thing but on the document itself, not the browser dom
			imgOrDivWithBackgroundImage.SetAttribute("data-copyright",
							 String.IsNullOrEmpty(imageInfo.Metadata.CopyrightNotice) ? "" : imageInfo.Metadata.CopyrightNotice);

			imgOrDivWithBackgroundImage.SetAttribute("data-creator", String.IsNullOrEmpty(imageInfo.Metadata.Creator) ? "" : imageInfo.Metadata.Creator);


			imgOrDivWithBackgroundImage.SetAttribute("data-license", imageInfo.Metadata.License == null ? "" : imageInfo.Metadata.License.ToString());
		}

		/*
		 * /// <summary>
		/// NB: ideally, this would just be PreserveHtmlOfElement but for now, this actually only copies the @class over
		/// </summary>
		public void PreserveClassAttributeOfElement(XmlElement pageElement, string elementHtml)
		{
			XmlElement incoming = XmlHtmlConverter.GetXmlDomFromHtml(elementHtml, false).SelectSingleNode("//body").FirstChild as XmlElement;
			string id = incoming.GetStringAttribute("id");
			XmlElement elementToChange = pageElement.SelectSingleNode("//*[@id='" + id + "']") as XmlElement;
			var newClassContent =  incoming.GetAttribute("class");
			elementToChange.SetAttribute("class", newClassContent);
		}*/
	}
}
