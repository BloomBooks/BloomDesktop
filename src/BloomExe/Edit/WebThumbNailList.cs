using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.Design;
using System.Diagnostics;
using System.Drawing;
using System.Text;
using System.Windows.Forms;
using System.Linq;
using System.Runtime.InteropServices;
using System.Xml;
using Bloom.Book;
using Bloom.Api;
using Bloom.MiscUI;
using Gecko;
using SIL.Windows.Forms.Reporting;
using SIL.Xml;
using L10NSharp;
using SIL.IO;
using SIL.Reporting;

namespace Bloom.Edit
{
	public partial class WebThumbNailList : UserControl
	{
		public HtmlThumbNailer Thumbnailer;
		public event EventHandler PageSelectedChanged;
		private Bloom.Browser _browser;
		internal EditingModel Model;
		private int _verticalScrollDistance;
		private static string _thumbnailInterval;
		private string _baseForRelativePaths;

		// Store this so we don't have to reload the thing from disk everytime we refresh the screen.
		private readonly string _baseHtml;

		/// <summary>
		/// Contains all the IPage objects currently displayed in the grid, keyed on "page-" + IdGuid.
		/// </summary>
		private readonly Dictionary<string, IPage> _pageMap = new Dictionary<string, IPage>();
		private List<IPage> _pages;
		private bool _usingTwoColumns;
		/// <summary>
		/// The CSS class we give the main div for each page; the same element always has an id attr which identifies the page.
		/// </summary>
		private const string GridItemClass = "gridItem";
		private const string PageContainerClass = "pageContainer";
		private const string InvisbleThumbClass = "invisibleThumbnailCover";

		internal class MenuItemSpec
		{
			public string Label;
			public Func<IPage, bool> EnableFunction;
			public Action<IPage> ExecuteCommand;
		}

		// A list of menu items that should be in both the web browser's right-click menu and
		// the one we show ourselves when the arrow is clicked. The second item in the tuple
		// determines whether the item should be enabled; the third performs the action.
		internal List<MenuItemSpec> ContextMenuItems { get; set; }

		public WebThumbNailList()
		{
			InitializeComponent();

			if (!ReallyDesignMode)
			{
				_browser = new Browser();
				_browser.BackColor = Color.DarkGray;
				_browser.Dock = DockStyle.Fill;
				_browser.Location = new Point(0, 0);
				_browser.Name = "_browser";
				_browser.Size = new Size(150, 491);
				_browser.TabIndex = 0;
				_browser.VerticalScroll.Visible = false;
				Controls.Add(_browser);
			}

			// set the thumbnail interval based on physical RAM
			if (string.IsNullOrEmpty(_thumbnailInterval))
			{
				var memInfo = MemoryManagement.GetMemoryInformation();

				// We need to divide by 1024 three times rather than dividing by Math.Pow(1024, 3) because the
				// later will force floating point math, producing incorrect results.
				var physicalMemGb = Convert.ToDecimal(memInfo.TotalPhysicalMemory) / 1024 / 1024 / 1024;

				if (physicalMemGb < 2.5M) // less than 2.5 GB physical RAM
				{
					_thumbnailInterval = "400";
				}
				else if (physicalMemGb < 4M) // less than 4 GB physical RAM
				{
					_thumbnailInterval = "200";
				}
				else  // 4 GB or more physical RAM
				{
					_thumbnailInterval = "100";
				}
			}
			var frame = BloomFileLocator.GetBrowserFile(false, "bookEdit", "pageThumbnailList", "pageThumbnailList.html");
			var backColor = ColorToHtmlCode(BackColor);
			_baseHtml = RobustFile.ReadAllText(frame, Encoding.UTF8).Replace("DarkGray", backColor);
		}

		public Func<GeckoContextMenuEventArgs, bool> ContextMenuProvider
		{
			get { return _browser.ContextMenuProvider; }
			set { _browser.ContextMenuProvider = value; }
		}

		protected bool ReallyDesignMode
		{
			get
			{
				return (base.DesignMode || GetService(typeof(IDesignerHost)) != null) ||
					(LicenseManager.UsageMode == LicenseUsageMode.Designtime);
			}
		}

