using Bloom.Book;
using SIL.IO;
using SIL.Xml;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;

namespace Bloom.Spreadsheet
{
	/// <summary>
	/// Imports data from an internal spreadsheet into a bloom book.
	/// </summary>
	public class SpreadsheetImporter
	{
		private HtmlDom _dest;
		private InternalSpreadsheet _sheet;
		private int _currentRowIndex;
		private int _currentPageIndex;
		private int _groupOnPageIndex;
		private int _imageContainerOnPageIndex;
		private XmlElement _currentPage;
		private XmlElement _currentGroup;
		private XmlElement _currentImageContainer;
		private List<XmlElement> _pages;
		private List<XmlElement> _groupsOnPage;
		private List<XmlElement> _imageContainersOnPage;
		private List<string> _warnings;
		private List<ContentRow> _inputRows;
		private XmlElement _dataDivElement;
		private string _bookFolderPath;
		private bool _completedGroupsOnPage; //true iff we have already updated all the translation groups on the current page
		private bool _completedImagesOnPage; //true iff we have already updated all the translation groups on the current page
											 //_completedGroupsOnPage and _completedImagesOnPage should only be simultaneously true
											 // after we have finished iterating through all images and all translation groups of the dom

		public SpreadsheetImporter(HtmlDom dest, InternalSpreadsheet sheet, string bookFolderPath)
		{
			_dest = dest;
			_dataDivElement = _dest.SafeSelectNodes("//div[@id='bloomDataDiv']").Cast<XmlElement>().First();
			_sheet = sheet;
			_bookFolderPath = bookFolderPath;
		}

		/// <summary>
		/// If true, bloom-editable elements in matched translation groups which do
		/// not have a corresponding column in the input will be deleted.
		/// </summary>
		public bool RemoveOtherLanguages => Params.RemoveOtherLanguages;

		public SpreadsheetImportParams Params = new SpreadsheetImportParams();

