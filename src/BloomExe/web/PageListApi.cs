using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Dynamic;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using System.Xml;
using Bloom.Api;
using Bloom.Book;
using Bloom.CollectionTab;
using Bloom.Edit;
using Bloom.SafeXml;
using Bloom.Utils;
using SIL.IO;
using SIL.Xml;

namespace Bloom.web
{
    /// <summary>
    /// Handles Api requests for the Page thumbnail list in the left panel when editing a book.
    /// </summary>
    public class PageListApi
    {
        private readonly BookSelection _bookSelection;
        private readonly CollectionModel _collectionModel;

        internal WebThumbNailList PageList { get; set; }

        // internal for use only by WebThumbnailList
        internal IPage SelectedPage { get; set; }

        // Called by autofac, which creates the one instance and registers it with the server.
        public PageListApi(BookSelection _bookSelection, CollectionModel collectionModel)
        {
            this._bookSelection = _bookSelection;
            _collectionModel = collectionModel;
        }

        public void RegisterWithApiHandler(BloomApiHandler apiHandler)
        {
            apiHandler
                .RegisterEndpointHandler("pageList/pages", HandlePagesRequest, false)
                .Measureable();
            apiHandler.RegisterEndpointHandler(
                "pageList/pageContent",
                HandlePageContentRequest,
                false
            );
            apiHandler
                .RegisterEndpointHandler("pageList/pageMoved", HandlePageMovedRequest, true)
                .Measureable();
            apiHandler.RegisterEndpointHandler(
                "pageList/pageClicked",
                HandlePageClickedRequest,
                true
            );
            apiHandler
                .RegisterEndpointHandler("pageList/menuClicked", HandleShowMenuRequest, true)
                .Measureable();

            apiHandler.RegisterEndpointHandler(
                "pageList/bookAttributesThatMayAffectDisplay",
                (request) =>
                {
                    HtmlDom dom = null;
                    var requestedBookId = request.GetParamOrNull("book-id");
                    try
                    {
                        var book = string.IsNullOrEmpty(requestedBookId)
                            ? _bookSelection.CurrentSelection
                            : _collectionModel.GetBookFromId(requestedBookId);
                        dom = book?.OurHtmlDom;
                    }
                    catch (Exception ex)
                    {
                        request.Failed(ex.Message);
                        return;
                    }

                    var attrs = dom?.GetBodyAttributesThatMayAffectDisplay();
                    if (attrs == null)
                    {
                        request.ReplyWithJson("{}");
                        return;
                    }
                    // Surely there's a way to do this more safely with JSON.net but I haven't found it yet
                    var props = string.Join(
                        ",",
                        attrs.Select(a =>
                            ("\"" + a.Name + "\": \"" + a.Value.Replace("\"", "\\\"") + "\"")
                        )
                    );
                    request.ReplyWithJson("{" + props + "}");
                },
                true
            );
        }

        private void HandlePageClickedRequest(ApiRequest request)
        {
            var requestData = DynamicJson.Parse(request.RequiredPostJson());
            string pageId = requestData.pageId;

            var shiftIsDown = (Control.ModifierKeys & Keys.Shift) == Keys.Shift;
            var label = shiftIsDown ? "Select Page (SHIFT)" : "Select Page";

            using (PerformanceMeasurement.Global?.Measure(label, requestData.detail ?? ""))
            {
                //using (PerformanceMeasurement.Global.Measure(label, requestData.detail ?? ""))
                //{
                IPage page = PageFromId(pageId);
                //}

                if (page != null)
                    PageList.PageClicked(page);
            }

            request.PostSucceeded();
        }

        // User clicked on the down arrow, we respond by showing the same menu as for right click.
        private void HandleShowMenuRequest(ApiRequest request)
        {
            var requestData = DynamicJson.Parse(request.RequiredPostJson());
            string pageId = requestData.pageId;
            IPage page = PageFromId(pageId);
            if (page != null)
                PageList.MenuClicked(page);
            request.PostSucceeded();
        }

        private void HandlePageMovedRequest(ApiRequest request)
        {
            var requestData = DynamicJson.Parse(request.RequiredPostJson());
            string newPageId = requestData.movedPageId;
            IPage movedPage = PageFromId(newPageId);
            int newIndex = Convert.ToInt32(requestData.newIndex); // Should come as int, but automatic JSON parsing doesn't know this
            PageList.PageMoved(movedPage, newIndex);
            request.PostSucceeded();
        }

        private Bloom.Book.Book CurrentBook => _bookSelection.CurrentSelection;

        // The book from which we most recently computed _currentBookPages. Updates should be locked to _currentBookPages.
        private Book.Book _pagesBook;

        // map from page ID to page. Lock access, as it is used by server threads.
        private readonly Dictionary<string, IPage> _currentBookPages =
            new Dictionary<string, IPage>();

        // Gets the object which we will jsonify and pass as the representation of the page
        // as part of the page list.
        dynamic GetPageObject(IPage page, ref int pageNumber)
        {
            dynamic result = new ExpandoObject();
            string captionI18nId;
            var caption = page.GetCaptionOrPageNumber(ref pageNumber, out captionI18nId);
            if (!string.IsNullOrEmpty(caption))
                caption = I18NApi.GetTranslationDefaultMayNotBeEnglish(captionI18nId, caption);
            result.caption = caption;

            // We'd like to answer XmlHtmlConverter.ConvertElementToHtml5(page.GetDivNodeForThisPage());
            // But it's too slow...as much as 80ms/page for picture dictionary pages
            // on a good desktop. A possible enhancement at some point is to keep track of
            // that result in a page variable, erase it if the page changes, and return it
            // at once if we have it. Instead, the pageThumbnail.tsx code is set up to
            // request individual page content for the visible pages and fill them in gradually
            // without slowing down other behavior.
            result.content = "";
            result.key = page.Id;
            result.isXMatter = page.IsXMatter;
            return result;
        }

