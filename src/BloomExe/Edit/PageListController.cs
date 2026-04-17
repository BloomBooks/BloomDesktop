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