		private void InvokePageSelectedChanged(IPage page)
		{
			EventHandler handler = PageSelectedChanged;

			// We're not really sure which of the two times will be shorter,
			// since there is SOME stuff that happens in C# after telling the browser
			// to navigate to the new page. This counts how many of the two reports have
			// been sent, so we can stop the watch when we're done with it.
			int reportsSent = 0;

			if (handler != null && /*REVIEW */ page != null)
			{
				var stopwatch = Stopwatch.StartNew();
				Browser.RequestJsNotification("editPagePainted", () =>
				{
					var paintTime = stopwatch.ElapsedMilliseconds;
					if (reportsSent++ >= 2)
						stopwatch.Stop();
					TroubleShooterDialog.Report($"page change to paint complete took {paintTime} milliseconds");
				});
				handler(page, null);
				var time = stopwatch.ElapsedMilliseconds;
				if (reportsSent++ >= 2)
					stopwatch.Stop();
				TroubleShooterDialog.Report($"C# part of page change took {time} milliseconds");
			}
		}

		public bool PreferPageNumbers { get; set; }

		public RelocatePageEvent RelocatePageEvent { get; set; }
		public void EmptyThumbnailCache()
		{

		}

		public void SetPageInsertionPoint(IPage determinePageWhichWouldPrecedeNextInsertion)
		{

		}

		private IPage _selectedPage;

		public void SelectPage(IPage page)
		{
			if (InvokeRequired)
				Invoke((Action)(() => SelectPageInternal(page)));
			else
				SelectPageInternal(page);
		}

		private void SelectPageInternal(IPage page)
		{
			if (_selectedPage != null && _selectedPage != page)
			{
				var oldGridElt = GetGridElementForPage(_selectedPage);
				if (oldGridElt != null)
				{
					var oldClassContent = oldGridElt.GetAttribute("class");
					oldGridElt.SetAttribute("class", oldClassContent.Replace(" gridSelected", ""));
				}
			}
			_selectedPage = page;
			if (page == null)
				return;
			var gridElt = GetGridElementForPage(page);
			if (gridElt == null)
				return; // Can't find it yet, will try again after we next build pages.
			var classContent = gridElt.GetAttribute("class");
			if (classContent.Contains("gridSelected"))
				return;
			gridElt.SetAttribute("class", classContent + " gridSelected");
			var menuElt = GetElementForMenuHolder();
			menuElt.ParentElement.RemoveChild(menuElt);
			gridElt.AppendChild(menuElt);
		}

		string ColorToHtmlCode(Color color)
		{
			// thanks to http://stackoverflow.com/questions/982028/convert-net-color-objects-to-hex-codes-and-back
			return string.Format("#{0:X2}{1:X2}{2:X2}", color.R, color.G, color.B);
		}

		public void SetItems(IEnumerable<IPage> pages)
		{
			_pages = UpdateItems(pages);
		}

		private bool RoomForTwoColumns => Width > 199;

		protected override void OnSizeChanged(EventArgs e)
		{
			base.OnSizeChanged(e);
			if (_pages != null && _pages.Count > 0 && RoomForTwoColumns != _usingTwoColumns)
				UpdateItems(_pages);
		}

		private void OnPaneContentsChanging(bool hasPages)
		{
			RemoveThumbnailListeners();
			// This check avoids a javascript error where "'stopListeningForSave' is not a function."
			if (hasPages)
			{
				// We get confusing Javascript errors if the websocket made for the previous version of this page
				// is still listening after the browser has navigated away to a new version of the page.
				// This used to be part of RemoveThumbnailListeners(), but that function is used elsewhere such that
				// we lost the Saving... toast functionality.
				_browser.RunJavaScript("if (typeof FrameExports !== 'undefined') {FrameExports.stopListeningForSave();}");
			}
		}

		public void UpdateAllThumbnails()
		{
			UpdateItems(_pages);
		}

		private List<IPage> UpdateItems(IEnumerable<IPage> pages)
		{
			List<IPage> result = null;
			if (InvokeRequired)
			{
				Invoke((Action) (() => result = UpdateItemsInternal(pages)));
			}
			else
			{
				result = UpdateItemsInternal(pages);
			}

			return result;
		}