		/// <summary>
		/// Import the spreadsheet into the dom
		/// </summary>
		/// <returns>a list of warnings</returns>
		public List<string> Import()
		{
			_warnings = new List<string>();
			_inputRows = _sheet.ContentRows.ToList();
			_pages = _dest.GetPageElements().ToList();
			_currentRowIndex = 0;
			_currentPageIndex = -1;
			_groupsOnPage = new List<XmlElement>(0);
			_imageContainersOnPage = new List<XmlElement>(0);
			AdvanceToNextPage();
			while (_currentGroup != null || _currentImageContainer != null || _currentRowIndex < _inputRows.Count)
			{
				var currentRow = _inputRows[_currentRowIndex];
				string rowTypeLabel = currentRow.MetadataKey;

				bool isTextGroupRow = rowTypeLabel.Equals(InternalSpreadsheet.TextGroupLabel);
				bool isImageRow = rowTypeLabel.Equals(InternalSpreadsheet.ImageKeyLabel);

				var pageNumber = HtmlDom.NumberOfPage(_currentPage);
				if (isTextGroupRow && _completedGroupsOnPage)
				{
					//TODO IMPORTANT handle the case where there are more rows after the book has ended. Possibly on nonexsitant pagenumbers.

					//TODO improve this warning message. Do we want it to be like the
					//"Input has {rowsForPage} row(s) for page {rowPageNumberLabel}, but page {rowPageNumberLabel} has only..."
					//message, or at least say how many extra rows there are?
					_warnings.Add($"Input has more text than there is room for on page {pageNumber}");
					//skip through any other extraneous text rows for this page
					while (_inputRows[_currentRowIndex].MetadataKey.Equals(InternalSpreadsheet.TextGroupLabel)
						   && _inputRows[_currentRowIndex].PageNumber.Equals(pageNumber)) //TODO this won't work if currentRowIndex > inputRows.Cont
					{
						_currentRowIndex++;
					}
					continue;
				}
				else if (isImageRow && _completedImagesOnPage)
				{
					//TODO improve this warning message too
					_warnings.Add($"Input has more image(s) than there is room for on page {pageNumber}");
					//skip through any other extraneous image rows for this page
					while (_inputRows[_currentRowIndex].MetadataKey.Equals(InternalSpreadsheet.ImageKeyLabel)
						   && _inputRows[_currentRowIndex].PageNumber.Equals(pageNumber))
					{
						_currentRowIndex++;
					}
					continue;
				}


				if (_currentRowIndex >= _inputRows.Count)
				{
					if (_groupOnPageIndex > 0)
					{
						_warnings.Add($"No input row found for text block {_groupOnPageIndex + 1} of page {pageNumber}");
						//TODO we get warnings about the rest of the groups on this page if they also didn't have rows?
					}

					if (_imageContainerOnPageIndex > 0)
					{
						_warnings.Add($"No input row found for image block {_imageContainerOnPageIndex + 1} of page {pageNumber}");
					}

					if (_groupOnPageIndex > 0 || _imageContainerOnPageIndex > 0) //TODO is this logic correct? Shoud it be an or?
					{
						_currentPageIndex++;
					}

					// complain about any pages that have numbers and TGs and no input.
					// xmatter pages are not an issue.
					pageNumber = "";
					while (_currentPageIndex < _pages.Count && pageNumber == "")
					{
						var page = _pages[_currentPageIndex];
						_currentPageIndex++;
						if (TranslationGroupManager.SortedGroupsOnPage(page).Count == 0 && ImageContainersOnPage(page).Count == 0)
							continue;
						pageNumber = HtmlDom.NumberOfPage(page);

					}
					if (pageNumber != "")
						_warnings.Add($"No input found for pages from {pageNumber} onwards.");
					break;
				}

				if (isTextGroupRow || isImageRow)
				{
					//for use in warning messages
					string blockType = "";
					if (isTextGroupRow)
					{
						blockType = "text";
					}
					else if (isImageRow)
					{
						blockType = "image";
					}

					var rowPageNumberLabel = currentRow.PageNumber;
					if (rowPageNumberLabel != pageNumber)
					{
						// Do we have a later page that has the right number?
						var indexOfTargetPage = IndexOfNextPageWithNumber(rowPageNumberLabel);
						if (indexOfTargetPage > 0)
						{
							// We're missing input for the current group.
							int blockNum = -1;
							if (isTextGroupRow)
							{
								blockNum = _groupOnPageIndex + 1;
							}
							else if (isImageRow)
							{
								blockNum = _imageContainerOnPageIndex + 1;
							}

							_warnings.Add($"No input row found for" + blockType + "block {blockNum} of page {pageNumber}");
							// We want to continue the loop, ensuring that GetNextGroup() will return the first group
							// on the indicated page.
							// Enhance: possibly we should do another warning if there are also whole pages with no input?
							// but not if they have no groups.

							_currentPageIndex = indexOfTargetPage - 1;
							_groupsOnPage = new List<XmlElement>(0); // so we will at once move to next page
							_imageContainersOnPage = new List<XmlElement>(0);

							if (isTextGroupRow)
							{
								AdvanceToNextGroupOnPage();
							}
							else if (isImageRow)
							{
								AdvanceToNextImageContainerOnPage();
							}
							if (_completedGroupsOnPage && _completedImagesOnPage)
							{
								AdvanceToNextPage();
							}
							continue; // same row, put it on that page.
						}
						// No later page matches this row. So we have nowhere to put it (until we implement
						// adding pages). Warn the user.
						var previousRowsOnSamePage = PreviousRowsOnSamePage(rowPageNumberLabel);
						if (previousRowsOnSamePage == 0)
						{
							// entire page is missing.
							// Or possibly, there IS such a page, but we couldn't put
							// even one row on it because it has no TGs at all.
							// Enhance: possibly better to give different messages for these two cases?
							_warnings.Add($"Input has rows for page {rowPageNumberLabel}, but document has no page {rowPageNumberLabel} that can hold this content");
							// advance to input row on another page
							_currentRowIndex++;
							while (_currentRowIndex < _inputRows.Count &&
								   _inputRows[_currentRowIndex].PageNumber == rowPageNumberLabel)
								_currentRowIndex++;
							continue; // keep same group
						}
						else
						{
							// We've put some rows on this page, but it doesn't have room for enough.
							var rowsForPage = previousRowsOnSamePage;
							while (_currentRowIndex < _inputRows.Count &&
								   _inputRows[_currentRowIndex].PageNumber == rowPageNumberLabel)
							{
								_currentRowIndex++;
								rowsForPage++;
							}

							_warnings.Add($"Input has {rowsForPage} row(s) for page {rowPageNumberLabel}, but page {rowPageNumberLabel} has only {previousRowsOnSamePage} place(s) for text/images");
							continue; // keep same group
						}
					}

					// This is actually the normal case. The next group matches the current row.
					// Fill it in and advance to the next row and group.
					if (isTextGroupRow)
					{
						PutRowInGroup(currentRow, _currentGroup);
						AdvanceToNextGroupOnPage();
					}
					else if (isImageRow)
					{
						PutImageInContainer(currentRow, _currentImageContainer);
						AdvanceToNextImageContainerOnPage();
					}

					if (_completedGroupsOnPage && _completedImagesOnPage)
					{
						AdvanceToNextPage();
					}
				}
				else if (rowTypeLabel[0] == '[' && rowTypeLabel[rowTypeLabel.Length - 1] == ']') //This row is xmatter
				{
					string dataBookLabel = rowTypeLabel.Substring(1, rowTypeLabel.Length - 2); //remove brackets
					UpdateDataDivFromRow(currentRow, dataBookLabel);
				}
				_currentRowIndex++;
			}

			return _warnings;
		}

