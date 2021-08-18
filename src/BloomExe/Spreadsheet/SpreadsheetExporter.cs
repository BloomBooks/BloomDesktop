using Bloom.Book;
using L10NSharp;
using SIL.Xml;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml;

namespace Bloom.Spreadsheet
{
	public class SpreadsheetExporter
	{
		InternalSpreadsheet _spreadsheet = new InternalSpreadsheet();
		public static List<string> _xmatterImagesWithNoSrcAttributes = new List<string>() { "licenseImage" };

		public SpreadsheetExportParams Params = new SpreadsheetExportParams();
		public InternalSpreadsheet Export(HtmlDom dom, string imagesFolderPath)
		{
			_spreadsheet.Params = Params;
			var pages = dom.GetPageElements();

			//Get xmatter
			var xmatterDataDiv = GetXMatterDataDiv(dom);
			foreach (var xmatterData in xmatterDataDiv)
			{
				AddXmatterDataRow(xmatterData, imagesFolderPath);
			}

			foreach (var page in pages)
			{
				var pageNumber = page.Attributes["data-page-number"]?.Value ?? "";
				// For now we will ignore all un-numbered pages, particularly xmatter,
				// which eventually needs to be handled by exporting data-book items.
				if (pageNumber == "")
					continue;

				//Get images
				var imageContainers = GetImageContainers(page);
				int imageIndex = 1;
				foreach (var imageContainer in imageContainers)
				{
					AddImageRow(imageContainer, pageNumber, imageIndex, imagesFolderPath);
					imageIndex++;
				}

				//Get translation groups
				var groups = TranslationGroupManager.SortTranslationGroups(TranslationGroupManager.GetTranslationGroups(page));
				int groupIndex = 1;
				foreach (var group in groups)
				{
					AddTranslationGroupRow(group, pageNumber, groupIndex);
					groupIndex++;
				}
			}
			return _spreadsheet;
		}

		private XmlElement[] GetXMatterDataDiv(HtmlDom elementOrDom)
		{
			return elementOrDom.SafeSelectNodes(".//div[@id='bloomDataDiv']").Cast<XmlElement>().ToArray();
		}

		private XmlElement[] GetImageContainers(XmlElement elementOrDom)
		{
			return elementOrDom.SafeSelectNodes(".//*[contains(@class,'bloom-imageContainer')]").Cast<XmlElement>().ToArray();
		}

		private void AddImageRow(XmlElement imageContainer, string pageNumber, int imageIndex, string imagesFolderPath)
		{
			foreach (XmlElement image in imageContainer.SafeSelectNodes(".//img"))
			{
				var row = new ContentRow(_spreadsheet);
				row.SetCell(InternalSpreadsheet.MetadataKeyLabel, InternalSpreadsheet.ImageKeyLabel);
				row.SetCell(InternalSpreadsheet.PageNumberLabel, pageNumber);
				row.SetCell(InternalSpreadsheet.ImageIndexOnPageLabel, imageIndex.ToString(CultureInfo.InvariantCulture));
				var imagePath = ImagePath(imagesFolderPath, image.GetAttribute("src"));
				row.SetCell(InternalSpreadsheet.ImageSourceLabel, imagePath);
			}
		}

		private string ImagePath(string imagesFolderPath, string imageSrc)
		{
			return Path.Combine(imagesFolderPath, UrlPathString.CreateFromUrlEncodedString(imageSrc).NotEncoded);
		}

		private void AddTranslationGroupRow(XmlNode group, string pageNumber, int groupIndex)
		{
			var row = new ContentRow(_spreadsheet);
			row.SetCell(InternalSpreadsheet.MetadataKeyLabel, InternalSpreadsheet.TextGroupLabel);
			row.SetCell(InternalSpreadsheet.PageNumberLabel, pageNumber);
			row.SetCell(InternalSpreadsheet.TextIndexOnPageLabel, groupIndex.ToString(CultureInfo.InvariantCulture));
			foreach (var editable in group.SafeSelectNodes("./*[contains(@class, 'bloom-editable')]").Cast<XmlElement>())
			{
				var lang = editable.Attributes["lang"]?.Value ?? "";
				if (lang == "z" || lang == "")
					continue;
				var index = _spreadsheet.ColumnForLang(lang);
				var content = editable.InnerXml;
				row.SetCell(index,content);
			}
		}