		private List<IPage> UpdateItemsInternal(IEnumerable<IPage> pages)
		{
			OnPaneContentsChanging(pages.Any());
			var result = new List<IPage>();
			if (pages.FirstOrDefault(p => p.Book != null) == null)
			{
				_browser.Navigate(@"about:blank", false); // no pages, we just want a blank screen, if anything.
				return result;
			}

			// We can't use 'RoomForTwoColumns' in the ctor (where we set _baseHtml),
			// because its value isn't correct at that point.
			_usingTwoColumns = RoomForTwoColumns;
			var htmlText = _baseHtml;
			if (!RoomForTwoColumns)
				htmlText = htmlText.Replace("columns: 4", "columns: 2").Replace("<div class=\"gridItem placeholder\" id=\"placeholder\"></div>", "");

			// We will end up navigating to pageListDom
			var pageListDom = new HtmlDom(XmlHtmlConverter.GetXmlDomFromHtml(htmlText));

			if (SIL.PlatformUtilities.Platform.IsLinux)
				OptimizeForLinux(pageListDom);

			pageListDom = Model.CurrentBook.GetHtmlDomReadyToAddPages(pageListDom);
			var pageDoc = pageListDom.RawDom;

			var gridlyParent = SetupGridlyParent(pageDoc);

			var pageNumber = 0;
			_pageMap.Clear();
			foreach (var page in pages)
			{
				var pageElement = page.GetDivNodeForThisPage();
				if (pageElement == null)
					continue; // or crash? How can this happen?
				result.Add(page);
				var layoutCssClass = GetLayoutCssClass(pageElement);
				var pageElementForThumbnail = CreatePageElementForThumbnail(pageDoc, pageElement, layoutCssClass);

				var gridId = GridId(page);
				var cellDiv = CreatePageGridWrapper(pageDoc, gridId);
				_pageMap.Add(gridId, page);
				gridlyParent.AppendChild(cellDiv);

				var pageContainer = WrapPageAndAddInvisibleCover(pageDoc, pageElementForThumbnail, layoutCssClass);
				cellDiv.AppendChild(pageContainer);

				pageNumber = AddCaptionToCell(pageDoc, cellDiv, page, pageNumber);
			} // end pages foreach

			_browser.WebBrowser.DocumentCompleted += WebBrowser_DocumentCompleted;
			// Save the scroll position of the thumbnail list to restore in WebBrowser_DocumentCompleted.
			// Note that the position we want is NOT _browser.VerticalScrollDistance (as in previous code),
			// since the actual list of pages is no longer the whole content of this browser; the
			// page controls are at the bottom and don't scroll. So we need the scroll position of
			// the element that overflows and scrolls.
			// it's awkward that we have to convert to a string and back, but the current version of
			// RunJavaScript does not support returning anything but strings.
			// (The TryParse is probably not necessary...in an early version of this code I was getting
			// nulls back sometimes, it may no longer be possible.)
			int.TryParse(_browser.RunJavaScript("(document.getElementById('pageGridWrapper')) ? document.getElementById('pageGridWrapper').scrollTop.toString() : '0'"), out _verticalScrollDistance);
			_baseForRelativePaths = pageListDom.BaseForRelativePaths;
			_browser.Navigate(pageListDom, source:BloomServer.SimulatedPageFileSource.Pagelist);
			return result;
		}

		private static XmlElement CreatePageElementForThumbnail(XmlDocument pageDoc, XmlNode pageElement,
			string layoutCssClass)
		{
			XmlElement pageElementForThumbnail;
			if (!TroubleShooterDialog.MakeEmptyPageThumbnails)
			{
				pageElementForThumbnail = pageDoc.ImportNode(pageElement, true) as XmlElement;

				// BL-1112: Reduce size of images in page thumbnails.
				// We are setting the src to empty so that we can use JavaScript to request the thumbnails
				// in a controlled manner that should reduce the likelihood of not receiving the image quickly
				// enough and displaying the alt text rather than the image.
				DelayAllImageNodes(pageElementForThumbnail);
			}
			else
			{
				// Just make a minimal placeholder to get the white border.
				pageElementForThumbnail = pageDoc.CreateElement("div");
				var pageClasses = "bloom-page";
				// The page needs to have one of the classes like A4Portrait or it will have zero size and vanish.
				if (!string.IsNullOrEmpty(layoutCssClass))
					pageClasses += " " + layoutCssClass;
				pageElementForThumbnail.SetAttribute("class", pageClasses);
			}

			return pageElementForThumbnail;
		}

