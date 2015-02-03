using System;
using System.Xml;
using Bloom.ImageProcessing;
using Palaso.Progress;
using Palaso.UI.WindowsForms.ImageToolbox;
using Palaso.Xml;
using Gecko;

namespace Bloom.Edit
{
	public class PageEditingModel
	{
		public void ChangePicture(string bookFolderPath, GeckoHtmlElement img, PalasoImage imageInfo, IProgress progress)
		{
			var imageFileName = ImageUtils.ProcessAndSaveImageIntoFolder(imageInfo, bookFolderPath);
			img.SetAttribute("src", imageFileName);
			UpdateMetdataAttributesOnImgElement(img, imageInfo);
		}


		/// <summary>
		/// for testing.... todo: maybe they should test ProcessAndSaveImageIntoFolder() directly, instead
		/// </summary>
		public void ChangePicture(string bookFolderPath, XmlDocument dom, string imageId, PalasoImage imageInfo)
		{

			var matches = dom.SafeSelectNodes("//img[@id='" + imageId + "']");
			XmlElement img = matches[0] as XmlElement;

			var imageFileName = ImageUtils.ProcessAndSaveImageIntoFolder(imageInfo, bookFolderPath);
			img.SetAttribute("src", imageFileName);

		}

	

		
		public void UpdateMetdataAttributesOnImgElement(GeckoHtmlElement img, PalasoImage imageInfo)
		{
			UpdateMetadataAttributesOnImage(img, imageInfo);

			img.Click(); //wake up javascript to update overlays
		}

		public static void UpdateMetadataAttributesOnImage(GeckoElement img, PalasoImage imageInfo)
		{
			//see also Book.UpdateMetadataAttributesOnImage(), which does the same thing but on the document itself, not the browser dom
			img.SetAttribute("data-copyright",
							 String.IsNullOrEmpty(imageInfo.Metadata.CopyrightNotice) ? "" : imageInfo.Metadata.CopyrightNotice);

			img.SetAttribute("data-creator", String.IsNullOrEmpty(imageInfo.Metadata.Creator) ? "" : imageInfo.Metadata.Creator);


			img.SetAttribute("data-license", imageInfo.Metadata.License == null ? "" : imageInfo.Metadata.License.ToString());
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
