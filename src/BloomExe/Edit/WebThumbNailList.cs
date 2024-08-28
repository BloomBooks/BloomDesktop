using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.Design;
using System.Diagnostics;
using System.Drawing;
using System.Text;
using System.Windows.Forms;
using System.Linq;
using Bloom.Book;
using Bloom.Api;
using Bloom.MiscUI;
using Bloom.Utils;
using Bloom.web;
using L10NSharp;
using SIL.IO;

namespace Bloom.Edit
{
    /// <summary>
    /// Displays a list page thumbnails (the left column in Edit mode) using a separate Browser configured by
    /// pageThumbnailList.pug to load the React component specified in pageThumbnailList.tsx.
    /// The code here is tightly coupled to the code in pageThumbnailList.tsx and its dependencies,
    /// and also to the code in PageListApi which supports various callbacks to C# from the JS.
    /// Todo: rename this PageThumbnailList (but in another PR, with no other changes).
    /// </summary>
    public partial class WebThumbNailList : UserControl
    {
        public HtmlThumbNailer Thumbnailer;
        public event EventHandler PageSelectedChanged;
        private Browser _browser;
        internal EditingModel Model;
        private static string _thumbnailInterval;
        private string _baseForRelativePaths;

        // Store this so we don't have to reload the thing from disk everytime we refresh the screen.
        private readonly string _baseHtml;
        private List<IPage> _pages;
        private bool _usingTwoColumns;

        /// <summary>
        /// The CSS class we give the main div for each page; the same element always has an id attr which identifies the page.
        /// </summary>
        private const string GridItemClass = "gridItem";
        private const string PageContainerClass = "pageContainer";

        // intended to be private except for initialization by PageListView
        internal PageListApi PageListApi
        {
            get => _pageListApi;
            set
            {
                _pageListApi = value;
                _pageListApi.PageList = this;
            }
        }

        internal BloomWebSocketServer WebSocketServer { get; set; }

        internal class MenuItemSpec
        {
            public string Label;
            public Func<IPage, bool> EnableFunction; // called to determine whether the item should be enabled.
            public Action<IPage> ExecuteCommand; // called when the item is chosen to perform the action.
        }

        // A list of menu items that should be in both the web browser'movedPageIdAndNewIndex right-click menu and
        // the one we show ourselves when the arrow is clicked.
        internal List<MenuItemSpec> ContextMenuItems { get; set; }

        public WebThumbNailList()
        {
            InitializeComponent();

            if (!ReallyDesignMode)
            {
                _browser = BrowserMaker.MakeBrowser();
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
                var physicalMemGb =
                    Convert.ToDecimal(memInfo.TotalPhysicalMemory) / 1024 / 1024 / 1024;

                if (physicalMemGb < 2.5M) // less than 2.5 GB physical RAM
                {
                    _thumbnailInterval = "400";
                }
                else if (physicalMemGb < 4M) // less than 4 GB physical RAM
                {
                    _thumbnailInterval = "200";
                }
                else // 4 GB or more physical RAM
                {
                    _thumbnailInterval = "100";
                }
            }
            var frame = BloomFileLocator.GetBrowserFile(
                false,
                "bookEdit",
                "pageThumbnailList",
                "pageThumbnailList.html"
            );
            var backColor = MiscUtils.ColorToHtmlCode(BackColor);
            _baseHtml = RobustFile.ReadAllText(frame, Encoding.UTF8).Replace("DarkGray", backColor);
        }

        protected bool ReallyDesignMode =>
            (base.DesignMode || GetService(typeof(IDesignerHost)) != null)
            || (LicenseManager.UsageMode == LicenseUsageMode.Designtime);

        private void InvokePageSelectedChanged(IPage page)
        {
            EventHandler handler = PageSelectedChanged;
            if (
                handler != null
                && /*REVIEW */
                page != null
            )
            {
                handler(page, null);
            }
        }

        public RelocatePageEvent RelocatePageEvent { get; set; }

        public void EmptyThumbnailCache()
        {
            // Prevents UpdateItemsInternal() from being able to enter into the early abort (optimization) condition.
            // Forces a full rebuild instead.
            _pages = null;
        }

