using System;
using Bloom.Book;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security;
using System.Threading;
using System.Windows.Forms;
using System.Xml;
using Bloom.MiscUI;
using Bloom.web;
using SIL.IO;
using SIL.Progress;
using SIL.Xml;
using Bloom.Collection;
using NAudio.Wave;

namespace Bloom.Spreadsheet
{
	/// <summary>
	/// Imports data from an internal spreadsheet into a bloom book.
	/// </summary>
	public class SpreadsheetImporter
	{
		private HtmlDom _destinationDom;
		private readonly Book.Book _book;
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
		private readonly XmlElement _dataDivElement;
		private readonly string _pathToSpreadsheetFolder;
		private readonly string _pathToBookFolder;
		private readonly IBloomWebSocketServer _webSocketServer;
		private IWebSocketProgress _progress;
		private int _unNumberedPagesSeen;
		private bool _bookIsLandscape;
		private Layout _destLayout;
		private readonly CollectionSettings _collectionSettings;

		public delegate SpreadsheetImporter Factory();

		/// <summary>
		/// Create an instance. The webSocketServer may be null unless using ImportWithProgress.
		/// </summary>
		/// <remarks>The web socket server is a constructor argument as a step in the direction
		/// of allowing this class to be instantiated and supplied with the socket server by
		/// AutoFac. However, for that to work, we'd need to move the other constructor arguments,
		/// which AutoFac can't know, to the Import method. And for now, all callers which need
		/// to pass a socket server already have one.</remarks>
		public SpreadsheetImporter(IBloomWebSocketServer webSocketServer, HtmlDom destinationDom, string pathToSpreadsheetFolder = null, string pathToBookFolder = null, CollectionSettings collectionSettings = null)
		{
			_destinationDom = destinationDom;
			_dataDivElement = _destinationDom.SafeSelectNodes("//div[@id='bloomDataDiv']").Cast<XmlElement>().First();
			_pathToBookFolder = pathToBookFolder;
			// Tests and CLI may not set one or more of these
			_pathToSpreadsheetFolder = pathToSpreadsheetFolder;
			_webSocketServer = webSocketServer;
			_collectionSettings = collectionSettings;
		}

		/// <summary>
		/// Used by the main SpreadsheetApi call. Tests and CLI (which don't usually have access to the book)
		/// use the other ctor.
		/// </summary>
		public SpreadsheetImporter(IBloomWebSocketServer webSocketServer, Book.Book book, string pathToSpreadsheetFolder)
			: this(webSocketServer, book.OurHtmlDom, pathToSpreadsheetFolder, book.FolderPath, book.CollectionSettings)
		{
			_book = book;
		}

		// If the import is not done on the main UI thread, a control should be passed that can be used to
		// invoke things (currently browser operations) that must be done on that thread.
		public Control ControlForInvoke { get; set; }

		/// <summary>
		/// If true, bloom-editable elements in matched translation groups which do
		/// not have a corresponding column in the input will be deleted.
		/// </summary>
		public bool RemoveOtherLanguages => Params.RemoveOtherLanguages;

		public SpreadsheetImportParams Params = new SpreadsheetImportParams();

		public void ImportWithProgress(string inputFilepath, Action doWhenProgressCloses)
		{
			Debug.Assert(_pathToBookFolder != null,
				"Somehow we made it into ImportWithProgress() without a path to the book folder");
			BrowserProgressDialog.DoWorkWithProgressDialog(_webSocketServer,   (progress, worker) =>
			{
				var cannotImportEnding = " For this reason, we need to abandon the import. Instead, you can import into a blank book.";
				var hasActivities = _destinationDom.HasActivityPages();
				if (hasActivities)
				{
					progress.MessageWithoutLocalizing($"Warning: Spreadsheet import cannot currently preserve quizzes, widgets, or other activities that are already in this book."+ cannotImportEnding, ProgressKind.Error);
					return true; // leave progress window up so user can see error.
				}
				var sheet = InternalSpreadsheet.ReadFromFile(inputFilepath, progress);
				if (sheet == null)
					return true;
				if (!Validate(sheet, progress))
					return true; // errors already reported to progress
				progress.MessageWithoutLocalizing($"Making a backup of the original book...");
				var backupPath = BookStorage.SaveCopyBeforeImportOverwrite(_pathToBookFolder);
				progress.MessageWithoutLocalizing($"Backup completed (at {backupPath})");
				var audioFolder = Path.Combine(_pathToBookFolder, "audio");
				if (Directory.Exists(audioFolder))
					SIL.IO.RobustIO.DeleteDirectoryAndContents(audioFolder);
				Import(sheet, progress);
				return true; // always leave the dialog up until the user chooses 'close'
			}, "collectionTab", "Importing Spreadsheet", doWhenDialogCloses: doWhenProgressCloses);
		}

