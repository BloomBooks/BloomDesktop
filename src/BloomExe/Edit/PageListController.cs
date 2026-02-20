using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Bloom.Api;
using Bloom.Book;
using Bloom.web;
using L10NSharp;
using SIL.Reporting;
using SIL.Windows.Forms.Reporting;

namespace Bloom.Edit
{
    public class PageListController
    {
        private readonly EditingModel _model;
        private bool _dontForwardSelectionEvent;
        private IPage _pageWeThinkShouldBeSelected;
        private PageThumbnailList _thumbNailList;

        public PageListController(
            RelocatePageEvent relocatePageEvent,
            EditingModel model,
            HtmlThumbNailer thumbnailProvider,
            PageListApi pageListApi,
            BloomWebSocketServer webSocketServer
        )
        {
            _model = model;
            _thumbNailList = new PageThumbnailList();
            _thumbNailList.PageListApi = pageListApi;
            _thumbNailList.WebSocketServer = webSocketServer;

            _thumbNailList.Thumbnailer = thumbnailProvider;
            _thumbNailList.RelocatePageEvent = relocatePageEvent;
            _thumbNailList.PageSelectedChanged += new EventHandler(OnPageSelectedChanged);
            _thumbNailList.Model = model;
            // First action determines whether the menu item is enabled, second performs it.
            var menuItems = new List<PageThumbnailList.MenuItemSpec>();
            menuItems.Add(
                new PageThumbnailList.MenuItemSpec()
                {
                    Id = "duplicatePage",
                    Label = LocalizationManager.GetString(
                        "EditTab.DuplicatePageButton",
                        "Duplicate Page"
                    ),
                    EnableFunction = (page) => page != null && _model.CanDuplicatePage,
                    ExecuteCommand = (page) => _model.DuplicatePage(page),
                }
            );
            menuItems.Add(
                new PageThumbnailList.MenuItemSpec()
                {
                    Id = "duplicatePageManyTimes",
                    Label = LocalizationManager.GetString(
                        "EditTab.DuplicatePageMultiple",
                        "Duplicate Page Many Times..."
                    ),
                    EnableFunction = (page) => page != null && _model.CanDuplicatePage,
                    ExecuteCommand = (page) => _model.DuplicateManyPages(page),
                }
            );
            menuItems.Add(
                new PageThumbnailList.MenuItemSpec()
                {
                    Id = "copyPage",
                    Label = LocalizationManager.GetString("EditTab.CopyPage", "Copy Page"),
                    EnableFunction = (page) => page != null && _model.CanCopyPage,
                    ExecuteCommand = (page) => _model.CopyPage(page),
                }
            );
            menuItems.Add(
                new PageThumbnailList.MenuItemSpec()
                {
                    Id = "pastePage",
                    Label = LocalizationManager.GetString("EditTab.PastePage", "Paste Page"),
                    EnableFunction = (page) =>
                        page != null && _model.CanAddPages && _model.GetClipboardHasPage(),
                    ExecuteCommand = (page) => _model.PastePage(page),
                }
            );
            menuItems.Add(
                new PageThumbnailList.MenuItemSpec()
                {
                    Id = "removePage",
                    Label = LocalizationManager.GetString(
                        "EditTab.DeletePageButton",
                        "Remove Page"
                    ),
                    EnableFunction = (page) => page != null && _model.CanDeletePage,
                    ExecuteCommand = (page) =>
                    {
                        if (ConfirmRemovePageDialog.Confirm())
                            _model.DeletePage(page);
                    },
                }
            );
            menuItems.Add(
                new PageThumbnailList.MenuItemSpec()
                {
                    Id = "chooseDifferentLayout",
                    Label = LocalizationManager.GetString(
                        "EditTab.ChooseLayoutButton",
                        "Choose Different Layout"
                    ),
                    // We don't want to allow layout changes for game pages (except Widget pages).
                    // Note: we also have to disable the Change Layout switch, in origami.ts
                    EnableFunction = (page) =>
                        page != null
                        && !page.Required
                        && !page.GetDivNodeForThisPage()
                            .GetAttribute("data-tool-id")
                            .Equals("game"),
                    ExecuteCommand = (page) =>
                    {
                        // While we have separate browsers running for this page list and the editing view, we switch
                        // the focus to the editing browser before launching the dialog so that Esc will work to close
                        // the dialog without interacting with the dialog first.
                        _model.GetEditingBrowser().Focus();
                        _model.ChangePageLayout(page);
                    },
                }
            );
            // This sets up the context menu items that will be shown when the user clicks the
            // arrow in the thumbnail list or right-clicks in the page list.
            // Note that we can't use ContextMenuProvider here, because there is no reasonable
            // way to know which page was right-clicked, if any. So we handle right-click in JS.
            _thumbNailList.ContextMenuItems = menuItems;
        }

        private void OnPageSelectedChanged(object page, EventArgs e)
        {
            if (page == null)
                return;
            if (!_dontForwardSelectionEvent)
            {
                // The only necessary action after saving is to navigate to the desired page.
                // This is achieved by returning the right ID in the trivial doAfterSaving function
                // passed as the first argument to SaveThen.
                _model.SaveThen(() => (page as Page).Id, () => { });
            }
        }

        public void SetBook(Book.Book book) //review: could do this instead by giving this class the bookselection object
        {
            if (book == null)
            {
                _thumbNailList.SetItems(new Page[] { });
            }
            else
            {
                _thumbNailList.SetItems(
                    new IPage[] { new PlaceHolderPage() }.Concat(book.GetPages())
                );

                if (_pageWeThinkShouldBeSelected != null)
                {
                    //this var will be set previously when someone told us the page we're to select,
                    //but had not yet given us leave to do the time-consuming process of actually
                    //making the thumbnails and showing them.
                    SelectThumbnailWithoutSendingEvent(_pageWeThinkShouldBeSelected);
                }
            }
        }

        public void UpdateThumbnailAsync(IPage page)
        {
            Logger.WriteMinorEvent("Updating thumbnail for page");
            _thumbNailList.UpdateThumbnailAsync(page);
        }

        public void UpdateAllThumbnails()
        {
            _thumbNailList.UpdateAllThumbnails();
        }

        public void Clear()
        {
            _thumbNailList.SetItems(new IPage[] { });
        }

        public void SelectThumbnailWithoutSendingEvent(IPage page)
        {
            _pageWeThinkShouldBeSelected = page;
            try
            {
                _dontForwardSelectionEvent = true;
                _thumbNailList.SelectPage(page);
            }
            finally
            {
                _dontForwardSelectionEvent = false;
            }
        }

        public void EmptyThumbnailCache()
        {
            _thumbNailList.EmptyThumbnailCache();
        }

        public bool Enabled
        {
            set { _thumbNailList.Enabled = value; }
        }
    }
}