		private static int AddCaptionToCell(XmlDocument pageDoc, XmlNode cellDiv, IPage page, int pageNumber)
		{
			var captionDiv = pageDoc.CreateElement("div");
			captionDiv.SetAttribute("class", "thumbnailCaption");
			cellDiv.AppendChild(captionDiv);
			string captionI18nId;
			var captionOrPageNumber = page.GetCaptionOrPageNumber(ref pageNumber, out captionI18nId);
			if (!string.IsNullOrEmpty(captionOrPageNumber))
				captionDiv.InnerText = I18NApi.GetTranslationDefaultMayNotBeEnglish(captionI18nId, captionOrPageNumber);
			return pageNumber;
		}

		private static XmlElement WrapPageAndAddInvisibleCover(XmlDocument pageDoc, XmlNode pageElementForThumbnail,
			string layoutCssClass)
		{
			// We wrap our incredible-shrinking page in a plain 'ol div so that we
			// have something to give a border to when this page is selected
			var pageContainer = pageDoc.CreateElement("div");
			pageContainer.SetAttribute("class", PageContainerClass);
			if (pageElementForThumbnail != null)
			{
				pageContainer.AppendChild(pageElementForThumbnail);
			}

			// We need an invisible div covering our pageContainer, so that the user can't actually click
			// on the underlying "incredible-shrinking page".
			var invisibleCover = pageDoc.CreateElement("div");
			invisibleCover.SetAttribute("class", InvisbleThumbClass);
			pageContainer.AppendChild(invisibleCover);

			// And here it gets fragile (or not).
			// The nature of how we're doing the thumbnails (relying on scaling) seems to mess up
			// the browser's normal ability to assign a width to the parent div. So our parent
			// here, .pageContainer, doesn't grow with the size of its child. Sigh. So for the
			// moment, we assign appropriate sizes, by hand. We rely on c# code to add these
			// classes, since we can't write a rule in css3 that peeks into a child attribute.

			if (!string.IsNullOrEmpty(layoutCssClass))
				pageContainer.SetAttribute("class", "pageContainer " + layoutCssClass);

			return pageContainer;
		}

		private static void OptimizeForLinux(HtmlDom pageListDom)
		{
			// BL-987: Add styles to optimize performance on Linux
			var style = pageListDom.RawDom.CreateElement("style");
			style.InnerXml =
				"img { image-rendering: optimizeSpeed; image-rendering: -moz-crisp-edges; image-rendering: crisp-edges; }";
			pageListDom.RawDom.GetElementsByTagName("head")[0].AppendChild(style);
		}

		private static string GetLayoutCssClass(XmlElement pageElement)
		{
			var pageClasses = pageElement.GetStringAttribute("class").Split(new[] { ' ' });
			return pageClasses.FirstOrDefault(c => c.ToLowerInvariant().EndsWith("portrait") || c.ToLower().EndsWith("landscape"));
		}

		private static XmlNode SetupGridlyParent(XmlDocument pageDoc)
		{
			var body = pageDoc.GetElementsByTagName("body")[0];

			// set interval based on physical RAM
			var intervalAttrib = pageDoc.CreateAttribute("data-thumbnail-interval");
			intervalAttrib.Value = _thumbnailInterval;
			body.Attributes?.Append(intervalAttrib);

			return body.SelectSingleNode("//*[@id='pageGrid']");
		}

		private static XmlElement CreatePageGridWrapper(XmlDocument pageDoc, string gridId)
		{
			var cellDiv = pageDoc.CreateElement("div");
			cellDiv.SetAttribute("class", GridItemClass);
			cellDiv.SetAttribute("id", gridId);
			return cellDiv;
		}

		private static void DelayAllImageNodes(XmlElement pageElementForThumbnail)
		{
			var imgNodes = HtmlDom.SelectChildImgAndBackgroundImageElements(pageElementForThumbnail);
			if (imgNodes != null)
			{
				foreach (XmlElement imgNode in imgNodes)
				{
					var imageElementUrl = HtmlDom.GetImageElementUrl(imgNode);
					imgNode.SetAttribute("thumb-src", imageElementUrl.PathOnly.UrlEncoded + imageElementUrl.QueryOnly.NotEncoded);
					HtmlDom.SetImageElementUrl(new ElementProxy(imgNode), UrlPathString.CreateFromUrlEncodedString(""));
				}
			}
		}