		private Browser _browser;

		/// <summary>
		/// Get a brower with certain functions that spreadsheet import needs made available
		/// </summary>
		/// <remarks>
		/// The browser is pre-loaded with the code imported by spreadsheetBundleRoot.ts,
		/// which can be accessed using code like RunJavascript("spreadsheetBundle.split('my sentence')")
		/// to call any function which that file exports. Add more to it as needed.
		/// It's potentially tricky to debug any problems on the JS side, since the browser isn't
		/// open on the screen anywhere to debug. However, it doesn't have any links that the
		/// browser complains about as being cross-domain, so I've chosen to implement it using file urls.
		/// This means that you can simply open the root file (output/browser/spreadsheet/spreadsheetFunctions.html)
		/// and try your RunJavascript inputs in the console.
		/// Remember to escape any single quotes in data strings passed to RunJavascript!
		/// </remarks>
		protected virtual Browser GetBrowser()
		{
			if (_browser == null)
			{
				if (ControlForInvoke != null && ControlForInvoke.InvokeRequired)
				{
					ControlForInvoke.Invoke((Action)(() => GetBrowser()));
					return _browser;
				}
				// Todo Linux: I'm choosing not to do BrowserMaker.MakeBrowser here, because
				// the Gecko code to try to determine that the browser has fully loaded everything
				// is more complicated, and I'm not sure this functionality is needed on Linux,
				// and it seems there is little need to be testing this code in the browser we are
				// about to drop. So if we want this on Linux, we'll have to test carefully there,
				// and possibly make a new overload of NavigateAndWaitUntilDone if the wait code here
				// is not good enough.
#if __MonoCS__
				_browser = new GeckoFxBrowser();
#else
				_browser = new WebView2Browser();
#endif
				var done = false;
				_browser.DocumentCompleted += (sender, args) => done = true;
				var rootPage = BloomFileLocator.GetBrowserFile(false, "spreadsheet", "spreadsheetFunctions.html");
				_browser.Navigate(rootPage, false);
				// This extra check that spreadsheetBundle actually exists might not be necessary.
				while (!done || _browser.RunJavaScript($"spreadsheetBundle") == null)
				{
					Application.DoEvents();
					Thread.Sleep(10);
					// Review: may need more than this if we allow GeckoFxBrowser; see impl of NavigateAndWaitTillDone.
					// Maybe we need a URL overload of that and to factor out the wait code?
				}
			}
			return _browser;
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
			_pages = _destinationDom.GetPageElements().ToList();
			_bookIsLandscape = _pages.FirstOrDefault()?.Attributes["class"]?.Value?.Contains("Landscape") ?? false;
			_currentRowIndex = 0;
			_currentPageIndex = -1;
			_groupsOnPage = new List<XmlElement>();
			_imageContainersOnPage = new List<XmlElement>();
			_destLayout = Layout.FromDom(_destinationDom, Layout.A5Portrait);
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
			// This section is necessary to make sure changes to the dom are recorded.
			// If we run SS Importer from the CLI (without CollectionSettings), BringBookUpToDate()
			// will happen when we eventually open the book, but the user gets an updated thumbail and preview
			// if we do it here for the main production case where we DO have both the CollectionSettings
			// and the Book itself. Testing is the other situation (mostly) that doesn't use CollectionSettings.
			if (_collectionSettings != null && _book != null)
			{
				_book.BringBookUpToDate(new NullProgress());
			}

			if (_browser != null)
			{
				Action disposeBrowser = () =>
				{
					_browser.Dispose();
					_browser = null;
				};
				if (ControlForInvoke != null && ControlForInvoke.InvokeRequired)
					ControlForInvoke.Invoke(disposeBrowser);
				else
					disposeBrowser();

			}

			Progress("Done");
			return _warnings;
		}

