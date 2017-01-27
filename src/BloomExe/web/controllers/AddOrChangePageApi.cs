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

		public void RegisterWithServer(EnhancedImageServer server)
		{
			server.RegisterEndpointHandler("addPage", HandleAddPage);
			server.RegisterEndpointHandler("changeLayout", HandleChangeLayout);
		}

		private void HandleAddPage(ApiRequest request)
		{
			XmlNode userStylesOnPage;
			var templatePage = GetPageTemplateAndUserStyles(request, out userStylesOnPage);
			if (templatePage != null)
			{
				_templateInsertionCommand.Insert(templatePage as Page, userStylesOnPage);
				_pageRefreshEvent.Raise(PageRefreshEvent.SaveBehavior.JustRedisplay); // needed to get the styles updated
				request.Succeeded();
				return;
			}
		}

		private void HandleChangeLayout(ApiRequest request)
		{
			XmlNode userStylesOnPage;
			var templatePage = GetPageTemplateAndUserStyles(request, out userStylesOnPage);
			if (templatePage != null)
			{
				var pageToChange = /*PageChangingLayout ??*/ _pageSelection.CurrentSelection;
				var book = _pageSelection.CurrentSelection.Book;
				book.UpdatePageToTemplate(book.OurHtmlDom, templatePage.GetDivNodeForThisPage(), pageToChange.Id);
				if (userStylesOnPage != null)
				{
					var existingUserStyles = book.GetOrCreateUserModifiedStyleElementFromStorage();
					var newMergedUserStyleXml = HtmlDom.MergeUserStylesOnInsertion(existingUserStyles, userStylesOnPage);
					existingUserStyles.InnerXml = newMergedUserStyleXml;
					book.Save();
				}
				// The Page objects are cached in the page list and may be used if we issue another
				// change layout command. We must update their lineage so the right "current layout"
				// will be shown if the user changes the layout of the same page again.
				var pageChanged = pageToChange as Page;
				if (pageChanged != null)
					pageChanged.UpdateLineage(new[] { templatePage.Id });

				_pageRefreshEvent.Raise(PageRefreshEvent.SaveBehavior.JustRedisplay);
				request.Succeeded();
			}
		}

		private IPage GetPageTemplateAndUserStyles(ApiRequest request, out XmlNode userStylesOnPage)
		{
			userStylesOnPage = null;
			var requestData = DynamicJson.Parse(request.RequiredPostJson());
			//var templateBookUrl = request.RequiredParam("templateBookUrl");
			var templateBookPath = HttpUtility.HtmlDecode(requestData.templateBookPath);
			var templateBook = _sourceCollectionsList.FindAndCreateTemplateBookByFullPath(templateBookPath);
			if(templateBook == null)
			{
				request.Failed("Could not find template book " + requestData.templateBookUrl);
				return null;
			}

			var pageDictionary = templateBook.GetTemplatePagesIdDictionary();
			IPage page = null;
			if(!pageDictionary.TryGetValue(requestData.pageId, out page))
			{
				request.Failed("Could not find the page " + requestData.pageId + " in the template book " + requestData.templateBookUrl);
				return null;
			}
			var domForPage = templateBook.GetEditableHtmlDomForPage(page);
			userStylesOnPage = HtmlDom.GetUserModifiableStylesUsedOnPage(domForPage); // could be empty
			return page;
		}
	}
}