		private static void MarkImageNodesForThumbnail(XmlElement pageElementForThumbnail)
		{
			var imgNodes = HtmlDom.SelectChildImgAndBackgroundImageElements(pageElementForThumbnail);
			if (imgNodes != null)
			{
				foreach (XmlElement imgNode in imgNodes)
				{
					//We can't handle doing anything special with these /api/branding/ images yet, they get mangled.
					var imageElementUrl = HtmlDom.GetImageElementUrl(imgNode);
					if(imageElementUrl.NotEncoded.Contains("/api/"))
						continue;

					var filename = imageElementUrl.PathOnly.UrlEncoded;
					if(!string.IsNullOrWhiteSpace(filename))
					{
						var url = filename + "?thumbnail=1";
						var query = imageElementUrl.QueryOnly;
						if (query.NotEncoded.Length > 0)
						{
							// Already has query, add another parameter. (e.g.: at one point we used optional=true for branding images).
							// It's important that the query be not encoded, otherwise the %3f for question mark
							// gets interpreted as part of the filename.
							url = filename + query.NotEncoded + "&thumbnail=1";
						}
						// It's not strictly true that url here is unencoded. In fact it contains a file path that IS
						// encoded. But also a query that isn't. So we need to treat it as unencoded.
						HtmlDom.SetImageElementUrl(new ElementProxy(imgNode), UrlPathString.CreateFromUnencodedString(url));
					}
				}
			}
		}

		private static string GridId(IPage page)
		{
			return "page-" + page.Id;
		}

		void WebBrowser_DocumentCompleted(object sender, Gecko.Events.GeckoDocumentCompletedEventArgs e)
		{
			AddThumbnailListeners();
			SelectPage(_selectedPage);
			// Since we always put this element in and the document is supposed to be completed I don't see how this can not find it,
			// but it happens, so adding defensive code...
			_browser.RunJavaScript("if (document.getElementById('pageGridWrapper')) {document.getElementById('pageGridWrapper').scrollTop =" + _verticalScrollDistance + ";}");
		}

		private void AddThumbnailListeners()
		{
			_browser.AddMessageEventListener("gridClick", ItemClick);
			_browser.AddMessageEventListener("gridReordered", GridReordered);
			_browser.AddMessageEventListener("menuClicked", MenuClick);
		}

		private void RemoveThumbnailListeners()
		{
			_browser.RemoveMessageEventListener("gridClick");
			_browser.RemoveMessageEventListener("gridReordered");
			_browser.RemoveMessageEventListener("menuClicked");
		}

		private void ItemClick(string s)
		{
			IPage page;
			if (_pageMap.TryGetValue(s, out page))
				InvokePageSelectedChanged(page);
		}

		private void MenuClick(string pageId)
		{
			IPage page;
			var menu = new ContextMenuStrip();
			if (!_pageMap.TryGetValue(pageId, out page))
				return;
			foreach (var item in ContextMenuItems)
			{
				var useItem = item; // for use in Click action (reference to loop variable has unpredictable results)
				var menuItem = new ToolStripMenuItem(item.Label);
				menuItem.Click += (sender, args) => useItem.ExecuteCommand(page);
				menuItem.Enabled = item.EnableFunction(page);
				menu.Items.Add(menuItem);
			}
			if (SIL.PlatformUtilities.Platform.IsLinux)
			{
				// The LostFocus event is never received by the ContextMenuStrip on Linux/Mono, and the menu
				// stays open when the user clicks elsewhere in the Edit tool area of the Bloom window.
				// To minimize user frustration, we hook up the local browser and the EditingView browser
				// mouse click handlers to explicitly close the menu.  Perhaps fixing the Mono bug would
				// be better, but I'd rather not go there if I don't have to.
				// See https://issues.bloomlibrary.org/youtrack/issue/BL-6753.
				_browser.OnBrowserClick += Browser_Click;
				Model.GetEditingBrowser().OnBrowserClick += Browser_Click;
				_popupPageMenu = menu;
			}
			menu.Show(MousePosition);
		}

		ContextMenuStrip _popupPageMenu;
		private void Browser_Click(object sender, EventArgs e)
		{
			if (_popupPageMenu != null)
			{
				_popupPageMenu.Close(ToolStripDropDownCloseReason.CloseCalled);
				_popupPageMenu = null;
				_browser.OnBrowserClick -= Browser_Click;
				Model.GetEditingBrowser().OnBrowserClick -= Browser_Click;
			}
		}

