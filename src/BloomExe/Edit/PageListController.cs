using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Bloom.Api;
using Bloom.Book;
using Bloom.web;
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
        }

        private void OnPageSelectedChanged(object page, EventArgs e)
        {
            if (page == null)
                return;
            if (_dontForwardSelectionEvent)
                return;

            var pageId = (page as Page).Id;

            // The only necessary action after saving is to go to the desired page, which is what
            // returning its ID from the first argument achieves.
            //
            // The click usually brings the outgoing page's content with it, which is the freshest
            // copy there is; when it does not, MergeCurrentPageThenSave uses the snapshot the
            // browser last volunteered. Either way the save and the move happen in one step, with
            // nothing to wait for in between -- which is what stopped a second page click being
            // silently discarded.
            _model.MergeCurrentPageThenSave(
                () => pageId,
                // Clicking a thumbnail changes nothing in the book: the action just names the page
                // to go to. This is the case the whole "do not write a page nobody edited"
                // optimisation exists for, so it must not claim the book changed.
                actionChangesTheBook: false,
                pageContentFromBrowser: (e as PageSelectedChangedEventArgs)?.PageContentFromBrowser
            );
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