        internal void ClearPagesCache()
        {
            _pagesBook = null;
        }

        // intended to be private except for WebThumnailList
        internal IPage PageFromId(string id)
        {
            lock (_currentBookPages)
            {
                if (_pagesBook != CurrentBook)
                {
                    _currentBookPages.Clear();
                    _pagesBook = CurrentBook;
                    foreach (var page in _pagesBook.GetPages())
                    {
                        _currentBookPages[page.Id] = page;
                    }
                }

                IPage result;
                _currentBookPages.TryGetValue(id, out result);
                return result;
            }
        }

        /// <summary>
        /// Handles a request to the server for the current page list. Skeleton objects
        /// without actual page content are returned for performance reasons, and later,
        /// the React code requests individual page contents.
        /// </summary>
        ///  request parameters:
        ///   book-id - optional book id. If not given, uses the collections "current" book.
        public void HandlePagesRequest(ApiRequest request)
        {
            //var watch = new Stopwatch();
            //watch.Start();

            Book.Book book;
            string requestedBookId = request.GetParamOrNull("book-id");
            try
            {
                book = string.IsNullOrEmpty(requestedBookId)
                    ? _bookSelection.CurrentSelection
                    : _collectionModel.GetBookFromId(requestedBookId);
            }
            catch (Exception ex)
            {
                request.Failed(ex.Message);
                return;
            }

            IPage[] pages = book == null ? new IPage[0] : book.GetPages().ToArray();
            int pageNumber = 0;
            dynamic answer = new ExpandoObject();
            answer.pages = pages.Select(p => GetPageObject(p, ref pageNumber)).ToArray();
            answer.selectedPageId =
                string.IsNullOrEmpty(requestedBookId) && SelectedPage != null
                    ? SelectedPage.Id
                    : "";
            answer.pageLayout = book?.GetLayout()?.SizeAndOrientation?.ClassName ?? "A5Portrait";
            answer.cssFiles =
                book != null ? book.Storage.GetCssFilesToLinkForPreview() : new string[0];
            request.ReplyWithJson(answer);
            //watch.Stop();
            //Debug.WriteLine($"Generating JSON for thumbnails took {watch.ElapsedMilliseconds}ms");
        }

        // Requests the content that should be displayed in a single page thumbnail.
        // Parameters:
        //    id - the page ID
        //    book-id - the book ID (optional)
        public void HandlePageContentRequest(ApiRequest request)
        {
            var watch = new Stopwatch();
            watch.Start();
            var id = request.RequiredParam("id");
            var bookId = request.GetParamOrNull("book-id");
            IPage page = null;
            try
            {
                var book = string.IsNullOrEmpty(bookId)
                    ? _bookSelection.CurrentSelection
                    : _collectionModel.GetBookFromId(bookId);
                page = book.GetPages().FirstOrDefault(p => p.Id == id);
            }
            catch (Exception)
            {
                // Theoretically, the user could have deleted the book or a page and the UI is
                // still sending out requests for a deleted thing.
                // If that happens, let's have the request fail. One could argue we should
                // send something back so that the client doesn't have to handle failure,
                // but how are we to know what the client wants in that case?
                request.Failed("Could not find book or page");
                return;
            }
            dynamic answer = new ExpandoObject();
            answer.content = GetPageContentForThumbnail(page);
            request.ReplyWithJson(answer);
            watch.Stop();
        }

        private static string GetPageContentForThumbnail(IPage page)
        {
            if (page == null)
            {
                return string.Empty;
            }

            var pageElement = page.GetDivNodeForThisPage().CloneNode(true) as SafeXmlElement;
            var videos = pageElement.SafeSelectNodes(".//video").Cast<SafeXmlElement>().ToArray();
            foreach (var video in videos)
            {
                video.ParentNode.RemoveChild(video);
            }

            MarkImageNodesForThumbnail(pageElement);
            // For WebView2, this prevents any interaction with elements in the page thumbnail.
            // We put an overlay over it to try to prevent such interaction, but this is more
            // reliable. Nothing in the page will ever get focus, be tabbed to, be read by
            // screen readers, etc. In particular, we finally concluded that BL-11528 was caused
            // by the browser temporarily focusing, and then scrolling into view, something
            // on the first page; this prevents that.
            pageElement.SetAttribute("inert", "true");
            return XmlHtmlConverter.ConvertElementToHtml5(pageElement);
        }

        // Note: Int parsing helper removed as unused.

        // As a further form of optimization, mark img elements as being thumbnails. The server
        // produces miniatures that take up less memory.
        private static void MarkImageNodesForThumbnail(SafeXmlElement pageElementForThumbnail)
        {
            var imgNodes = HtmlDom.SelectChildImgAndBackgroundImageElements(
                pageElementForThumbnail
            );
            if (imgNodes != null)
            {
                foreach (SafeXmlElement imgNode in imgNodes)
                {
                    //We can't handle doing anything special with these /api/branding/ images yet, they get mangled.
                    var imageElementUrl = HtmlDom.GetImageElementUrl(imgNode);
                    if (imageElementUrl.NotEncoded.Contains("/api/"))
                        continue;

                    var filename = imageElementUrl.PathOnly.UrlEncoded;
                    if (!string.IsNullOrWhiteSpace(filename))
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
                        HtmlDom.SetImageElementUrl(
                            imgNode,
                            UrlPathString.CreateFromUnencodedString(url)
                        );
                    }
                }
            }
        }
    }
}
