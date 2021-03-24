using System;
using System.Web;
using System.Windows.Forms;
using Bloom.Api;
using Bloom.Book;
using Bloom.Edit;

namespace Bloom.web.controllers
{
	/// <summary>
	/// This API handles requests to add new pages and change existing pages to match some other layout.
	/// </summary>
	public class AddOrChangePageApi
	{
		private readonly TemplateInsertionCommand _templateInsertionCommand;
		private readonly PageRefreshEvent _pageRefreshEvent;
		private readonly PageSelection _pageSelection;
		private readonly SourceCollectionsList _sourceCollectionsList;

		public AddOrChangePageApi(	TemplateInsertionCommand templateInsertionCommand,
									PageRefreshEvent pageRefreshEvent,
									PageSelection pageSelection,
									SourceCollectionsList sourceCollectionsList)
		{
			_templateInsertionCommand = templateInsertionCommand;
			_pageRefreshEvent = pageRefreshEvent;
			_pageSelection = pageSelection;
			_sourceCollectionsList = sourceCollectionsList;
		}

		public void RegisterWithApiHandler(BloomApiHandler apiHandler)
		{
			// Both of these display UI, expect to require UI thread.
			apiHandler.RegisterEndpointHandler("addPage", HandleAddPage, true);
			apiHandler.RegisterEndpointHandler("changeLayout", HandleChangeLayout, true);
		}

		private void HandleAddPage(ApiRequest request)
		{
			bool unused;
			var templatePage = GetPageTemplateAndUserStyles(request, out unused);
			if (templatePage != null)
			{
				CopyVideoPlaceHolderIfNeeded(templatePage);
				_templateInsertionCommand.Insert(templatePage as Page);
				_pageRefreshEvent.Raise(PageRefreshEvent.SaveBehavior.JustRedisplay); // needed to get the styles updated
				request.PostSucceeded();	// request.Failed already called if template page is null
			}
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
				_pageSelection.CurrentSelection.Book.Storage.Update("video-placeholder.svg");
		}

		private IPage _templatePage;
		private bool _changeWholeBook;
		private void HandleChangeLayout(ApiRequest request)
		{
			_templatePage = GetPageTemplateAndUserStyles(request, out _changeWholeBook);
			if (_templatePage != null)
			{
				// Changing the page layout and refreshing the page immediately here can cause the page list to
				// freeze because the javascript dialog for selecting a page template is still open.  Our memory
				// leak fix of reloading the entire page destroys the underlying javascript (and reloads it),
				// leading to the dialog disappearing but not cleaning up by calling the "editView/setModalState"
				// API with false.  This leaves the pageListView permanently disabled.
				// See https://issues.bloomlibrary.org/youtrack/issue/BL-9712.
				Application.Idle += ChangePageLayoutOnIdle;
				request.PostSucceeded();	// request.Failed already called if template page is null
			}
		}

		private void ChangePageLayoutOnIdle(object sender, EventArgs e)
		{
			Application.Idle -= ChangePageLayoutOnIdle;
			CopyVideoPlaceHolderIfNeeded(_templatePage);
			var pageToChange = _pageSelection.CurrentSelection;
			if (_templatePage.Book != null) // may be null in unit tests that are unconcerned with stylesheets
				HtmlDom.AddStylesheetFromAnotherBook(_templatePage.Book.OurHtmlDom, pageToChange.Book.OurHtmlDom);
			if (_changeWholeBook)
				ChangeSimilarPagesInEntireBook(pageToChange, _templatePage);
			else
				pageToChange.Book.UpdatePageToTemplateAndUpdateLineage(pageToChange, _templatePage);
			_pageRefreshEvent.Raise(PageRefreshEvent.SaveBehavior.JustRedisplay);
		}

		private static void ChangeSimilarPagesInEntireBook(IPage currentSelectedPage, IPage newTemplatePage)
		{
			var book = currentSelectedPage.Book;
			var ancestorPageId = currentSelectedPage.IdOfFirstAncestor;
			foreach (var page in book.GetPages())
			{
				if (page.IsXMatter || page.IdOfFirstAncestor != ancestorPageId)
					continue;
				// The user has explicitly allowed possible data loss.
				// See https://issues.bloomlibrary.org/youtrack/issue/BL-6921.
				book.UpdatePageToTemplateAndUpdateLineage(page, newTemplatePage, true);
			}
		}

		private IPage GetPageTemplateAndUserStyles(ApiRequest request, out bool convertWholeBook)
		{
			convertWholeBook = false;
			var requestData = DynamicJson.Parse(request.RequiredPostJson());
			//var templateBookUrl = request.RequiredParam("templateBookUrl");
			var templateBookPath = HttpUtility.HtmlDecode(requestData.templateBookPath);
			var templateBook = _sourceCollectionsList.FindAndCreateTemplateBookByFullPath(templateBookPath);
			if(templateBook == null)
			{
				request.Failed("Could not find template book " + templateBookPath);
				return null;
			}

			var pageDictionary = templateBook.GetTemplatePagesIdDictionary();
			IPage page = null;
			if(!pageDictionary.TryGetValue(requestData.pageId, out page))
			{
				request.Failed("Could not find the page " + requestData.pageId + " in the template book " + requestData.templateBookUrl);
				return null;
			}

			if (requestData.convertWholeBook)
				convertWholeBook = true;
			return page;
		}
	}
}
