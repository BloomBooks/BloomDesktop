using System;
using Bloom.Book;
using L10NSharp;
using SIL.Xml;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml;
using Bloom.Api;
using Bloom.MiscUI;
using Bloom.web;

namespace Bloom.Spreadsheet
{
	public class SpreadsheetExporter
	{
		InternalSpreadsheet _spreadsheet = new InternalSpreadsheet();
		private IWebSocketProgress _progress;
		private bool _shouldKeepDialogOpen = false;

		//a list of values which, if they occur in the data-book attribute of an element in the bloomDataDiv,
		//indicate that the element content should be treated as an image, even though the element doesn't
		//have a src attribute nor actually contain an img element
		public static List<string> DataDivImagesWithNoSrcAttributes = new List<string>() { "licenseImage" };

		public void ExportWithProgress(HtmlDom dom, string imagesFolderPath, BloomWebSocketServer socketServer, Action<InternalSpreadsheet> resultCallback)
		{
			BrowserProgressDialog.DoWorkWithProgressDialog(socketServer, "spreadsheet-export", () =>
				new ReactDialog("progressDialogBundle",
						// props to send to the react component
						new
						{
							title = "Exporting Spreadsheet",
							titleIcon = "", // todo:
							titleColor = "white",
							titleBackgroundColor = Palette.kBloomBlueHex,
							webSocketContext = "spreadsheet-export",
							showReportButton = "if-error"
						}, "Sync Team Collection")
					// winforms dialog properties
					{ Width = 620, Height = 550 }, (progress, worker) =>
			{
				var spreadsheet = Export(dom, imagesFolderPath, progress);
				resultCallback(spreadsheet);
				return _shouldKeepDialogOpen;
			});
		}

		public SpreadsheetExportParams Params = new SpreadsheetExportParams();
		public InternalSpreadsheet Export(HtmlDom dom, string imagesFolderPath, IWebSocketProgress progress = null)
		{
			_progress = progress ?? new NullWebSocketProgress();
			_spreadsheet.Params = Params;
			var pages = dom.GetPageElements();

			//Get xmatter
			var dataDiv = GetDataDiv(dom);
			AddDataDivData(dataDiv, imagesFolderPath);

			var iContentPage = 0;
			foreach (var page in pages)
			{
				var rowsForPage = new List<SpreadsheetRow>();

				var pageNumber = page.Attributes["data-page-number"]?.Value ?? "";
				// For now we will ignore all un-numbered pages, particularly xmatter,
				// which was handled above by exporting data div data.
				if (pageNumber == "")
					continue;

				//Get images
				var imageContainers = GetImageContainers(page);
				foreach (var imageContainer in imageContainers)
				{
					rowsForPage.AddRange(CreateImageRows(imageContainer, pageNumber, imagesFolderPath));
				}

				//Get translation groups
				var groups = TranslationGroupManager.SortTranslationGroups(TranslationGroupManager.GetTranslationGroups(page));
				foreach (var group in groups)
				{
					rowsForPage.Add(CreateTranslationGroupRow(group, pageNumber));
				}

				//Each page alternates colors
				var colorForPage = iContentPage++ % 2 == 0 ? InternalSpreadsheet.AlternatingRowsColor1 : InternalSpreadsheet.AlternatingRowsColor2;
				foreach (var row in rowsForPage)
					row.BackgroundColor = colorForPage;
			}
			_spreadsheet.SortHiddenRowsToTheBottom();
			return _spreadsheet;
		}

		private XmlElement GetDataDiv(HtmlDom elementOrDom)
		{
			return elementOrDom.SafeSelectNodes(".//div[@id='bloomDataDiv']").Cast<XmlElement>().First();
		}

		private XmlElement[] GetImageContainers(XmlElement elementOrDom)
		{
			return elementOrDom.SafeSelectNodes(".//*[contains(@class,'bloom-imageContainer')]").Cast<XmlElement>().ToArray();
		}

