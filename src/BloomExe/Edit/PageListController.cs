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

            // When the click brought the outgoing page's content with it, save it and go, in one
            // step. Nothing has to be asked of the browser, so we never enter SavePending -- the
            // state in which a further page click would be silently discarded.
            var contentFromBrowser = (e as PageSelectedChangedEventArgs)?.PageContentFromBrowser;
            if (
                contentFromBrowser != null
                && _model.SavePageInPlaceThenGoToPage(contentFromBrowser, pageId)
            )
                return;

            // No content came with the click (the page list could not collect it), or we were not
            // in a state where we could use it. Ask the browser for the content and let the state
            // machine navigate once it arrives. The only necessary action after saving is to go to
            // the desired page, which is what returning its ID from the first argument achieves.
            _model.SaveThen(() => pageId, () => { });
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