		/// <summary>
		/// Given a particular node (typically one the user right-clicked), determine whether it is clearly part of
		/// a particular page (inside a PageContainerClass div).
		/// If so, return the corresponding Page object. If not, return null.
		/// </summary>
		/// <param name="clickNode"></param>
		/// <returns></returns>
		internal IPage GetPageContaining(GeckoNode clickNode)
		{
			bool gotPageElt = false;
			for (var elt = clickNode as GeckoElement ?? clickNode.ParentElement; elt != null; elt = elt.ParentElement)
			{
				var classAttr = elt.Attributes["class"];
				if (classAttr != null)
				{
					var className = " " + classAttr.NodeValue + " ";
					if (className.Contains(" " + PageContainerClass + " "))
					{
						// Click is inside a page element: can succeed. But it's not this one.
						gotPageElt = true;
						continue;
					}
					if (className.Contains(" " + GridItemClass + " "))
					{
						if (!gotPageElt)
							return null; // clicked somewhere in a grid, but not actually on the page: intended page may be ambiguous.
						var id = elt.Attributes["id"].NodeValue;
						IPage page;
						_pageMap.TryGetValue(id, out page);
						return page;
					}
				}
			}
			return null;
		}

		private void GridReordered(string s)
		{
			var newSeq = new List<IPage>();
			var keys = s.Split(new [] {','}, StringSplitOptions.RemoveEmptyEntries);
			foreach (var key in keys)
			{
				IPage page;
				if (_pageMap.TryGetValue(key, out page))
					newSeq.Add(page);
			}
			Debug.Assert(newSeq.Count == _pages.Count);
			// Now, which one moved?
			int firstDiff = 0;
			while (firstDiff < _pages.Count && _pages[firstDiff] == newSeq[firstDiff])
				firstDiff++;
			int limDiff = _pages.Count;
			while (limDiff > firstDiff && _pages[limDiff-1] == newSeq[limDiff-1])
				limDiff--;
			if (firstDiff == limDiff)
				return; // spurious notification somehow? Nothing changed.
			// We have the subsequence that altered.
			// Is the change legal?
			for (int i = firstDiff; i < limDiff; i++)
			{
				if (!_pages[i].CanRelocate)
				{
					var msg = LocalizationManager.GetString("EditTab.PageList.CantMoveXMatter",
						"That change is not allowed. Front matter and back matter pages must remain where they are.");
					//previously had a caption that didn't add value, just more translation work
					if (_pages[i].Book.LockedDown)
					{
						msg = LocalizationManager.GetString("PageList.CantMoveWhenTranslating",
							"Pages can not be re-ordered when you are translating a book.");
						msg = msg + System.Environment.NewLine+ EditingView.GetInstructionsForUnlockingBook();
					}
					MessageBox.Show(msg);
					UpdateItems(_pages); // reset to old state
					return;
				}
			}
			// There are two possibilities: the user dragged the item that used to be at the start to the end,
			// or the item that used to be the end to the start.
			IPage movedPage;
			int newPageIndex;
			if (_pages[firstDiff] == newSeq[limDiff - 1])
			{
				// Move forward
				movedPage = _pages[firstDiff];
				newPageIndex = limDiff - 1;
			}
			else
			{
				Debug.Assert(_pages[limDiff - 1] == newSeq[firstDiff]); // moved last page forwards
				movedPage = _pages[limDiff - 1];
				newPageIndex = firstDiff;
			}
			var relocatePageInfo = new RelocatePageInfo(movedPage, newPageIndex);
			RelocatePageEvent.Raise(relocatePageInfo);
			if (relocatePageInfo.Cancel)
				UpdateItems(_pages);
			else
			{
				_pages = newSeq;
				UpdatePageNumbers();
				// This is only needed if left and right pages are styled differently.
				// Unfortunately gecko does not re-apply the styles when things are re-ordered!
				UpdateItems(_pages);
			}
		}

		private void UpdatePageNumbers()
		{
			int pageNumber = 0;
			foreach (var page in _pages)
			{
				var node = page.GetDivNodeForThisPage();
				if (node == null)
					continue; // or crash? How can this happen?
				var gridElt = _browser.WebBrowser.Document.GetElementById(GridId(page));
				var titleElt = GetFirstChildWithClass(gridElt, "gridTitle") as GeckoElement;
				string captioni18nId;
				var captionOrPageNumber = page.GetCaptionOrPageNumber(ref pageNumber, out captioni18nId);
				var desiredText = I18NApi.GetTranslationDefaultMayNotBeEnglish(captioni18nId, captionOrPageNumber);
				if (titleElt == null || titleElt.TextContent == desiredText)
					continue;
				titleElt.TextContent = desiredText;
			}
		}