		private void PutRowInImage(ContentRow currentRow)
		{
			var spreadsheetImgPath = currentRow.GetCell(InternalSpreadsheet.ImageSourceColumnLabel).Content;
			if (spreadsheetImgPath == InternalSpreadsheet.BlankContentIndicator)
			{
				spreadsheetImgPath = "placeHolder.png";
			}

			var destFileName = Path.GetFileName(spreadsheetImgPath);

			var imgElement = GetImgFromContainer(_currentImageContainer);
			// Enhance: warn if null?
			imgElement?.SetAttribute("src", UrlPathString.CreateFromUnencodedString(destFileName).UrlEncoded);
			// Earlier versions of Bloom often had explicit height and width settings on images.
			// In case anything of the sort remains, it probably won't be correct for the new image,
			// so best to get rid of it.
			imgElement?.RemoveAttribute("height");
			imgElement?.RemoveAttribute("width");
			// image containers often have a generated title attribute that gives the file name and
			// notes about its resolution, etc. We think it will be regenerated as needed, but certainly
			// the one from a previous image is no use.
			_currentImageContainer.RemoveAttribute("title");
			if (_pathToSpreadsheetFolder != null) //currently will only be null in tests
			{
				// To my surprise, if spreadsheetImgPath is rooted (a full path), this will just use it,
				// ignoring _pathToSpreadsheetFolder, which is what we want.
				var fullSpreadsheetPath = Path.Combine(_pathToSpreadsheetFolder, spreadsheetImgPath);
				if (spreadsheetImgPath == "placeHolder.png")
				{
					// Don't assume the source has it, let's get a copy from files shipped with Bloom
					fullSpreadsheetPath = Path.Combine(BloomFileLocator.FactoryCollectionsDirectory, "template books",
						"Basic Book", "placeHolder.png");
				}

				CopyImageFileToDestination(destFileName, fullSpreadsheetPath, imgElement);
			}
		}

		private void CopyImageFileToDestination(string destFileName, string fullSpreadsheetPath, XmlElement imgElement=null)
		{
			try
			{
				if (_pathToBookFolder != null && _pathToSpreadsheetFolder != null)
				{
					var dest = Path.Combine(_pathToBookFolder, destFileName);
					if (RobustFile.Exists(fullSpreadsheetPath))
					{
						RobustFile.Copy(fullSpreadsheetPath, dest, true);
						if (imgElement != null)
							ImageUpdater.UpdateImgMetadataAttributesToMatchImage(_pathToBookFolder, imgElement,
							new NullProgress());
					}
					else
					{
						// Review: I doubt these messages are worth localizing? The sort of people who attempt
						// spreadsheet import can likely cope with some English?
						// +1 conversion from zero-based to 1-based counting, further adding header.RowCount
						// makes it match up with the actual row label in the spreadsheet.
						Warn(
							$"Image \"{fullSpreadsheetPath}\" on row {_currentRowIndex + 1 + _sheet.Header.RowCount} was not found.");
					}
				}
			}
			catch (Exception e) when (e is IOException || e is SecurityException ||
									  e is UnauthorizedAccessException)
			{
				Warn(
					$"Bloom had trouble copying the file {fullSpreadsheetPath} to the book folder or retrieving its metadata: " +
					e.Message);
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
				templateNode = _destinationDom.RawDom.CreateElement("div");
				templateNode.SetAttribute("data-book", dataBookLabel);
			}

			var imageSrcCol = _sheet.GetColumnForTag(InternalSpreadsheet.ImageSourceColumnLabel);
			var imageSrc = imageSrcCol >= 0 ? currentRow.GetCell(imageSrcCol).Content : null; // includes "images" folder
			var imageFileName = Path.GetFileName(imageSrc);
			bool specificLanguageContentFound = false;
			bool asteriskContentFound = false;

			//Whether or not a data-book div has a src attribute, we found that the innerText is used to set the
			//src of the image in the actual pages of the document, though we haven't found a case where they differ.
			//So during export we put the innerText into the image source column, and want to put it into
			//both src and innertext on import, unless the element is in the noSrcAttribute list
			if (imageFileName.Length > 0)
			{
				templateNode.SetAttribute("lang", "*");
				templateNode.InnerText = imageFileName;

				if (!SpreadsheetExporter.DataDivImagesWithNoSrcAttributes.Contains(dataBookLabel))
				{
					templateNode.SetAttribute("src", imageFileName);
				}
				if (templateNodeIsNew)
					AddDataBookNode(templateNode);

				if (_pathToSpreadsheetFolder != null)
				{
					// Make sure the image gets copied over too.
					var fullSpreadsheetPath = Path.Combine(_pathToSpreadsheetFolder, imageSrc);
					CopyImageFileToDestination(imageFileName, fullSpreadsheetPath);
				}
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
							AddAudio(matchingNode, lang, currentRow);
						}
						else //No node for this language and data-book. Create one from template and add.
						{
							XmlElement newNode = (XmlElement)templateNode.CloneNode(deep: true);
							newNode.SetAttribute("lang", lang);
							newNode.InnerXml = langVal;
							AddDataBookNode(newNode);
							AddAudio(newNode, lang, currentRow);
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

				if (asteriskContentFound && specificLanguageContentFound && currentRow.MetadataKey != "[ISBN]")
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
			var pageLabel = templatePage.SafeSelectNodes(".//div[@class='pageLabel']").Cast<XmlElement>().FirstOrDefault()?.InnerText ?? "";
			Progress($"Adding page {PageNumberToReport} using a {pageLabel} layout");
		}

		// Insert a clone of templatePage into the document before _currentPage (or after _lastContentPage, if _currentPage is null),
		// and make _currentPage point to the new page.
		private void ImportPage(XmlElement templatePage)
		{
			var newPage = _destinationDom.RawDom.DocumentElement.OwnerDocument.ImportNode(templatePage, true) as XmlElement;
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
				var allInGroup = editables.Where(x => x.ParentNode == e.ParentNode && x != e);
				if (allInGroup.Any())
					e.ParentNode.RemoveChild(e);
				else
				{
					// The only thing in the group is an element with a language other than 'z'.
					// We want to keep this, but only as a template (e.g., to preserve the user style).
					e.SetAttribute("lang", "z");
					e.InnerText = "";
				}
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

				AddAudio(editable, lang, row);
			}

			if (RemoveOtherLanguages)
			{
				HtmlDom.RemoveOtherLanguages(@group, sheetLanguages);
			}
		}