		private void AddXmatterDataRow(XmlNode node, string imagesFolderPath)
		{
			var dataBookNodeList = node.SafeSelectNodes("./div[@data-book]").Cast<XmlElement>().ToList();
			dataBookNodeList.Sort((a, b) => a.GetAttribute("data-book").CompareTo(b.GetAttribute("data-book")));
			string prevDataBookLabel = null;
			SpreadsheetRow row = _spreadsheet.Header;
			foreach (XmlElement dataBookElement in dataBookNodeList)
			{
				var dataBookLabel = dataBookElement.GetAttribute("data-book");

				//The first time we see this tag:
				if (!dataBookLabel.Equals(prevDataBookLabel))
				{
					row = new ContentRow(_spreadsheet);
					row.SetCell(InternalSpreadsheet.MetadataKeyLabel, dataBookLabel.Trim());

					var imageSrcAttribute = dataBookElement.GetAttribute("src").Trim();

					if (IsImageXmatterElement(dataBookElement, dataBookLabel))
					{
						if (imageSrcAttribute.Length > 0
							&& dataBookElement.InnerText.Trim().Length > 0
							&& !imageSrcAttribute.Equals(dataBookElement.InnerText.Trim()))
						{
							var msg = LocalizationManager.GetString("Spreadsheet:XmatterConflictWarning",
								"Export warning: Found differing 'src' attribute and element text for xmatter element " + dataBookLabel
								+ ". The 'src' attribute will be ignored.");
							NonFatalProblem.Report(ModalIf.All, PassiveIf.None, msg, showSendReport: true);
						}

						string imageSource;
						if (FirstChildIsImageWithSource(dataBookElement))
						{
							if (! dataBookElement.Name.Contains("branding"))
							{
								var msg = LocalizationManager.GetString("Spreadsheet:XmatterNonBrandingImageElment",
									"Export warning: Found a non-branding image in an <img> element for " + dataBookLabel
									+ ". This is not fully handled yet.");
								NonFatalProblem.Report(ModalIf.All, PassiveIf.None, msg, showSendReport: true);
							}
							imageSource = dataBookElement.ChildNodes[0].GetStringAttribute("src");
						}
						else
						{
							imageSource = dataBookElement.InnerText.Trim();
						}
						row.SetCell(InternalSpreadsheet.ImageSourceLabel, ImagePath(imagesFolderPath, imageSource));

						continue; 
					}
				}

				if (IsImageXmatterElement(dataBookElement, dataBookLabel))
				{
					var msg = LocalizationManager.GetString("Spreadsheet:XmatterImageMultiple",
						"Export warning: Found multiple elements for image element " + dataBookLabel + ". Only the first will be exported.");
					NonFatalProblem.Report(ModalIf.All, PassiveIf.None, msg, showSendReport: true);
					continue;
				}
				var lang = dataBookElement.GetAttribute("lang");
				row.SetCell(_spreadsheet.ColumnForLang(lang), dataBookElement.InnerXml.Trim());
				prevDataBookLabel = dataBookLabel;
			}
		}

		private bool IsImageXmatterElement(XmlElement dataBookElement, string dataBookLabel)
		{
			var imageSrc = dataBookElement.GetAttribute("src").Trim();
			return imageSrc.Length > 0
					|| FirstChildIsImageWithSource(dataBookElement)
					|| _xmatterImagesWithNoSrcAttributes.Contains(dataBookLabel);
		}

		private bool FirstChildIsImageWithSource(XmlElement node)
		{
			if (!node.HasChildNodes)
			{
				return false;
			}

			var firstChild = node.ChildNodes[0];
			return firstChild.Name.Equals("img") && ((XmlElement) firstChild).HasAttribute("src");
		}
	}
}
