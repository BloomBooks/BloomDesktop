using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Xml;
using Bloom.Api;
using Bloom.Book;
using Bloom.Edit;
using SIL.Extensions;
using SIL.Xml;

namespace Bloom.web.controllers
{
	/// <summary>
	/// This API handles requests to add new pages and change existing pages to match some other layout.
	/// </summary>
	public class AddOrChangePageApi
	{
		private Dictionary<string, IPage> _templatePagesDict;
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
				_templateInsertionCommand.Insert(templatePage as Page);
				_pageRefreshEvent.Raise(PageRefreshEvent.SaveBehavior.JustRedisplay); // needed to get the styles updated
				request.PostSucceeded();
				return;
			}
		}

		private void HandleChangeLayout(ApiRequest request)
		{
			bool changeWholeBook;
			var templatePage = GetPageTemplateAndUserStyles(request, out changeWholeBook);
			if (templatePage != null)
			{
				var pageToChange = _pageSelection.CurrentSelection;
				if (changeWholeBook)
					ChangeSimilarPagesInEntireBook(pageToChange, templatePage);
				else
					pageToChange.Book.UpdatePageToTemplateAndUpdateLineage(pageToChange, templatePage);

				_pageRefreshEvent.Raise(PageRefreshEvent.SaveBehavior.JustRedisplay);
				request.PostSucceeded();
			}
		}

		private static void ChangeSimilarPagesInEntireBook(IPage currentSelectedPage, IPage newTemplatePage)
		{
			var book = currentSelectedPage.Book;
			var ancestorPageId = currentSelectedPage.IdOfFirstAncestor;
			foreach (var page in book.GetPages())
			{
				if (page.IsXMatter || page.IdOfFirstAncestor != ancestorPageId)
					continue;
				book.UpdatePageToTemplateAndUpdateLineage(page, newTemplatePage, false);
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