        public void SelectPage(IPage page)
        {
            if (InvokeRequired)
                Invoke((Action)(() => SelectPageInternal(page)));
            else
                SelectPageInternal(page);
        }

        private void SelectPageInternal(IPage page)
        {
            if (PageListApi != null && PageListApi.SelectedPage != page)
            {
                PageListApi.SelectedPage = page;
                WebSocketServer.SendString("pageThumbnailList", "selecting", page.Id);
            }
        }

        public void SetItems(IEnumerable<IPage> pages)
        {
            _pages = UpdateItems(pages);
        }

        private bool RoomForTwoColumns => Width > 199;

        protected override void OnLoad(EventArgs e)
        {
            var settings = (_browser as WebView2Browser)?.InternalBrowser?.CoreWebView2?.Settings;
            // If we switch to a single Browser instance, this setting would lose the CTRL-plus and
            // CTRL-minus zooming of the book page in the Edit pane if applied to the single Browser.
            // The CTRL-mousewheel and clicking on the + and - icons apparently would still work.
            if (settings != null)
                settings.IsZoomControlEnabled = false;
            base.OnLoad(e);
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            if (_pages != null && _pages.Count > 0 && RoomForTwoColumns != _usingTwoColumns)
                UpdateItems(_pages);
        }

        private void OnPaneContentsChanging(bool hasPages)
        {
            // We try to prevent some spurious javascript errors by shutting down the websocket listener before
            // navigating to the new root page. This may not be entirely reliable as there is a race
            // condition between the navigation request and the reception of the stopListening message.
            WebSocketServer.SendString("pageThumbnailList", "stopListening", "");
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
                Invoke((Action)(() => result = UpdateItemsInternal(pages)));
            }
            else
            {
                result = UpdateItemsInternal(pages);
            }

            return result;
        }

        private List<IPage> UpdateItemsInternal(IEnumerable<IPage> pages)
        {
            var result = pages.ToList();
            // When it'movedPageIdAndNewIndex safe (we're not changing books etc), just send pageListNeedsRefresh to the web socket
            // and return result. (We need to skip(1) for the placeholder to get a meaningful book comparison)
            if (
                _pages != null
                && _pages.Skip(1).FirstOrDefault()?.Book == pages.Skip(1).FirstOrDefault()?.Book
            )
            {
                WebSocketServer.SendString("pageThumbnailList", "pageListNeedsRefresh", "");
                return result;
            }
            OnPaneContentsChanging(result.Any());

            if (result.FirstOrDefault(p => p.Book != null) == null || !_browser.IsReadyToNavigate)
            {
                // If we're not ready to navigate (old code: don't hae a GeckWebBrowser yet),
                // Navigate won't do anything. But some of the code below would have problems (BL-9167).
                //  But if we haven't already been initialized enough to have a browser, then we don't
                // have a thumbnail display to update anyway.
                _browser.Navigate(@"about:blank", false); // no pages, we just want a blank screen, if anything.
                return new List<IPage>();
            }
            _usingTwoColumns = RoomForTwoColumns;
            var sizeClass =
                result.Count > 1
                    ? Book.Layout
                        .FromPage(result[1].GetDivNodeForThisPage(), Book.Layout.A5Portrait)
                        .SizeAndOrientation.ClassName
                    : "A5Portrait";

            // Somehow, the React code needs to know the page size, mainly so it can put the right class on
            // the pageContainer element in pageThumbnail.tsx.
            // - It could get it by parsing the HTML page content, but that'movedPageIdAndNewIndex clumsy and also really too late:
            //   the pages are drawn empty before the page content is ever retrieved.
            // - we can't use the class on the page element because it is inside the pageContainer we need to affect
            // - we could put a sizeClass on the body or some other higher-level element, and rewrite the CSS
            //   rules to look for pageContainer INSIDE a certain page class. But this seems risky.
            //   Our expectation is that this class is applied to a page-level element. We don't want to
            //   accidentally invoke some rule that makes the whole preview pane A5Portrait-shaped.
            //   It also violates all our expectations, and forces us to do counter-intuitive things
            //   like making pageContainer a certain size if it is 'inside' something that is A5Portrait.
            // So, I ended up putting a data-pageSize attribute on the body element, and having the
            // code that initializes React look for it and pass pageSize to the root React element
            // as it should be, a property.
            var htmlText = _baseHtml.Replace(
                "data-pageSize=\"A5Portrait\"",
                $"data-pageSize=\"{sizeClass}\""
            );

            // We will end up navigating to pageListDom
            var pageListDom = new HtmlDom(XmlHtmlConverter.GetXmlDomFromHtml(htmlText));

            if (SIL.PlatformUtilities.Platform.IsLinux)
                OptimizeForLinux(pageListDom);

            pageListDom = Model.CurrentBook.GetHtmlDomForPageList(pageListDom);
            _browser.DocumentCompleted += WebBrowser_DocumentCompleted;

            _baseForRelativePaths = pageListDom.BaseForRelativePaths;
            if (this.IsHandleCreated) // somehow we can get here when the edit view is not active at all
                Invoke(
                    (Action)(
                        () =>
                            _browser.Navigate(pageListDom, source: InMemoryHtmlFileSource.Pagelist)
                    )
                );
            return result.ToList();
        }