		/// <summary>
		/// Add any audio for the given language from the row to the editable.
		/// Audio is present if the spreadsheet has a column [audio lg]
		/// (and possibly one [audio alignments lg]), and the row has data in the cell
		/// that corresponds to the [audio lg] column.
		/// If it has, we next determine whether the group is recorded in TextBox mode.
		/// This is true if the relevant [audio alignments lg] cell is non-empty.
		/// (Report an error if this is true and there is more than one recording file.)
		/// (Report an error if any audio file does not exist or has the wrong
		/// extension...possibly also if it isn't really an mp3? Not attempting that
		/// currently, though our code for getting the duration might throw.)
		/// We will set data-audiorecordingmode to TextBox or Sentence accordingly,
		/// and in TextBox mode, we will
		///  - (report error if the alignments aren't numbers, or not ascending, or [warning] larger than
		///    the audio file duration, or there is more than one but not as many as we have
		///    sentences)
		///  - copy all but the last number in alignments to data-audiorecordingendtimes.
		///    (unless some are too large or won't parse...then we do a warning and convert back to unsplit).
		///  - put the actual duration of the audio (measured from the one file) in a final entry
		///    in data-audiorecordingendtimes and also a data-duration for the whole bloom-editable.
		///  - if we have as many alignments as sentences, make appropriate
		///    bloom-highlightSegment spans out of the sentences and add class bloom-postAudioSplit to the editable.
		///  - add class audio-sentence to the bloom-editable
		///  - copy the audio file into our audio folder. Use a name that is a valid
		///    filename, a valid XML ID, does not conflict with any existing file, and
		///    otherwise as similar to the given name as possible
		///  - give the bloom-editable an id corresponding to the file
		///  - make a recordingMD5 as usual.
		/// If instead we're in sentence mode (no alignment data),
		///  - (report an error if we don't have one audio file per sentence, or if any are
		///    missing that don't say they are)
		///  - make a span for each sentence with
		///    - class audio-sentence
		///    - recordingmd5 computed as usual (assume the recording is current)
		///    - id derived by copying the corresponding audio file, as above
		///    - data-duration attribute computed from the file
		/// </summary>
		/// <param name="editable"></param>
		/// <param name="lang"></param>
		/// <param name="row"></param>
		private void AddAudio(XmlElement editable, string lang, ContentRow row)
		{
			// in case we're importing into an existing page, remove any existing audio-related stuff first.
			// We want to do this even if there isn't any new audio stuff.
			editable.RemoveAttribute("data-duration");
			editable.RemoveAttribute("data-audiorecordingendtimes");
			editable.RemoveAttribute("recordingmd5");
			HtmlDom.RemoveClass(editable, "bloom-postAudioSplit");
			editable.RemoveAttribute("data-audiorecordingmode");
			HtmlDom.RemoveClass(editable, "audio-sentence");
			foreach (var span in editable.SafeSelectNodes("span[@class]").Cast<XmlElement>().ToArray())
			{
				var className = span.Attributes["class"].Value;
				if (className == "audio-sentence" || className == "bloom-highlightSegment")
				{
					// remove these spans, keeping content
					foreach (XmlNode node in span.ChildNodes)
					{
						span.ParentNode.InsertBefore(node, span);
					}
					span.ParentNode.RemoveChild(span);
				}
			}

			var audioColIndex = _sheet.GetOptionalColumnForLangAudio(lang);
			if (audioColIndex == -1)
				return; // no audio data for this language at all.
			if (_pathToSpreadsheetFolder == null)
				return; // happens during unit tests not focused on audio
			var audioFilesList = row.GetCell(audioColIndex).Content;
			// We need the 'where' because Split creates a single empty string if the input is empty.
			var audioFiles = audioFilesList.Split(',').Select(x => x.Trim())
				.Where(x => x != "").ToArray();
			var audioAlignColIndex = _sheet.GetOptionalColumnForAudioAlignment(lang);
			var alignmentData = "";
			if (audioAlignColIndex >= 0)
			{
				alignmentData = row.GetCell(audioAlignColIndex).Content;
			}

			if (audioFiles.Length == 0 && string.IsNullOrEmpty(alignmentData))
				return; // OK to have no audio at all, even if the columns are there.

			var alignments = alignmentData.Split(new[] {' '}, StringSplitOptions.RemoveEmptyEntries);
			var paras = editable.SafeSelectNodes(".//p").Cast<XmlElement>();
			// In this variable we build a list of paragraphs and their sentences, for later processing
			// after we make some sanity checks. The immediate goal is to count sentences that could
			// have recording or alignment information.
			var paraFragments = new List<Tuple<XmlElement, string[]>>();
			int sentenceCount = 0;
			foreach (var para in paras)
			{
				var text = para.InnerXml.Replace("'", "\\'");
				// Sentences is a sequence of strings, each of which is the text of a JS TextFragment,
				// prepended with 's' if it's a sentence and ' ' if it's not.
				string[] fragments = new string[0];
				if (text.Length > 0) // if not, we get a spurious empty string instead of an empty array.
				{
					Action runJs = () =>
						fragments = GetSentenceFragments(text);
					if (ControlForInvoke != null && ControlForInvoke.InvokeRequired)
						ControlForInvoke.Invoke(runJs);
					else
						runJs();
				}

				paraFragments.Add(Tuple.Create(para, fragments));
				sentenceCount += fragments.Count(x => x.StartsWith("s"));
			}

			if (alignments.Length > 0)
			{
				// The presence of alignment data indicates TextBox recording mode, so the editable should
				// only have one audio file.
				if (audioFiles.Length > 1)
				{
					_progress.MessageWithParams("TooManyAudioFilesForAlignment", "",
						"Did not import audio on page {0} because there should be only one audio file when audio alignment is specified.",
						ProgressKind.Error, PageNumberToReport.ToString());
					return;
				}
				var audioFile = audioFilesList;
				var durationStr = AddAudioFile(editable, audioFile, row);
				if (String.IsNullOrEmpty(durationStr))
					return; // already reported, just don't add any audio information.
				var duration = double.Parse(durationStr, CultureInfo.InvariantCulture);
				editable.SetAttribute("data-audiorecordingmode", "TextBox");
				HtmlDom.AddClass(editable, "audio-sentence");
				var alignmentChecks = alignments.Select(x =>
				{
					if (double.TryParse(x, NumberStyles.Any, CultureInfo.InvariantCulture, out double val))
					{
						if (val > duration + 0.02)
						{
							return "toobig";
						}

						return "good";
					}

					return "bad";
				});
				// Yes, we might be asking for -1 alignmentChecks, that just returns none.
				// Decided not to check whether the last (often only) alignment is too big, since we will
				// automatically correct it to the actual duration anyway.
				if (alignmentChecks.Take(alignments.Length - 1).Any(x => x == "toobig"))
				{
					_progress.MessageWithParams("AlignmentsTooBig", "",
						"Removed audio alignments on page {0} because some values in the list given ('{1}') are larger than the duration of the audio file ({2}).",
						ProgressKind.Warning, PageNumberToReport.ToString(), alignmentData, durationStr);
					return;
				}

				if (alignmentChecks.Any(x => x == "bad"))
				{
					_progress.MessageWithParams("AlignmentsInvalid", "",
						"Removed audio alignments on page {0} because some values in '{1}' are not valid numbers.",
						ProgressKind.Warning, PageNumberToReport.ToString(), alignmentData);
					return;
				}

				if (sentenceCount == alignments.Length)
				{
					// treat as split TextBox (although just possibly it was originally a single sentence
					// recorded in TextBox mode and never split)

					// Break it up into spans with ids and the bloom-highlightSegment class
					foreach (var paraGroup in paraFragments)
					{
						var para = paraGroup.Item1;
						var fragments = paraGroup.Item2;

						para.InnerText = "";
						foreach (var taggedFragment in fragments)
						{
							var fragment = taggedFragment.Substring(1);
							if (taggedFragment.StartsWith("s"))
							{
								var span = para.OwnerDocument.CreateElement("span");
								HtmlDom.SetNewHtmlIdValue(span); // need it to have one, don't care what
								span.SetAttribute("class", "bloom-highlightSegment");
								span.InnerXml = fragment;
								para.AppendChild(span);
							}
							else
							{
								// a white space fragment.
								var node = para.OwnerDocument.CreateTextNode(fragment);
								para.AppendChild(node);
							}
						}
					}

					// Set the end times, using the given data except for the last one, which should
					// be the accurate duration.
					var alignmentsVal = durationStr;
					if (alignments.Length > 1)
						alignmentsVal = String.Join(" ", alignments.Take(alignments.Length - 1)) + " " + durationStr;
					editable.SetAttribute("data-audiorecordingendtimes", alignmentsVal);
					HtmlDom.AddClass(editable, "bloom-postAudioSplit");
				}
				// If there is just one alignment, but not just one sentence, we're in TextBox mode, unsplit.
				// There's nothing more to do. But any other non-matching number is an error.
				else if(alignments.Length != 1)
				{
					_progress.MessageWithParams("AlignmentMismatch", "",
						"Did not import audio alignments on page {0} because there are {1} audio alignments for {2} sentences; they should match up.",
						ProgressKind.Error, PageNumberToReport.ToString(), alignments.Length.ToString(), sentenceCount.ToString());
					return;
				}
			}
			else
			{
				// Sentence mode
				if (audioFiles.Length != sentenceCount)
				{
					_progress.MessageWithParams("AudioFileMismatch", "",
						"Did not import audio on page {0} because there are {1} audio files for {2} sentences; they should match up. Use 'missing' if necessary.",
						ProgressKind.Error, PageNumberToReport.ToString(), audioFiles.Length.ToString(), sentenceCount.ToString());
					return;
				}
				editable.SetAttribute("data-audiorecordingmode", "Sentence");
				// Break it up into spans with ids, the audio-sentence class, etc.
				var audioFileIndex = 0;
				foreach (var paraGroup in paraFragments)
				{
					var para = paraGroup.Item1;
					var fragments = paraGroup.Item2;

					para.InnerText = "";
					foreach (var taggedFragment in fragments)
					{
						var fragment = taggedFragment.Substring(1);
						if (taggedFragment.StartsWith("s"))
						{
							var span = para.OwnerDocument.CreateElement("span");
							var audioFile = audioFiles[audioFileIndex++];
							span.InnerXml = fragment;
							AddAudioFile(span, audioFile, row);
							HtmlDom.AddClass(span, "audio-sentence");
							para.AppendChild(span);
						}
						else
						{
							var node = para.OwnerDocument.CreateTextNode(fragment);
							para.AppendChild(node);
						}
					}
				}
			}
		}

