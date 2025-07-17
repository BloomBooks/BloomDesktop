using System.Web;
using Bloom.Api;
using Bloom.Book;
using Bloom.Edit;
using Microsoft.CSharp.RuntimeBinder;

namespace Bloom.web.controllers
{
    /// <summary>
    /// This API handles requests to add new pages and change existing pages to match some other layout.
    /// </summary>
    public class AddOrChangePageApi
    {
        private readonly TemplateInsertionCommand _templateInsertionCommand;
        private readonly PageSelection _pageSelection;
        private readonly ITemplateFinder _sourceCollectionsList;
        private readonly EditingModel _editingModel;

        public AddOrChangePageApi(
            TemplateInsertionCommand templateInsertionCommand,
            PageSelection pageSelection,
            ITemplateFinder sourceCollectionsList,
            EditingModel editingModel
        )
        {
            _templateInsertionCommand = templateInsertionCommand;
            _pageSelection = pageSelection;
            _sourceCollectionsList = sourceCollectionsList;
            _editingModel = editingModel;
        }

        public void RegisterWithApiHandler(BloomApiHandler apiHandler)
        // See also PageTemplateApi.
        {
            // Both of these display UI, expect to require UI thread.
            apiHandler
                .RegisterEndpointHandler("addPage", HandleAddPage, true)
                .Measureable("Add Page");
            apiHandler
                .RegisterEndpointHandler("changeLayout", HandleChangeLayout, true)
                .Measureable("Change Layout");
            ;
        }

        private void HandleAddPage(ApiRequest request)
        {
            (IPage templatePage, bool dummy, int numberOfPagesToAdd, bool _) =
                GetPageTemplateAndUserStyles(request);
            if (templatePage == null || numberOfPagesToAdd < 1) // just in case
                return;
            CopyVideoPlaceHolderIfNeeded(templatePage);
            _templateInsertionCommand.Insert(templatePage as Page, numberOfPagesToAdd);

            // Don't understand what the comment is getting at here. Is it somehow possible that the new page has style
            // definitions that have not been captured yet? We can't just do this here, because the insertion involves
            // async code, and the new page has not been fully loaded (and the system is not in a valid state) for saving.
            //_pageRefreshEvent.Raise(PageRefreshEvent.SaveBehavior.JustRedisplay); // needed to get the styles updated
            request.PostSucceeded();
        }

        /// <summary>
        /// Ensure the book folder has the video-placeholder.svg file if it is needed by the template
        /// page.
        /// </summary>
        /// <remarks>
        /// See https://issues.bloomlibrary.org/youtrack/issue/BL-6920.
        /// </remarks>
        private void CopyVideoPlaceHolderIfNeeded(IPage templatePage)
        {
            // The need for video-placeholder.svg is given by an element in the page that looks
            // something like this:
            //     <div class="bloom-videoContainer bloom-noVideoSelected bloom-leadingElement" />
            // Note that the page's HTML doesn't directly reference the image file.  That's done
            // by the CSS which interprets bloom-noVideoSelected.
            var div = templatePage.GetDivNodeForThisPage();
            if (div.SelectSingleNode(".//div[contains(@class, 'bloom-noVideoSelected')]") != null)
                Book.Book.EnsureVideoPlaceholderFile(_pageSelection.CurrentSelection.Book);
        }

        private void HandleChangeLayout(ApiRequest request)
        {
            (IPage templatePage, bool changeWholeBook, int dummy, bool allowDataLoss) =
                GetPageTemplateAndUserStyles(request);
            if (templatePage == null)
                return;
            var pageId = _pageSelection.CurrentSelection.Id;
            _editingModel.SaveThen(
                () =>
                {
                    CopyVideoPlaceHolderIfNeeded(templatePage);
                    var pageToChange = _pageSelection.CurrentSelection;
                    if (templatePage.Book != null) // may be null in unit tests that are unconcerned with stylesheets
                        HtmlDom.AddStylesheetFromAnotherBook(
                            templatePage.Book.OurHtmlDom,
                            pageToChange.Book.OurHtmlDom
                        );
                    if (changeWholeBook)
                        ChangeSimilarPagesInEntireBook(pageToChange, templatePage, allowDataLoss);
                    else
                        pageToChange.Book.UpdatePageToTemplateAndUpdateLineage(
                            pageToChange,
                            templatePage
                        );

                    return pageId;
                },
                () => { } // wrong state, do nothing
            );
            request.PostSucceeded();
        }