		private void UpdateDataDivFromRow(ContentRow currentRow, string dataBookLabel)
		{
			var xPath = "div[@data-book=\"" + dataBookLabel + "\"]";
			var matchingNodes = _dataDivElement.SelectNodes(xPath);
			XmlElement templateNode;
			if (matchingNodes.Count > 0)
			{
				templateNode = (XmlElement)matchingNodes[0];
			}
			else
			{
				templateNode = _dest.RawDom.CreateElement("div");
				templateNode.SetAttribute("data-book", dataBookLabel);
			}

			var imageSrcCol = _sheet.ColumnForTag(InternalSpreadsheet.ImageSourceLabel);
			var imageSrcPath = currentRow.GetCell(imageSrcCol).Content;
			if (imageSrcPath.Length > 0)
			{
				//TODO copy in only if not already there...
				CopyImageIn(imageSrcPath);
			}

			bool specificLanguageContentFound = false;
			bool asteriskContentFound = false;

			//Whether or not a data-book div has a src attribute, we found that the innerText is used to set the
			//src of the image in the actual pages of the document, though we haven't found a case where they differ.
			//So during export we put the innerText into the image source column, and want to put it into
			//both src and innertext on import, unless the element is in the noSrcAttribute list
			var imageSrcFileName = Path.GetFileName(imageSrcPath);
			if (imageSrcFileName.Length > 0)
			{
				XmlElement newNode = (XmlElement)templateNode.CloneNode(deep: true);
				newNode.SetAttribute("lang", "*");
				newNode.InnerText = imageSrcFileName;

				if (!SpreadsheetExporter.DataDivImagesWithNoSrcAttributes.Contains(dataBookLabel))
				{
					newNode.SetAttribute("src", imageSrcFileName);
				}
				AddDataBookNode(newNode);
			}
			else //This is not an image node
			{
				if (dataBookLabel.Equals("coverImage"))
				{
					_warnings.Add("No cover image found");
				}

				foreach (string lang in _sheet.Languages)
				{
					var langVal = currentRow.GetCell(_sheet.ColumnForLang(lang)).Content;
					var langXPath = "div[@data-book=\"" + dataBookLabel + "\" and @lang=\"" + lang + "\"]";
					var langMatchingNodes = _dataDivElement.SelectNodes(langXPath).Cast<XmlElement>();

					if (langVal.Length >= 1) //Found content in spreadsheet for this language and row
					{
						if (lang.Equals("*"))
						{
							asteriskContentFound = true;
						}
						else
						{
							specificLanguageContentFound = true;
						}

						if (langMatchingNodes.Count() > 0) //Found matching node in dom. Update node.
						{
							XmlElement matchingNode = langMatchingNodes.First();
							matchingNode.InnerXml = langVal;
							if (langMatchingNodes.Count() > 1)
							{
								_warnings.Add("Found more than one " + dataBookLabel + " element for language "
												+ lang + " in the book dom. Only the first will be updated.");
							}
						}
						else //No node for this language and data-book. Create one from template and add.
						{
							XmlElement newNode = (XmlElement)templateNode.CloneNode(deep: true);
							newNode.SetAttribute("lang", lang);
							newNode.InnerXml = langVal;
							AddDataBookNode(newNode);
						}
					}
					else  //Spreadsheet cell for this row and language is empty. Remove the corresponding node if present.
					{
						foreach (XmlNode n in langMatchingNodes.ToArray())
						{
							_dataDivElement.RemoveChild(n);
						}
					}
				}

				if (RemoveOtherLanguages)
				{
					HtmlDom.RemoveOtherLanguages(matchingNodes.Cast<XmlElement>().ToList(), _dataDivElement, _sheet.Languages);
				}

				if (asteriskContentFound && specificLanguageContentFound)
				{
					_warnings.Add(dataBookLabel + " information found in both * language column and other language column(s)");
				}
			}
		}