		protected virtual string[] GetSentenceFragments(string text)
		{
			return GetBrowser().RunJavaScript($"spreadsheetBundle.split('{text}')").Split('\n');
		}


		/// <summary>
		/// Add an audio file to the book, linked by adding an id matching its name to the element.
		/// The audio file will be copied to the book's audio folder.
		/// The name may be changed in various circumstances.
		/// Also sets the data-duration and recordingmd5 attributes of the element, which means
		/// it is important to finalize the text of the element before calling this.
		/// </summary>
		/// <param name="elt"></param>
		/// <param name="audioFile"></param>
		/// <returns>a string representation of the duration of the audio file, in seconds, or an empty string,
		/// if we don't find a valid audio file.</returns>
		private string AddAudioFile(XmlElement elt, string audioFile, ContentRow row)
		{
			if (audioFile == "missing")
			{
				HtmlDom.SetNewHtmlIdValue(elt);
				return "0";
			}
			var destFile = SanitizeXHtmlId(BookStorage.SanitizeNameForFileSystem(Path.GetFileName(audioFile)));
			var id = Path.GetFileNameWithoutExtension(destFile);
			// We may as well set this; elements with class audio-sentence are supposed to have
			// ids, even if there is no corresponding file.
			elt.SetAttribute("id", id);
			if (_pathToSpreadsheetFolder == null)
				return "0"; // unit tests, we can't try to copy file.
			string src = audioFile;
			if (audioFile.StartsWith("./"))
				src = Path.Combine(_pathToSpreadsheetFolder, audioFile.Substring(2));
			if (RobustFile.Exists(src))
			{
				var audioPath = Path.Combine(_pathToBookFolder, "audio");
				Directory.CreateDirectory(audioPath);
				var destPath = Path.Combine(audioPath, destFile);
				if (RobustFile.Exists(destPath))
				{
					id = HtmlDom.SetNewHtmlIdValue(elt);
					destPath = Path.Combine(audioPath, id + ".mp3");
				}

				double duration;
				try
				{
					duration = GetDuration(src);
				}
				catch (InvalidDataException ex)
				{
					_progress.MessageWithParams("InvalidMp3", "",
						"Did not import audio on page {0} because the audio file '{1}' is not a valid mp3 file.",
						ProgressKind.Error, PageNumberToReport.ToString(), src.Replace("\\", "/"));
					return "0";
				}
				RobustFile.Copy(src, destPath);
				var durationStr = duration.ToString(CultureInfo.InvariantCulture);
				elt.SetAttribute("data-duration", durationStr);
				string md5 = ""; // value is unused, but compiler gets confused
				Action runJs = () => md5 = GetMd5(elt);
				if (ControlForInvoke != null && ControlForInvoke.InvokeRequired)
					ControlForInvoke.Invoke(runJs);
				else
					runJs();
				elt.SetAttribute("recordingmd5", md5);
				return durationStr;
			}

			src = src.Replace("\\", "/"); // works on all platforms, simplifies testing
			var rowId = row.GetCell(0).Content;
			if (rowId == InternalSpreadsheet.PageContentRowLabel)
			{
				_progress.MessageWithParams("MissingAudioFile", "",
					"Did not import audio on page {0} because '{1}' was not found.",
					ProgressKind.Error, PageNumberToReport.ToString(), src);
			}
			else
			{
				_progress.MessageWithParams("MissingAudioFile", "",
					"Did not import audio for {0} because '{1}' was not found.",
					ProgressKind.Error, rowId.Trim(new[] {'[',']'}), src);
			}

			return "";
		}

		protected virtual string GetMd5(XmlElement elt)
		{
			return GetBrowser().RunJavaScript($"spreadsheetBundle.getMd5('{elt.InnerText.Replace("'", "\\'")}')");
		}

		internal static string SanitizeXHtmlId(string id)
		{
			if (!XmlConvert.IsStartNCNameChar(id[0]))
				id = 'i' + id;
			for (int i = 0; i < id.Length; )
			{
				if (!XmlConvert.IsNCNameChar(id[i]))
					id = id.Replace(id[i].ToString(), "");
				else
					i++;
			}
			if (id.Length > 1)
				return id;
			return "defaultId"; // arbitrary, and likely a duplicate, but at least valid.
		}

		private double GetDuration(string path)
		{
			return Utils.MiscUtils.GetMp3TimeSpan(path, true).TotalSeconds;
		}
	}
}
