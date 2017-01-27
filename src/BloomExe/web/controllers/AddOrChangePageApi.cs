using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Xml;
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
			IEnumerable<XmlNode> userStylesOnPage;
			var page = GetPageTemplateAndUserStyles(request, out userStylesOnPage);
			if (page != null)
			{
				_templateInsertionCommand.Insert(page as Page, userStylesOnPage);
				_pageRefreshEvent.Raise(PageRefreshEvent.SaveBehavior.JustRedisplay); // needed to get the styles updated
				request.Succeeded();
				return;
			}
		}

		private void HandleChangeLayout(ApiRequest request)
		{
			IEnumerable<XmlNode> userStylesOnPage;
			var templatePage = GetPageTemplateAndUserStyles(request, out userStylesOnPage);
			if (templatePage != null)
			{
				var pageToChange = /*PageChangingLayout ??*/ _pageSelection.CurrentSelection;
				var book = _pageSelection.CurrentSelection.Book;
				book.UpdatePageToTemplate(book.OurHtmlDom, templatePage.GetDivNodeForThisPage(), pageToChange.Id);
				if (userStylesOnPage != null && userStylesOnPage.Any())
				{
					foreach (var newStyle in userStylesOnPage)
					{
						book.CreateEmptyUserModifiedStyleElement().InnerXml = newStyle.InnerXml;
					}
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

		private IPage GetPageTemplateAndUserStyles(ApiRequest request, out IEnumerable<XmlNode> userStylesOnPage)
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
			userStylesOnPage = GetUserModifiableStylesUsedOnPage(templateBook, page);
			return page;
		}

		private IEnumerable<XmlNode> GetUserModifiableStylesUsedOnPage(Book.Book templateBook, IPage page)
		{
			var pageDom = templateBook.GetEditableHtmlDomForPage(page);
			var nodeList = pageDom.SafeSelectNodes("//style[@title='userModifiedStyles']");
			var trimmedList = from XmlNode node in nodeList
							  where pageDom.SafeSelectNodes("//div[contains(concat(' ', @class, ' '), ' " + GetClassKeyFromStyleNode(node) + " ')]").Count > 0
							  select node;
			return trimmedList;
		}

		private string GetClassKeyFromStyleNode(XmlNode node)
		{
			// Takes a string like "\r\n.StyleName-style {css stuff}" and returns "StyleName-style"
			var afterDot = node.InnerText.Substring(node.InnerText.IndexOf('.') + 1);
			return afterDot.Remove(afterDot.IndexOf("-style") + 6);
		}
	}
}
