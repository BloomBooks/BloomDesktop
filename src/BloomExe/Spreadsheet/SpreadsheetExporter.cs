using Bloom.Book;
using SIL.Xml;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml;

namespace Bloom.Spreadsheet
{
	public class SpreadsheetExporter
	{
		InternalSpreadsheet _spreadsheet = new InternalSpreadsheet();

		public SpreadsheetExportParams Params = new SpreadsheetExportParams();
		public InternalSpreadsheet Export(HtmlDom dom, string imagesFolderPath)
		{
			_spreadsheet.Params = Params;
			var pages = dom.GetPageElements();

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
				var imageSrc = UrlPathString.CreateFromUrlEncodedString(image.GetAttribute("src"));
				var imagePath = Path.Combine(imagesFolderPath, imageSrc.NotEncoded);
				row.SetCell(InternalSpreadsheet.ImageSourceLabel, imagePath);
			}
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
	}
}
