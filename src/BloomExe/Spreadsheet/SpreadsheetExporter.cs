using Bloom.Book;
using L10NSharp;
using SIL.Xml;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml;

namespace Bloom.Spreadsheet
{
	public class SpreadsheetExporter
	{
		InternalSpreadsheet _spreadsheet = new InternalSpreadsheet();

		//a list of values which, if they occur in the data-book attribute of an element in the bloomDataDiv,
		//indicate that the element content should be treated as an image, even though the element doesn't
		//have a src attribute nor actually contain an img element
		public static List<string> DataDivImagesWithNoSrcAttributes = new List<string>() { "licenseImage" };

		public SpreadsheetExportParams Params = new SpreadsheetExportParams();
		public InternalSpreadsheet Export(HtmlDom dom, string imagesFolderPath)
		{
			_spreadsheet.Params = Params;
			var pages = dom.GetPageElements();

			//Get xmatter
			var dataDiv = GetDataDiv(dom);
			AddDataDivData(dataDiv, imagesFolderPath);

			var iContentPage = 0;
			foreach (var page in pages)
			{
				var pageNumber = page.Attributes["data-page-number"]?.Value ?? "";
				// For now we will ignore all un-numbered pages, particularly xmatter,
				// which was handled above by exporting data div data.
				if (pageNumber == "")
					continue;

				//Each page alternates colors
				var colorForPage = iContentPage++ % 2 == 0 ? InternalSpreadsheet.AlternatingRowsColor1 : InternalSpreadsheet.AlternatingRowsColor2;
				AddContentRows(page, pageNumber, imagesFolderPath, colorForPage);
			}
			_spreadsheet.SortHiddenRowsToTheBottom();
			return _spreadsheet;
		}

		private void AddContentRows(XmlElement page, string pageNumber, string imagesFolderPath, Color colorForPage)
		{
			var imageContainers = GetImageContainers(page);
			var groups = TranslationGroupManager.SortTranslationGroups(TranslationGroupManager.GetTranslationGroups(page));
						
			var pageContentTuples = imageContainers.ZipLongest(groups, (imageContainer, group) => (imageContainer, group));
			foreach(var pageContent in pageContentTuples)
			{
				var row = new ContentRow(_spreadsheet);
				row.SetCell(InternalSpreadsheet.MetadataKeyColumnLabel, InternalSpreadsheet.PageContentRowLabel);
				row.SetCell(InternalSpreadsheet.PageNumberColumnLabel, pageNumber);

				if (pageContent.imageContainer != null)
				{
					var image = (XmlElement)pageContent.imageContainer.SafeSelectNodes(".//img").Item(0);
					var imagePath = ImagePath(imagesFolderPath, image.GetAttribute("src"));
					row.SetCell(InternalSpreadsheet.ImageSourceColumnLabel, imagePath);
				}

				if (pageContent.group != null)
				{
					foreach (var editable in pageContent.group.SafeSelectNodes("./*[contains(@class, 'bloom-editable')]").Cast<XmlElement>())
					{
						var lang = editable.Attributes["lang"]?.Value ?? "";
						if (lang == "z" || lang == "")
							continue;
						var index = _spreadsheet.ColumnForLang(lang);
						var content = editable.InnerXml;
						row.SetCell(index, content);
					}
				}

				row.BackgroundColor = colorForPage;
			}
		}

		private XmlElement GetDataDiv(HtmlDom elementOrDom)
		{
			return elementOrDom.SafeSelectNodes(".//div[@id='bloomDataDiv']").Cast<XmlElement>().First();
		}

		private XmlElement[] GetImageContainers(XmlElement elementOrDom)
		{
			return elementOrDom.SafeSelectNodes(".//*[contains(@class,'bloom-imageContainer')]").Cast<XmlElement>().ToArray();
		}

		//private SpreadsheetRow CreateImageRows(XmlElement imageContainer, string pageNumber, string imagesFolderPath)
		//{
		//	var row = new ContentRow(_spreadsheet);
		//	row.SetCell(InternalSpreadsheet.MetadataKeyColumnLabel, InternalSpreadsheet.PageContentRowLabel);
		//	row.SetCell(InternalSpreadsheet.PageNumberColumnLabel, pageNumber);
		//	var image = (XmlElement)imageContainer.SafeSelectNodes(".//img").Item(0);
		//	var imagePath = ImagePath(imagesFolderPath, image.GetAttribute("src"));
		//	row.SetCell(InternalSpreadsheet.ImageSourceColumnLabel, imagePath);
		//	return row;
		//}

		private string ImagePath(string imagesFolderPath, string imageSrc)
		{
			return Path.Combine(imagesFolderPath, UrlPathString.CreateFromUrlEncodedString(imageSrc).NotEncoded);
		}

		//private SpreadsheetRow CreateTranslationGroupRow(XmlNode group, string pageNumber)
		//{
		//	var row = new ContentRow(_spreadsheet);
		//	row.SetCell(InternalSpreadsheet.MetadataKeyColumnLabel, InternalSpreadsheet.PageContentRowLabel);
		//	row.SetCell(InternalSpreadsheet.PageNumberColumnLabel, pageNumber);
		//	foreach (var editable in group.SafeSelectNodes("./*[contains(@class, 'bloom-editable')]").Cast<XmlElement>())
		//	{
		//		var lang = editable.Attributes["lang"]?.Value ?? "";
		//		if (lang == "z" || lang == "")
		//			continue;
		//		var index = _spreadsheet.ColumnForLang(lang);
		//		var content = editable.InnerXml;
		//		row.SetCell(index,content);
		//	}
		//	return row;
		//}