		public void UpdateThumbnailAsync(IPage page)
		{
			if (page.Book.Storage.NormalBaseForRelativepaths != _baseForRelativePaths)
			{
				// book has been renamed! can't go on with old document that pretends to be in the wrong place.
				// Regenerate completely.
				UpdateItems(_pages);
				return;
			}
			if (TroubleShooterDialog.MakeEmptyPageThumbnails)
				return; // no new content needed.
			var targetClass = "bloom-page";
			var gridElt = GetGridElementForPage(page);
			var pageContainerElt = GetFirstChildWithClass(gridElt, PageContainerClass) as GeckoElement;
			if (pageContainerElt == null)
			{
				Debug.Fail("Can't update page...missing pageContainer element");
				return; // for end user we just won't update the thumbnail.
			}
			var pageElt = GetFirstChildWithClass(pageContainerElt, targetClass);
			if (pageElt == null)
			{
				Debug.Fail("Can't update page...missing page element");
				return; // for end user we just won't update the thumbnail.
			}
			// Remove listeners so that garbage collection resulting from the Dispose has a better
			// chance to work (without entanglements between javascript and mozilla's DOM memory).
			RemoveThumbnailListeners();
			var divNodeForThisPage = page.GetDivNodeForThisPage();
			//clone so we can modify it for thumbnailing without messing up the version we will save
			divNodeForThisPage = divNodeForThisPage.CloneNode(true) as XmlElement;
			MarkImageNodesForThumbnail(divNodeForThisPage);
			GeckoNode geckoNode = null;
			for (int i = 0; i < 3; i++)
			{
				// As described in BL-4690, apparently some random event occasionally causes a failure
				// deep inside Gecko when we go to do this.
				// We don't need to bother the user if we just have a problem updating the thumbnail in
				// the page list.
				// So, we log it, and try again a couple of times in case it is a transient problem.
				// If it keeps happening, just leave the old thumbnail in place.
				try
				{
					geckoNode = MakeGeckoNodeFromXmlNode(_browser.WebBrowser.Document, divNodeForThisPage);
					break;
				}
				catch (InvalidComObjectException e)
				{
					Logger.WriteError("BL-4690 InvalidComObjectException try " + i, e);
				}
			}
			if (geckoNode != null)
			{
				pageContainerElt.ReplaceChild(geckoNode, pageElt);
				pageElt.Dispose();
			}
			AddThumbnailListeners();
		}

		private GeckoElement GetGridElementForPage(IPage page)
		{
			return _browser.WebBrowser.Document.GetElementById(GridId(page));
		}

		private GeckoElement GetElementForMenuHolder()
		{
			return _browser.WebBrowser.Document.GetElementById("menuIconHolder");
		}

		private GeckoNode GetFirstChildWithClass(GeckoElement parentElement, string targetClass)
		{
			// Something here can be null when adding pages very quickly, possibly because something
			// is incompletely constructed or in the course of being disposed? So be very careful.
			if (parentElement == null || parentElement.ChildNodes == null)
				return null;
			var targetWithSpaces = " " + targetClass + " "; // search for this to avoid partial word matches
			return parentElement.ChildNodes.FirstOrDefault(e =>
			{
				var ge = e as GeckoElement;
				if (ge == null)
					return false;
				var attr = ge.Attributes["class"];
				if (attr == null)
					return false;
				var content = " " + attr.TextContent + " "; // wrapping spaces allow us to find targetWithSpaces at start or end
				if (content == null)
					return false;
				return content.Contains(targetWithSpaces);
			});
		}

		private Gecko.GeckoNode MakeGeckoNodeFromXmlNode(Gecko.GeckoDocument doc, XmlNode xmlElement)
		{
			var result = doc.CreateElement(xmlElement.LocalName);
			foreach (XmlAttribute attr in xmlElement.Attributes)
				result.SetAttribute(attr.LocalName, attr.Value);
			foreach (var child in xmlElement.ChildNodes)
			{
				if (child is XmlElement)
					result.AppendChild(MakeGeckoNodeFromXmlNode(doc, (XmlElement)child));
				else if (child is XmlText)
					result.AppendChild(doc.CreateTextNode(((XmlText) child).InnerText));
				else
				{
					result = result;
				}
			}
			return result;
		}

		public ControlKeyEvent ControlKeyEvent
		{
			set
			{
				if (_browser != null)
					_browser.ControlKeyEvent = value;
			}
		}
	}
}