        private static void ChangeSimilarPagesInEntireBook(
            IPage currentSelectedPage,
            IPage newTemplatePage,
            bool allowDataLoss
        )
        {
            var book = currentSelectedPage.Book;
            var ancestorPageId = currentSelectedPage.IdOfFirstAncestor;
            // besides being more efficient, it's important to get these first because
            // currentPage is one that will certainly be changed.
            var (oldTextCount, oldImageCount, oldVideoCount, oldWidgetCount) =
                HtmlDom.GetEditableDataCounts(currentSelectedPage.GetDivNodeForThisPage());
            foreach (var page in book.GetPages())
            {
                // We need to decide whether a page is 'similar' to the selected page, which the user
                // has said to change to newTemplatePage. We first require it to have been derived originally
                // from the same template. This is not necessarily much help in the origami world, since
                // two very dissimilar pages could both have been created from 'custom page' or even
                // 'basic text and picture'. But if someone is working from a specialized template, it might
                // restrict things usefully. (We don't of course ever change xmatter with this tool.)
                if (page.IsXMatter || page.IdOfFirstAncestor != ancestorPageId)
                    continue;
                var (textCount, imageCount, videoCount, widgetCount) =
                    HtmlDom.GetEditableDataCounts(page.GetDivNodeForThisPage());
                // Even if the pages have the same original template, and the user has OK'd the kind of data
                // loss to be anticipated on the selected page, if another page has been edited to have a different number
                // of editable blocks of some kind, it's too dangerous to migrate it; it's not 'similar' enough.
                // For an extreme example, the user might have a custom page with an extra block at the end
                // to hold a footnote, and attempt to change layout to a single text block to delete all the footnotes.
                // But if all the pages in the book were derived from custom layout, this could delete all but the first
                // text block from everything!
                // (This is somewhat arbitrary; see the discussion in BL-11147 for further examples.)
                if (
                    textCount != oldTextCount
                    || imageCount != oldImageCount
                    || videoCount != oldVideoCount
                    || widgetCount != oldWidgetCount
                )
                    continue;

                // The user may have explicitly allowed possible data loss.
                // See https://issues.bloomlibrary.org/youtrack/issue/BL-6921.
                book.UpdatePageToTemplateAndUpdateLineage(page, newTemplatePage, allowDataLoss);
            }
        }

        private (
            IPage templatePage,
            bool convertWholeBook,
            int numberToAdd,
            bool allowDataLoss
        ) GetPageTemplateAndUserStyles(ApiRequest request)
        {
            var convertWholeBook = false;
            var requestData = DynamicJson.Parse(request.RequiredPostJson());
            var templateBookPath = HttpUtility.HtmlDecode(requestData.templateBookPath);
            var templateBook = _sourceCollectionsList.FindAndCreateTemplateBookByFullPath(
                templateBookPath
            );
            if (templateBook == null)
            {
                request.Failed("Could not find template book " + templateBookPath);
                return (null, false, -1, false);
            }

            var pageDictionary = templateBook.GetTemplatePagesIdDictionary();
            if (!pageDictionary.TryGetValue(requestData.pageId, out IPage page))
            {
                request.Failed(
                    "Could not find the page "
                        + requestData.pageId
                        + " in the template book "
                        + requestData.templateBookUrl
                );
                return (null, false, -1, false);
            }

            if (requestData.convertWholeBook)
                convertWholeBook = true;
            var allowDataLoss = requestData.allowDataLoss;
            var pageDiv = page.GetDivNodeForThisPage();
            if (!string.IsNullOrEmpty(requestData.dataToolId))
            {
                pageDiv.SetAttribute("data-tool-id", requestData.dataToolId);
            }
            // Remove the data-feature attribute if it exists, so that it doesn't get copied to the
            // new page.  It's already served its purpose in the Add Page dialog.
            if (pageDiv.HasAttribute("data-feature"))
            {
                pageDiv.RemoveAttribute("data-feature");
            }

            int addNum;
            // Unfortunately, a try-catch is the only reliable way to know if 'numberToAdd' is defined or not.
            try
            {
                addNum = (int)requestData.numberToAdd;
            }
            catch (RuntimeBinderException)
            {
                addNum = 1;
            }

            return (page, convertWholeBook, addNum, allowDataLoss);
        }
    }
}