		private void AddDataDivData(XmlNode node, string imagesFolderPath)
		{
			var dataBookNodeList = node.SafeSelectNodes("./div[@data-book]").Cast<XmlElement>().ToList();
			//Bring the ones with the same data-book value together so we can easily make a single row for each data-book value
			dataBookNodeList.Sort((a, b) => a.GetAttribute("data-book").CompareTo(b.GetAttribute("data-book")));
			string prevDataBookLabel = null;
			SpreadsheetRow row = null;
			foreach (XmlElement dataBookElement in dataBookNodeList)
			{
				var lang = dataBookElement.GetAttribute("lang");
				if (lang == "z")
				{
					continue;
				}
				var dataBookLabel = dataBookElement.GetAttribute("data-book");

				//The first time we see this tag:
				if (!dataBookLabel.Equals(prevDataBookLabel))
				{
					row = new ContentRow(_spreadsheet);
					var label = "[" + dataBookLabel.Trim() + "]";
					if (label != InternalSpreadsheet.BookTitleRowLabel)
						row.Hidden = true;
					row.SetCell(InternalSpreadsheet.MetadataKeyColumnLabel, label);

					var imageSrcAttribute = dataBookElement.GetAttribute("src").Trim();

					if (IsDataDivImageElement(dataBookElement, dataBookLabel))
					{
						if (imageSrcAttribute.Length > 0
							&& dataBookElement.InnerText.Trim().Length > 0
							&& !imageSrcAttribute.Equals(dataBookElement.InnerText.Trim()))
						{
							//Some data-book items redundantly store the src of the image which they capture in both their content and
							//src attribute. We haven't yet found any case in which they are different, so are only storing one in the
							//spreadsheet. This test is to make sure that we notice if we come across a case where it might be necessary
							//to save both.
							var msg = LocalizationManager.GetString("Spreadsheet:DataDivConflictWarning",
								"Export warning: Found differing 'src' attribute and element text for xmatter element " + dataBookLabel
								+ ". The 'src' attribute will be ignored.");
							NonFatalProblem.Report(ModalIf.All, PassiveIf.None, msg, showSendReport: true);
						}

						string imageSource;
						string childSrc = ChildImgElementSrc(dataBookElement);
						if (childSrc.Length > 0)
						{
							if (! dataBookElement.GetAttribute("data-book").Contains("branding"))
							{
								var msg = LocalizationManager.GetString("Spreadsheet:DataDivNonBrandingImageElment",
									"Export warning: Found a non-branding image in an <img> element for " + dataBookLabel
									+ ". This is not fully handled yet.");
								NonFatalProblem.Report(ModalIf.All, PassiveIf.None, msg, showSendReport: true);
							}
							imageSource = childSrc;
						}
						else
						{
							//We determined that whether or not a data-book div has a src attribute, it is the innerText
							//of the item that is used to set the src of the image in the actual pages of the document.
							//So that's what we want to capture in the spreadsheet.
							imageSource = dataBookElement.InnerText.Trim();
						}
						row.SetCell(InternalSpreadsheet.ImageSourceColumnLabel, ImagePath(imagesFolderPath, imageSource));
						prevDataBookLabel = dataBookLabel;
						continue; 
					}
				}

				if (IsDataDivImageElement(dataBookElement, dataBookLabel))
				{
					var msg = LocalizationManager.GetString("Spreadsheet:DataDivImageMultiple",
						"Export warning: Found multiple elements for image element " + dataBookLabel + ". Only the first will be exported.");
					NonFatalProblem.Report(ModalIf.All, PassiveIf.None, msg, showSendReport: true);
					continue;
				}
				row.SetCell(_spreadsheet.ColumnForLang(lang), dataBookElement.InnerXml.Trim());
				prevDataBookLabel = dataBookLabel;
			}
		}

		private bool IsDataDivImageElement(XmlElement dataBookElement, string dataBookLabel)
		{
			var imageSrc = dataBookElement.GetAttribute("src").Trim();
			//Unfortunately, in the current state of Bloom, we have at least three ways of representing in the bloomDataDiv things that are
			//images in the main document.Some can be identified by having a src attribute on the data-book element itself. Some actually contain
			//an img element. And some don't have any identifying mark at all, so to recognize them we just have to hard-code a list.
			return imageSrc.Length > 0
					|| ChildImgElementSrc(dataBookElement).Length > 0
					|| DataDivImagesWithNoSrcAttributes.Contains(dataBookLabel);
		}

		private string ChildImgElementSrc(XmlElement node)
		{
			foreach (XmlNode childNode in node.ChildNodes)
			{
				if (childNode.Name.Equals("img") && ((XmlElement)childNode).HasAttribute("src"))
				{
					return ((XmlElement) childNode).GetAttribute("src");
				}
			}
			return "";
		}
	}
}