		private void AddDataBookNode(XmlNode node)
		{
			_dataDivElement.AppendChild(node);
		}

		private int PreviousRowsOnSamePage(string label)
		{
			int lastRowOnDifferentPage = _currentRowIndex - 1;
			while (lastRowOnDifferentPage >= 0 && _inputRows[lastRowOnDifferentPage].PageNumber == label)
				lastRowOnDifferentPage--;
			return _currentRowIndex - lastRowOnDifferentPage - 1;
		}

		private int IndexOfNextPageWithNumber(string number)
		{
			for (int i = _currentPageIndex; i < _pages.Count; i++)
			{
				if (HtmlDom.NumberOfPage(_pages[i]) == number)
					return i;
			}

			return -1;
		}

		private void AdvanceToNextGroupOnPage()
		{
			_groupOnPageIndex++;
			// We arrange for this to be always true initially
			if (_groupOnPageIndex >= _groupsOnPage.Count)
			{
				_completedGroupsOnPage = true;
				_currentGroup = null;
			}
			else
			{
				_currentGroup = _groupsOnPage[_groupOnPageIndex];
			}
		}

		private void AdvanceToNextImageContainerOnPage()
		{
			_imageContainerOnPageIndex++;
			// We arrange for this to be always true initially

			if (_imageContainerOnPageIndex >= _imageContainersOnPage.Count)
			{
				_completedImagesOnPage = true;
				_currentImageContainer = null;
			}
			else
			{
				_currentImageContainer = _imageContainersOnPage[_imageContainerOnPageIndex];
			}
		}

		//Called whenever we finish both all the translation groups and all the image containers on the page
		private void AdvanceToNextPage()
		{
			while (_groupOnPageIndex >= _groupsOnPage.Count
				&& _imageContainerOnPageIndex >= _imageContainersOnPage.Count)
			{
				_currentPageIndex++;
				if (_currentPageIndex >= _pages.Count)
				{
					_currentGroup = null;
					_currentImageContainer = null;
					_currentPage = null;
					return;
				}

				_currentPage = _pages[_currentPageIndex];
				if (HtmlDom.NumberOfPage(_currentPage) == "")
				{
					_groupsOnPage = new List<XmlElement>(0); // skip xmatter or similar page
					_imageContainersOnPage = new List<XmlElement>(0);
				}
				else
				{
					_groupsOnPage = TranslationGroupManager.SortedGroupsOnPage(_currentPage);
					_imageContainersOnPage = ImageContainersOnPage(_currentPage);
				}
				_groupOnPageIndex = 0;
				_imageContainerOnPageIndex = 0;
			}

			//check whether there are translation groups or image containers on page (there will be at least one)
			//and set variables accordingly
			_completedGroupsOnPage = false;
			_completedImagesOnPage = false;
			if (_groupsOnPage.Count == 0)
			{
				_completedGroupsOnPage = true;
				_currentGroup = null;
			}
			else
			{
				_currentGroup = _groupsOnPage[_groupOnPageIndex];
			}

			if (_imageContainersOnPage.Count == 0)
			{
				_completedImagesOnPage = true;
				_currentImageContainer = null;
			}
			else
			{
				_currentImageContainer = _imageContainersOnPage[_imageContainerOnPageIndex];
			}
		}

