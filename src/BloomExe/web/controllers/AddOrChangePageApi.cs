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
				CopyVideoPlaceHolderIfNeeded(templatePage);
				_templateInsertionCommand.Insert(templatePage as Page);
				_pageRefreshEvent.Raise(PageRefreshEvent.SaveBehavior.JustRedisplay); // needed to get the styles updated
				request.PostSucceeded();
				return;
			}
		}

		// These values are from "Sign Language.pug" in the template collection.
		static List<string> _videoPageIds = new List<string> {
			"08422e7b-9406-4d11-8c71-02005b1b8095",	// Big Text Diglot
			"299644f5-addb-476f-a4a5-e3978139b188",	// Video Over Text
			"24c90e90-2711-465d-8f20-980d9ffae299",	// Picture & Video
			"9a4beb1f-46c5-4729-87fc-7a9a7eee534e",	// Big Video Diglot
			"16301dd0-a813-459e-b7e8-294339f7f241"	// Big Picture Diglot
		};
		/// <summary>
		/// Ensure the book folder has the video-placeholder.svg if it is needed by the template page.
		/// </summary>
		/// <remarks>
		/// See https://issues.bloomlibrary.org/youtrack/issue/BL-6920.
		/// </remarks>
		private void CopyVideoPlaceHolderIfNeeded(IPage templatePage)
		{
			if (_videoPageIds.Contains(templatePage.Id))
				_pageSelection.CurrentSelection.Book.Storage.Update("video-placeholder.svg");
		}

		private void HandleChangeLayout(ApiRequest request)
		{
			bool changeWholeBook;
			var templatePage = GetPageTemplateAndUserStyles(request, out changeWholeBook);
			if (templatePage != null)
			{
				CopyVideoPlaceHolderIfNeeded(templatePage);
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
