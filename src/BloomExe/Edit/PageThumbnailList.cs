using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using Bloom.Api;
using Bloom.Book;
using Bloom.Utils;
using Bloom.web;
using L10NSharp;
using SIL.IO;

namespace Bloom.Edit
{
    /// <summary>
    /// Handle a list of page thumbnails (the left column in Edit mode) using an iframe configured by
    /// pageThumbnailList.pug to load the React component specified in pageThumbnailList.tsx.
    /// The code here is tightly coupled to the code in pageThumbnailList.tsx and its dependencies,
    /// and also to the code in PageListApi which supports various callbacks to C# from the JS.
    /// </summary>
    public class PageThumbnailList
    {
        public HtmlThumbNailer Thumbnailer;
        public event EventHandler PageSelectedChanged;

        internal EditingModel Model;
        private static string _thumbnailInterval;
        private string _baseForRelativePaths;

        // Store this so we don't have to reload the thing from disk everytime we refresh the screen.
        private readonly string _baseHtml;
        private List<IPage> _pages;

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

        public PageThumbnailList()
        {
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
            var useViteDev = ReactControl.ShouldUseViteDev();
            var frame = BloomFileLocator.GetBrowserFile(
                false,
                "bookEdit",
                "pageThumbnailList",
                useViteDev ? "pageThumbnailList.vite-dev.html" : "pageThumbnailList.html"
            );
            var backColor = MiscUtils.ColorToHtmlCode(Palette.SidePanelBackgroundColor);
            _baseHtml = RobustFile.ReadAllText(frame, Encoding.UTF8).Replace("DarkGray", backColor);
        }

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

        public void UpdateAllThumbnails()
        {
            UpdateItems(_pages);
        }

        private List<IPage> UpdateItems(IEnumerable<IPage> pages)
        {
            return UpdateItemsInternal(pages);
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

            if (result.FirstOrDefault(p => p.Book != null) == null)
            {
                return new List<IPage>();
            }
            var sizeClass =
                result.Count > 1
                    ? Book
                        .Layout.FromPage(result[1].GetDivNodeForThisPage(), Book.Layout.A5Portrait)
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

            pageListDom = Model.CurrentBook.GetHtmlDomForPageList(pageListDom);

            _baseForRelativePaths = pageListDom.BaseForRelativePaths;
            return result.ToList();
        }

        internal void PageClicked(IPage page)
        {
            if (Enabled)
                InvokePageSelectedChanged(page);
        }

        /// <summary>
        /// Check whether or not the page thumbnail context menu item is enabled for the
        /// given page.
        /// </summary>
        /// <remarks>
        /// The list of commandId values is found in pageThumbnailList.tsx in the
        /// pageMenuDefinition array.
        /// </remarks>
        internal bool IsContextMenuCommandEnabled(IPage page, string commandId)
        {
            if (!Enabled || page == null)
                return false;

            switch (commandId)
            {
                case "duplicatePage":
                case "duplicatePageManyTimes":
                    return Model.CanDuplicatePage;
                case "copyPage":
                    return Model.CanCopyPage;
                case "pastePage":
                    return Model.CanAddPages && Model.GetClipboardHasPage();
                case "removePage":
                    return Model.CanDeletePage;
                case "chooseDifferentLayout":
                    return !page.Required
                        && !page.GetDivNodeForThisPage()
                            .GetAttribute("data-tool-id")
                            .Equals("game");
                default:
                    return false;
            }
        }

        internal void ExecuteContextMenuCommand(IPage page, string commandId)
        {
            if (!IsContextMenuCommandEnabled(page, commandId))
                return;

            switch (commandId)
            {
                case "duplicatePage":
                    Model.DuplicatePage(page);
                    break;
                case "duplicatePageManyTimes":
                    Model.DuplicateManyPages(page);
                    break;
                case "copyPage":
                    Model.CopyPage(page);
                    break;
                case "pastePage":
                    Model.PastePage(page);
                    break;
                case "removePage":
                    if (ConfirmRemovePageDialog.Confirm())
                        Model.DeletePage(page);
                    break;
                case "chooseDifferentLayout":
                    Model.GetEditingBrowser().Focus();
                    Model.ChangePageLayout(page);
                    break;
            }
        }

        private PageListApi _pageListApi;
        internal bool Enabled = true;

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
    }
}