		private static List<XmlElement> ImageContainersOnPage(XmlElement page)
		{
			return page.SafeSelectNodes(".//div[contains(@class, 'bloom-imageContainer')]")
				.Cast<XmlElement>().ToList();

		}

		/// <summary>
		/// This is where all the excitement happens. We update the specified group
		/// with the data from the spreadsheet row.
		/// </summary>
		/// <param name="row"></param>
		/// <param name="group"></param>
		private void PutRowInGroup(ContentRow row, XmlElement group)
		{
			var sheetLanguages = _sheet.Languages;
			foreach (var lang in sheetLanguages)
			{
				var colIndex = _sheet.ColumnForLang(lang);
				var content = row.GetCell(colIndex).Content;
				var editable = HtmlDom.GetEditableChildInLang(group, lang);
				if (editable == null)
				{
					if (string.IsNullOrEmpty(content))
						continue; // Review: or make an empty one?
					var temp = HtmlDom.GetEditableChildInLang(group, "z"); // standard template element
					if (temp == null)
						temp = HtmlDom.GetEditableChildInLang(group, null); // use any available template
					if (temp == null)
					{
						// Enhance: Eventually we should be able to come up with some sort of default here.
						// Since this is a rather simple temporary expedient I haven't unit tested it.
						_warnings.Add(
							$"Could not import text group {_groupOnPageIndex} ({content}) on page {HtmlDom.NumberOfPage(_currentPage)} because it has no bloom-editable children to use as templates.");
						return;
					}

					editable = temp.Clone() as XmlElement;
					editable.SetAttribute("lang", lang);
					group.AppendChild(editable);
				}

				editable.InnerXml = content;
			}

			if (RemoveOtherLanguages)
			{
				HtmlDom.RemoveOtherLanguages(@group, sheetLanguages);
			}
		}

		private void PutImageInContainer(ContentRow row, XmlElement container)
		{
			var imageSrcCol = _sheet.ColumnForTag(InternalSpreadsheet.ImageSourceLabel);
			var imageSrcPath = row.GetCell(imageSrcCol).Content;

			XmlElement imageElement = container.SafeSelectNodes(".//img").Cast<XmlElement>().First();
			if (imageElement == null)
			{
				_warnings.Add(
					$"Could not import image {_imageContainerOnPageIndex} on page {HtmlDom.NumberOfPage(_currentPage)}, " +
					$"img element missing from book html.");
				return;
			}

			if (Path.Combine(_bookFolderPath, imageElement.GetAttribute("src")).ToLowerInvariant()
				.Equals(imageSrcPath))
			{
				//image is unchanged
				return;
			}

			CopyImageIn(imageSrcPath);

			//Remove all attributes (which may contain old image info such as copyright) except the image container class
			var classAttribute = container.GetAttribute("class");
			container.RemoveAllAttributes();
			container.SetAttribute("class", classAttribute);
			imageElement.RemoveAllAttributes();
			var imageFileName = Path.GetFileName(imageSrcPath);
			imageElement.SetAttribute("src", imageFileName);
		}

		private void CopyImageIn(string imageSrcPath)
		{
			var imageFileName = Path.GetFileName(imageSrcPath);
			//Image source in spreadsheet is not in the book folder, move it in
			if (!Path.GetDirectoryName(imageSrcPath).Equals(_bookFolderPath))
			{
				var imageDestPath = Path.Combine(_bookFolderPath, imageFileName);
				RobustFile.Copy(imageSrcPath, imageDestPath, overwrite: true); //TODO overwrite=true?
			}
		}
	}
}