		private List<SpreadsheetRow> CreateImageRows(XmlElement imageContainer, string pageNumber, string imagesFolderPath)
		{
			var rowsCreated = new List<SpreadsheetRow>();
			foreach (XmlElement image in imageContainer.SafeSelectNodes(".//img"))
			{
				var row = new ContentRow(_spreadsheet);
				row.SetCell(InternalSpreadsheet.MetadataKeyColumnLabel, InternalSpreadsheet.ImageRowLabel);
				row.SetCell(InternalSpreadsheet.PageNumberColumnLabel, pageNumber);
				var imagePath = ImagePath(imagesFolderPath, image.GetAttribute("src"));
				row.SetCell(InternalSpreadsheet.ImageSourceColumnLabel, imagePath);
				rowsCreated.Add(row);
			}
			return rowsCreated;
		}

		private string ImagePath(string imagesFolderPath, string imageSrc)
		{
			return Path.Combine(imagesFolderPath, UrlPathString.CreateFromUrlEncodedString(imageSrc).NotEncoded);
		}

		private SpreadsheetRow CreateTranslationGroupRow(XmlNode group, string pageNumber)
		{
			var row = new ContentRow(_spreadsheet);
			row.SetCell(InternalSpreadsheet.MetadataKeyColumnLabel, InternalSpreadsheet.TextGroupRowLabel);
			row.SetCell(InternalSpreadsheet.PageNumberColumnLabel, pageNumber);
			foreach (var editable in group.SafeSelectNodes("./*[contains(@class, 'bloom-editable')]").Cast<XmlElement>())
			{
				var lang = editable.Attributes["lang"]?.Value ?? "";
				if (lang == "z" || lang == "")
					continue;
				var index = _spreadsheet.ColumnForLang(lang);
				var content = editable.InnerXml;
				row.SetCell(index,content);
			}
			return row;
		}

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
							_shouldKeepDialogOpen = true;
							_progress.MessageWithParams("Spreadsheet.DataDivConflictWarning","",
								"Export warning: Found differing 'src' attribute and element text for data-div element {0}. The 'src' attribute will be ignored.",
								ProgressKind.Warning, dataBookLabel) ;
						}

						string imageSource;
						string childSrc = ChildImgElementSrc(dataBookElement);
						if (childSrc.Length > 0)
						{
							// We've lost track of what was 'incomplete' about our handling of data-book elements
							// that have an image child and don't have branding in their key. But the message
							// was a nuisance. Keeping the code in case it reminds us of a problem at some point.
							//if (! dataBookElement.GetAttribute("data-book").Contains("branding"))
							//{
							//	var msg = LocalizationManager.GetString("Spreadsheet:DataDivNonBrandingImageElment",
							//		"Export warning: Found a non-branding image in an <img> element for " + dataBookLabel
							//		+ ". This is not fully handled yet.");
							//	NonFatalProblem.Report(ModalIf.All, PassiveIf.None, msg, showSendReport: true);
							//}
							// Don't think we ever have data-book elements with more than one image. But if we encounter one,
							// I think it's worth warning the user that we don't handle it.
							if (dataBookElement.ChildNodes
								    .Cast<XmlNode>().Count(n => n.Name == "img" && string.IsNullOrEmpty(((XmlElement)n).GetAttribute("src"))) > 1)
							{
								_shouldKeepDialogOpen = true;
								_progress.MessageWithParams("Spreadsheet.MultipleImageChildren", "",
									"Export warning: Found multiple images in data-book element {0}. Only the first will be exported.", ProgressKind.Warning, dataBookLabel);
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
					_shouldKeepDialogOpen = true;
					_progress.MessageWithParams("Spreadsheet.DataDivImageMultiple", "",
						"Export warning: Found multiple elements for image element {0}. Only the first will be exported.", ProgressKind.Warning, dataBookLabel);
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
