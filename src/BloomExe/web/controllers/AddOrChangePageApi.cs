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

		public void RegisterWithServer(FileAndApiServer server)
		{
			// Both of these display UI, expect to require UI thread.
			server.RegisterEndpointHandler("addPage", HandleAddPage, true);
			server.RegisterEndpointHandler("changeLayout", HandleChangeLayout, true);
		}

		private void HandleAddPage(ApiRequest request)
		{
			var templatePage = GetPageTemplateAndUserStyles(request);
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
			var templatePage = GetPageTemplateAndUserStyles(request);
			if (templatePage != null)
			{
				var pageToChange = _pageSelection.CurrentSelection;
				pageToChange.Book.UpdatePageToTemplateAndUpdateLineage(pageToChange, templatePage);

				_pageRefreshEvent.Raise(PageRefreshEvent.SaveBehavior.JustRedisplay);
				request.PostSucceeded();
			}
		}

		private IPage GetPageTemplateAndUserStyles(ApiRequest request)
		{
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
			return page;
		}
	}
}