        private static void OptimizeForLinux(HtmlDom pageListDom)
        {
            // BL-987: Add styles to optimize performance on Linux
            var style = pageListDom.RawDom.CreateElement("style");
            style.InnerXml =
                "img { image-rendering: optimizeSpeed; image-rendering: -moz-crisp-edges; image-rendering: crisp-edges; }";
            pageListDom.RawDom.GetElementsByTagName("head")[0].AppendChild(style);
        }

        void WebBrowser_DocumentCompleted(object sender, EventArgs e)
        {
            SelectPage(PageListApi?.SelectedPage);
            _browser.DocumentCompleted -= WebBrowser_DocumentCompleted; // need to do this only once
        }

        internal void PageClicked(IPage page)
        {
            InvokePageSelectedChanged(page);
        }

        internal void MenuClicked(IPage page)
        {
            var menu = new ContextMenuStrip();
            if (page == null)
                return;
            foreach (var item in ContextMenuItems)
            {
                var useItem = item; // for use in Click action (reference to loop variable has unpredictable results)
                var menuItem = new ToolStripMenuItem(item.Label);
                menuItem.Click += (sender, args) => useItem.ExecuteCommand(page);
                menuItem.Enabled = item.EnableFunction(page);
                menu.Items.Add(menuItem);
            }

            _browser.OnBrowserClick += Browser_Click;
            Model.GetEditingBrowser().OnBrowserClick += Browser_Click;
            _popupPageMenu = menu;

            menu.Show(MousePosition);
        }

        ContextMenuStrip _popupPageMenu;
        private PageListApi _pageListApi;

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

        // This gets invoked by Javascript (via the PageListApi) when it determines that a particular page has been moved.
        // newIndex is the (zero-based) index that the page is moving to
        // in the whole list of pages, including the placeholder.
        internal void PageMoved(IPage movedPage, int newPageIndex)
        {
            // accounts for placeholder.
            // Enhance: may not be needed in single-column mode, if we ever restore that.
            newPageIndex--;
            if (!movedPage.CanRelocate || !_pages[newPageIndex + 1].CanRelocate)
            {
                var msg = LocalizationManager.GetString(
                    "EditTab.PageList.CantMoveXMatter",
                    "That change is not allowed. Front matter and back matter pages must remain where they are."
                );
                //previously had a caption that didn't add value, just more translation work
                MessageBox.Show(msg);
                WebSocketServer.SendString("pageThumbnailList", "pageListNeedsReset", "");
                return;
            }
            Model.SaveThen(
                () =>
                {
                    var relocatePageInfo = new RelocatePageInfo(movedPage, newPageIndex);
                    RelocatePageEvent.Raise(relocatePageInfo);
                    UpdateItems(movedPage.Book.GetPages());
                    PageSelectedChanged(movedPage, new EventArgs());
                    return movedPage.Id;
                },
                () => { }, // wrong state, do nothing
                forceFullSave: true
            );
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
            WebSocketServer.SendString("pageThumbnailList", "pageNeedsRefresh", page.Id);
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
