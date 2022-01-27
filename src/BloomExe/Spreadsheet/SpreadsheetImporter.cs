using System;
using Bloom.Book;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security;
using System.Windows.Forms;
using System.Xml;
using Bloom.Api;
using Bloom.MiscUI;
using Bloom.web;
using SIL.IO;
using SIL.Progress;
using SIL.Xml;
using TagLib;

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
		private XmlElement _currentPage;
		private XmlElement _currentGroup;
		private List<XmlElement> _pages;
		private List<XmlElement> _groupsOnPage;
		private int _imageContainerOnPageIndex;
		private List<XmlElement> _imageContainersOnPage;
		private XmlElement _currentImageContainer;
		private List<string> _warnings;
		private List<ContentRow> _inputRows;
		private XmlElement _dataDivElement;
		private string _pathToSpreadsheetFolder;
		private string _pathToBookFolder;
		private IWebSocketProgress _progress;
		private int _unNumberedPagesSeen;
		private bool _bookIsLandscape;
		private Layout _destLayout;

		public delegate SpreadsheetImporter Factory();

		/// <summary>
		/// Create an instance. The webSocketServer may be null unless using ImportWithProgress.
		/// </summary>
		/// <remarks>The web socket server is a constructor argument as a step in the direction
		/// of allowing this class to be instantiated and supplied with the socket server by
		/// AutoFac. However, for that to work, we'd need to move the other constructor arguments,
		/// which AutoFac can't know, to the Import method. And for now, all callers which need
		/// to pass a socket server already have one.</remarks>
		public SpreadsheetImporter(HtmlDom dest, string pathToSpreadsheetFolder = null, string pathToBookFolder = null)
		{
			_dest = dest;
			_dataDivElement = _dest.SafeSelectNodes("//div[@id='bloomDataDiv']").Cast<XmlElement>().First();
			_pathToBookFolder = pathToBookFolder;
			_pathToSpreadsheetFolder = pathToSpreadsheetFolder;
		}

		/// <summary>
		/// If true, bloom-editable elements in matched translation groups which do
		/// not have a corresponding column in the input will be deleted.
		/// </summary>
		public bool RemoveOtherLanguages => Params.RemoveOtherLanguages;

		public SpreadsheetImportParams Params = new SpreadsheetImportParams();

		public void ImportWithProgress(string inputFilepath, string bookPath)
		{
			var mainShell = Application.OpenForms.Cast<Form>().FirstOrDefault(f => f is Shell);
			BrowserProgressDialog.DoWorkWithProgressDialog("spreadsheet-import", () =>
				new ReactDialog("progressDialogBundle",
						// props to send to the react component
						new
						{
							title = "Importing Spreadsheet",
							titleIcon = "", // enhance: add icon if wanted
							titleColor = "white",
							titleBackgroundColor = Palette.kBloomBlueHex,
							webSocketContext = "spreadsheet-import",
							showReportButton = "if-error"
						}, "Import Spreadsheet")
					// winforms dialog properties
					{ Width = 620, Height = 550 }, (progress, worker) =>
			{
				string folderPath = Path.GetDirectoryName(bookPath);
				var hasAudio = _dest.GetRecordedAudioSentences(folderPath).Any();
				if (hasAudio)
				{
					progress.MessageWithoutLocalizing($"Warning: Spreadsheet import cannot currently preserve Talking Book audio that is already in this book. For this reason, we need to abandon the import.", ProgressKind.Error);
					return true; // leave progress window up so user can see error.
				}
				var sheet = InternalSpreadsheet.ReadFromFile(inputFilepath, progress);
				if (sheet == null)
					return true;
				if (!Validate(sheet, progress))
					return true; // errors already reported to progress
				progress.MessageWithoutLocalizing($"Making a backup of the original book...");
				var backupPath = BookStorage.SaveCopyBeforeImportOverwrite(folderPath, bookPath);
				progress.MessageWithoutLocalizing($"Backup completed (at {backupPath})");
				Import(sheet, progress);
				return true; // always leave the dialog up until the user chooses 'close'
			}, null, mainShell);
		}

		public bool Validate(InternalSpreadsheet sheet, IWebSocketProgress progress)
		{
			// An export would have several others. But none of them is absolutely required except this one.
			// (We could do without it, too, by assuming the first column contains them. But it's helpful to be
			// able to recognize spreadsheets created without any knowledge at all of the expected content.)
			// Note: depending on row content, this problem may be detected earlier in SpreadsheetIO while
			// converting the file to an InternalSpreadsheet.
			var rowTypeColumn = sheet.GetColumnForTag(InternalSpreadsheet.RowTypeColumnLabel);
			if (rowTypeColumn < 0)
			{
				progress.MessageWithoutLocalizing(MissingHeaderMessage, ProgressKind.Error);
				return false;
			}
			var inputRows = sheet.ContentRows.ToList();
			if (!inputRows.Any(r => r.GetCell(rowTypeColumn).Content.StartsWith("[")))
			{
				progress.MessageWithoutLocalizing("This spreadsheet has no data that Bloom knows how to import. Did you follow the standard format for Bloom spreadsheets?", ProgressKind.Warning);
				// Technically this isn't a fatal error. We could just let the main loop do nothing. But reporting it as one avoids creating a spurious backup.
				return false;
			}
			return true;
		}

		/// <summary>
		/// Import the spreadsheet into the dom
		/// </summary>
		/// <returns>a list of warnings</returns>
		public List<string> Import(InternalSpreadsheet sheet, IWebSocketProgress progress = null)
		{
			_sheet = sheet;
			_progress = progress ?? new NullWebSocketProgress();
			Progress("Importing spreadsheet...");
			_warnings = new List<string>();
			_inputRows = _sheet.ContentRows.ToList();
			_pages = _dest.GetPageElements().ToList();
			_bookIsLandscape = _pages[0]?.Attributes["class"]?.Value?.Contains("Landscape") ?? false;
			_currentRowIndex = 0;
			_currentPageIndex = -1;
			_groupsOnPage = new List<XmlElement>();
			_imageContainersOnPage = new List<XmlElement>();
			_destLayout = Layout.FromDom(_dest, Layout.A5Portrait);
			while (_currentRowIndex < _inputRows.Count)
			{

				var currentRow = _inputRows[_currentRowIndex];
				string rowTypeLabel = currentRow.MetadataKey;

				if (rowTypeLabel == InternalSpreadsheet.PageContentRowLabel)
				{
					bool rowHasImage = !string.IsNullOrWhiteSpace(currentRow.GetCell(InternalSpreadsheet.ImageSourceColumnLabel).Text);
					bool rowHasText = RowHasText(currentRow);
					if (rowHasImage && rowHasText)
					{
						AdvanceToNextGroupAndImageContainer();
					} else if (rowHasImage)
					{
						AdvanceToNextImageContainer();
					} else if (rowHasText)
					{
						AdvanceToNextGroup();
					}
					if (rowHasImage)
					{
						PutRowInImage(currentRow);
					}
					if (rowHasText) {
						PutRowInGroup(currentRow, _currentGroup);
					}
				}
				else if (rowTypeLabel.StartsWith("[") && rowTypeLabel.EndsWith("]")) //This row is xmatter
				{
					string dataBookLabel = rowTypeLabel.Substring(1, rowTypeLabel.Length - 2); //remove brackets
					UpdateDataDivFromRow(currentRow, dataBookLabel);
				}
				_currentRowIndex++;
			}

			Progress("Done");
			return _warnings;
		}

		private void PutRowInImage(ContentRow currentRow)
		{
			var imgSrc = currentRow.GetCell(InternalSpreadsheet.ImageSourceColumnLabel).Content;
			if (imgSrc == InternalSpreadsheet.BlankContentIndicator)
			{
				imgSrc = "placeHolder.png";
			}

			var imgFileName = Path.GetFileName(imgSrc);
			var bloomSrc = Path.GetFileName(imgFileName);
			var img = GetImgFromContainer(_currentImageContainer);
			// Enhance: warn if null?
			img?.SetAttribute("src", UrlPathString.CreateFromUnencodedString(bloomSrc).UrlEncoded);
			// Earlier versions of Bloom often had explicit height and width settings on images.
			// In case anything of the sort remains, it probably won't be correct for the new image,
			// so best to get rid of it.
			img?.RemoveAttribute("height");
			img?.RemoveAttribute("width");
			// image containers often have a generated title attribute that gives the file name and
			// notes about its resolution, etc. We think it will be regenerated as needed, but certainly
			// the one from a previous image is no use.
			_currentImageContainer.RemoveAttribute("title");
			if (_pathToSpreadsheetFolder != null) //currently will only be null in tests
			{
				// To my surprise, if imgSrc is rooted (a full path), this will just use it,
				// ignoring _pathToSpreadsheetFolder, which is what we want.
				var source = Path.Combine(_pathToSpreadsheetFolder, imgSrc);
				if (imgSrc == "placeHolder.png")
				{
					// Don't assume the source has it, let's get a copy from files shipped with Bloom
					source = Path.Combine(BloomFileLocator.FactoryCollectionsDirectory, "template books",
						"Basic Book", "placeHolder.png");
				}

				try
				{
					// Copy image file to destination
					if (_pathToBookFolder != null && _pathToSpreadsheetFolder != null)
					{
						var dest = Path.Combine(_pathToBookFolder, bloomSrc);
						if (RobustFile.Exists(source))
						{
							RobustFile.Copy(source, dest, true);
							ImageUpdater.UpdateImgMetadataAttributesToMatchImage(_pathToBookFolder, img,
								new NullProgress());
						}
						else
						{
							// Review: I doubt these messages are worth localizing? The sort of people who attempt
							// spreadsheet import can likely cope with some English?
							// +1 conversion from zero-based to 1-based counting, further adding header.RowCount
							// makes it match up with the actual row label in the spreadsheet.
							Warn(
								$"Image \"{source}\" on row {_currentRowIndex + 1 + _sheet.Header.RowCount} was not found.");
						}
					}
				}
				catch (Exception e) when (e is IOException || e is SecurityException ||
				                          e is UnauthorizedAccessException)
				{
					Warn(
						$"Bloom had trouble copying the file {source} to the book folder or retrieving its metadata: " +
						e.Message);
				}
			}
		}

		void Warn(string message)
		{
			_warnings.Add(message);
			_progress?.MessageWithoutLocalizing(message, ProgressKind.Warning);
		}

		void Progress(string message)
		{
			// We don't think the importer messages are worth localizing at this point.
			// Users sophisticated enough to use this feature can probably cope with some English.
			_progress?.MessageWithoutLocalizing(message, ProgressKind.Progress);
		}

		private XmlElement GetImgFromContainer(XmlElement container)
		{
			return container.ChildNodes.Cast<XmlNode>()
				.FirstOrDefault(x => x.Name == "img") as XmlElement;
		}

		private void UpdateDataDivFromRow(ContentRow currentRow, string dataBookLabel)
		{
			if (dataBookLabel.Contains("branding"))
				return; // branding data-div elements are complex and difficult and determined by current collection state
			// Only a few of these are worth reporting
			string whatsUpdated = null;
			switch (dataBookLabel)
			{
				case "coverImage":
					whatsUpdated = "the image on the cover";
					break;
				case "bookTitle":
					whatsUpdated = "the book title";
					break;
				case "copyright":
					whatsUpdated = "copyright information";
					break;
			}
			if (whatsUpdated != null)
				Progress($"Updating {whatsUpdated}.");

			var xPath = "div[@data-book=\"" + dataBookLabel + "\"]";
			var matchingNodes = _dataDivElement.SelectNodes(xPath);
			XmlElement templateNode;
			bool templateNodeIsNew = false;
			if (matchingNodes.Count > 0)
			{
				templateNode = (XmlElement) matchingNodes[0];
			}
			else
			{
				templateNodeIsNew = true;
				templateNode = _dest.RawDom.CreateElement("div");
				templateNode.SetAttribute("data-book", dataBookLabel);
			}

			var imageSrcCol = _sheet.GetColumnForTag(InternalSpreadsheet.ImageSourceColumnLabel);
			var imageSrc = imageSrcCol >= 0 ? Path.GetFileName(currentRow.GetCell(imageSrcCol).Content) : null;
			bool specificLanguageContentFound = false;
			bool asteriskContentFound = false;

			//Whether or not a data-book div has a src attribute, we found that the innerText is used to set the
			//src of the image in the actual pages of the document, though we haven't found a case where they differ.
			//So during export we put the innerText into the image source column, and want to put it into
			//both src and innertext on import, unless the element is in the noSrcAttribute list
			if (imageSrc.Length > 0)
			{
				templateNode.SetAttribute("lang", "*");
				templateNode.InnerText = imageSrc;

				if (! SpreadsheetExporter.DataDivImagesWithNoSrcAttributes.Contains(dataBookLabel))
				{
					templateNode.SetAttribute("src", imageSrc);
				}
				if (templateNodeIsNew)
					AddDataBookNode(templateNode);
			}
			else //This is not an image node
			{
				if (dataBookLabel.Equals("coverImage"))
				{
					Warn("No cover image found");
				}

				foreach (string lang in _sheet.Languages)
				{
					var langVal = currentRow.GetCell(_sheet.GetRequiredColumnForLang(lang)).Content;
					var langXPath = "div[@data-book=\"" + dataBookLabel + "\" and @lang=\"" + lang + "\"]";
					var langMatchingNodes = _dataDivElement.SelectNodes(langXPath).Cast<XmlElement>();

					if (!string.IsNullOrEmpty(langVal))
					{
						//Found content in spreadsheet for this language and row
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
								Warn("Found more than one " + dataBookLabel +" element for language "
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
					Warn(dataBookLabel + " information found in both * language column and other language column(s)");
				}
			}
		}

		private void AddDataBookNode(XmlNode node)
		{
			_dataDivElement.AppendChild(node);
		}

		private void AdvanceToNextGroup()
		{
			_groupOnPageIndex++;
			// We arrange for this to be always true initially
			if (_groupOnPageIndex >= _groupsOnPage.Count)
			{
				AdvanceToNextNumberedPage(false, true);
				_groupOnPageIndex = 0;
			}

			_currentGroup = _groupsOnPage[_groupOnPageIndex];
		}

		private int PageNumberToReport => _currentPageIndex + 1 - _unNumberedPagesSeen;

		private XmlDocument _basicBookTemplate;

		private void GeneratePage(string guid)
		{
			if (_basicBookTemplate == null)
			{
				var path = Path.Combine(BloomFileLocator.FactoryCollectionsDirectory, "template books", "Basic Book", "Basic Book.html");
				_basicBookTemplate = XmlHtmlConverter.GetXmlDomFromHtmlFile(path, false);
			}

			var templatePage = _basicBookTemplate.SelectSingleNode($"//div[@id='{guid}']") as XmlElement;
			ImportPage(templatePage);
			var pageLabel = templatePage.SafeSelectNodes("//div[@class='pageLabel']").Cast<XmlElement>().FirstOrDefault()?.InnerText ?? "";
			Progress($"Adding page {PageNumberToReport} using a {pageLabel} layout");
		}

		// Insert a clone of templatePage into the document before _currentPage (or after _lastContentPage, if _currentPage is null),
		// and make _currentPage point to the new page.
		private void ImportPage(XmlElement templatePage)
		{
			var newPage = _pages[0].OwnerDocument.ImportNode(templatePage, true) as XmlElement;
			BookStarter.SetupIdAndLineage(templatePage, newPage);
			_pages.Insert(_currentPageIndex, newPage);
			SizeAndOrientation.UpdatePageSizeAndOrientationClasses(newPage, _destLayout);
			// Correctly inserts at end if _currentPage is null, though this will hardly ever
			// be true because we normally have at least backmatter page to insert before.
			_pages[0].ParentNode.InsertBefore(newPage, _currentPage);

			// clear everything: this is useful in case it has slots we won't use.
			// They might have content either from the original last page, or from the
			// modifications we already made to it.
			var editables = newPage.SelectNodes(".//div[contains(@class, 'bloom-editable') and @lang != 'z']").Cast<XmlElement>().ToArray();
			foreach (var e in editables)
			{
				e.ParentNode.RemoveChild(e);
			}

			var imageContainers = GetImageContainers(newPage);
			foreach (var c in imageContainers)
			{
				var img = GetImgFromContainer(c);
				img?.SetAttribute("src", "placeHolder.png");
				foreach (var attr in new[] { "alt", "data-copyright", "data-creator", "data-license" })
					img?.RemoveAttribute(attr);
				c.RemoveAttribute("title");
			}

			// This is not tested yet, but we want to remove video content if any from whatever last page we're copying.
			foreach (var v in newPage.SelectNodes(".//div[contains(@class, 'bloom-videoContainer')]/video")
				         .Cast<XmlElement>().ToList())
			{
				HtmlDom.AddClass(v.ParentNode as XmlElement, "bloom-noVideoSelected");
				v.ParentNode.RemoveChild(v);
			}

			// and widgets (also not tested)
			foreach (var w in newPage.SelectNodes(".//div[contains(@class, 'bloom-widgetContainer')]/iframe")
				         .Cast<XmlElement>().ToList())
			{
				HtmlDom.AddClass(w.ParentNode as XmlElement, "bloom-noWidgetSelected");
				w.ParentNode.RemoveChild(w);
			}

			_currentPage = newPage;
		}

		private XmlElement _lastContentPage; // Actually the last one we've seen so far, but used only when it really is the last
		private bool _lastPageHasImageContainer;
		private bool _lastPageHasTextGroup;
		public const string MissingHeaderMessage = "Bloom can only import spreadsheets that match a certain layout. In this spreadsheet, Bloom was unable to find the required \"[row type]\" column in row 1";

		private void AdvanceToNextNumberedPage(bool needImageContainer, bool needTextGroup)
		{
			Debug.Assert(needTextGroup || needImageContainer,
				"Shouldn't be advancing to another page unless we have something to put on it");
			string guidOfPageToClone;
			while (true)
			{
				_currentPageIndex++;

				if (_currentPageIndex >= _pages.Count)
				{
					// We'll have to generate a new page. It will have what we need, so the loop
					// will terminate there.
					_currentPage = null; // this has an effect on where we insert the new page.
					InsertCloneOfLastPageOrDefault(needImageContainer, needTextGroup);
					return;
				}

				_currentPage = _pages[_currentPageIndex];
				// Is this where we want to stop, or should we skip this page and move on?
				// If it's a numbered page, we consider that a template content page we can
				// insert row content into...or if it doesn't hold the right sort of content,
				// we'll insert a page that does before it.
				// If it's a back matter page, we've come to the end...we'll have to insert
				// extra pages before it for whatever remaining content we have.
				if (HtmlDom.NumberOfPage(_currentPage) != "" ||
				    _currentPage.Attributes["class"].Value.Contains("bloom-backMatter"))
				{
					break;
				}

				_unNumberedPagesSeen++;
			}

			var isBackMatter = _currentPage.Attributes["class"].Value.Contains("bloom-backMatter");
			if (isBackMatter)
			{
				InsertCloneOfLastPageOrDefault(needImageContainer, needTextGroup);
				return;
			}

			// OK, we've found a page in the template book which can hold imported content.
			// If it can hold the sort of content we have, we'll use it; otherwise,
			// we'll insert a page that can.
			GetElementsFromCurrentPage();
			// Note that these are only updated when current page is set to an original usable page,
			// not when it is set to an inserted one. Thus, once we start adding pages at the end,
			// it and these variables are fixed at the values for the last content page.
			_lastContentPage = _currentPage;
			_lastPageHasImageContainer = _imageContainersOnPage.Count > 0;
			_lastPageHasTextGroup = _groupsOnPage.Count > 0;
			guidOfPageToClone = GuidOfPageToCopyIfNeeded(needImageContainer, needTextGroup, _lastPageHasImageContainer,
				_lastPageHasTextGroup);

			if (guidOfPageToClone != null)
			{
				GeneratePage(guidOfPageToClone); // includes progress message
				GetElementsFromCurrentPage();
			}
			else
			{
				Progress($"Updating page {PageNumberToReport}");
			}

			_imageContainerOnPageIndex = -1;
			_groupOnPageIndex = -1;
		}

		private void InsertCloneOfLastPageOrDefault(bool needImageContainer, bool needTextGroup)
		{
			// If we don't have a last page at all, the _lastPageHas variables will both be false,
			// so we'll know to create one.
			var guidOfNeededPage = GuidOfPageToCopyIfNeeded(needImageContainer, needTextGroup,
				_lastPageHasImageContainer, _lastPageHasTextGroup);
			if (guidOfNeededPage == null)
			{
				Progress($"Adding page {PageNumberToReport} by copying the last page");
				ImportPage(_lastContentPage);
			}
			else
			{
				GeneratePage(guidOfNeededPage);
			}

			GetElementsFromCurrentPage();
			_imageContainerOnPageIndex = -1;
			_groupOnPageIndex = -1;
		}

		private const string basicTextAndImageGuid = "adcd48df-e9ab-4a07-afd4-6a24d0398382";

		/// <summary>
		/// If current page does not have one of the elements we need, return the guid of a page from Basic Book
		/// that does. If current page is fine, just return null.
		/// </summary>
		private string GuidOfPageToCopyIfNeeded(bool needImageContainer, bool needTextGroup, bool haveImageContainer, bool haveTextGroup)
		{
			string guid = null;
			if (needImageContainer && !haveImageContainer)
			{
				guid = needTextGroup
					? basicTextAndImageGuid
					: "adcd48df-e9ab-4a07-afd4-6a24d0398385"; // just an image
			}
			else if (needTextGroup && !haveTextGroup)
			{
				guid = needImageContainer
					? basicTextAndImageGuid
					: "a31c38d8-c1cb-4eb9-951b-d2840f6a8bdb"; // just text
			}

			if (_bookIsLandscape && guid == basicTextAndImageGuid)
				guid = "7b192144-527c-417c-a2cb-1fb5e78bf38a"; // Picture on left
			return guid;
		}

		private List<XmlElement> GetImageContainers(XmlElement ancestor)
		{
			return ancestor.SafeSelectNodes(".//div[contains(@class, 'bloom-imageContainer')]").Cast<XmlElement>()
				.ToList();
		}

		private void GetElementsFromCurrentPage()
		{
			_imageContainersOnPage = GetImageContainers(_currentPage);
			// For now we are ignoring image description slots as possible destinations for text.
			// It's difficult to know whether a page intentionally has one or not, as empty ones
			// can easily be added by just turning on the tool, and they can confuse alignment
			// of images and main text blocks.
			var allGroups = TranslationGroupManager.SortedGroupsOnPage(_currentPage, true);
			_groupsOnPage = allGroups.Where(x => !x.Attributes["class"].Value.Contains("bloom-imageDescription")).ToList();
		}

		private void AdvanceToNextImageContainer()
		{
			_imageContainerOnPageIndex++;
			// We arrange for this to be always true initially
			if (_imageContainerOnPageIndex >= _imageContainersOnPage.Count)
			{
				AdvanceToNextNumberedPage(true, false);
				_imageContainerOnPageIndex = 0;
			}

			_currentImageContainer = _imageContainersOnPage[_imageContainerOnPageIndex];
		}

		private void AdvanceToNextGroupAndImageContainer()
		{
			_imageContainerOnPageIndex++;
			_groupOnPageIndex++;
			// We arrange for this to be always true initially
			if (_imageContainerOnPageIndex >= _imageContainersOnPage.Count || _groupOnPageIndex >= _groupsOnPage.Count)
			{
				AdvanceToNextNumberedPage(true, true);
				_imageContainerOnPageIndex = 0;
				_groupOnPageIndex = 0;
			}

			_currentImageContainer = _imageContainersOnPage[_imageContainerOnPageIndex];
			_currentGroup = _groupsOnPage[_groupOnPageIndex];
		}

		private bool RowHasText(ContentRow row)
		{
			var sheetLanguages = _sheet.Languages;
			foreach (var lang in sheetLanguages)
			{
				var colIndex = _sheet.GetRequiredColumnForLang(lang);
				var content = row.GetCell(colIndex).Content;
				if (!string.IsNullOrEmpty(content))
					return true;
			}
			return false;
		}

		public static bool IsEmptyCell(string content)
		{
			return string.IsNullOrEmpty(content)
			       || content == InternalSpreadsheet.BlankContentIndicator
				   // How the blank content indicator appears when read from spreadsheet by SpreadsheetIO
			       || content == "<p>" + InternalSpreadsheet.BlankContentIndicator + "</p>";
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
				var colIndex = _sheet.GetRequiredColumnForLang(lang);
				var content = row.GetCell(colIndex).Content;
				var editable = HtmlDom.GetEditableChildInLang(group, lang);
				if (editable == null)
				{
					if (IsEmptyCell(content))
						continue; // Review: or make an empty one?

					editable = TranslationGroupManager.MakeElementWithLanguageForOneGroup(group, lang);
				}

				if (IsEmptyCell(content))
				{
					editable.ParentNode.RemoveChild(editable);
				}
				else
				{
					editable.InnerXml = content;
				}
			}

			if (RemoveOtherLanguages)
			{
				HtmlDom.RemoveOtherLanguages(@group, sheetLanguages);
			}
		}
	}
}
