using System.Collections.Generic;
using System.Dynamic;
using System.IO;
using Bloom.Api;
using Bloom.Book;
using Bloom.Edit;
using Newtonsoft.Json;

namespace Bloom.web.controllers
{
	/// <summary>
	///  <a href='/api/pageTemplates'></a>
	/// </summary>
	public class PageTemplatesApi
	{
		private readonly BookSelection _bookSelection;
		private readonly PageSelection _pageSelection;
		private readonly TemplateInsertionCommand _templateInsertionCommand;

		public PageTemplatesApi(BookSelection bookSelection, PageSelection pageSelection, TemplateInsertionCommand templateInsertionCommand)
		{
			_bookSelection = bookSelection;
			_pageSelection = pageSelection;
			_templateInsertionCommand = templateInsertionCommand;
		}

		public void RegisterWithServer(EnhancedImageServer server)
		{
			server.RegisterEndpointHandler("pageTemplates", HandleRequest);
		}

		/// <summary>
		/// </summary>
		public  void HandleRequest(ApiRequest request)
		{
			request.ReplyWithJson(GetAddPageArguments());
		}


		/// <summary>
		/// Returns a json string for initializing the AddPage dialog. It gives paths to our current TemplateBook
		/// and specifies whether the dialog is to be used for adding pages or choosing a different layout.
		/// </summary>
		/// <remarks>If forChooseLayout is true, page argument is required.</remarks>
		public string GetAddPageArguments()
		{
			dynamic addPageSettings = new ExpandoObject();

			addPageSettings.lastPageAdded = _templateInsertionCommand.MostRecentInsertedTemplatePage;
			addPageSettings.orientation = _bookSelection.CurrentSelection.GetLayout().SizeAndOrientation.IsLandScape ? "landscape" : "portrait";

			var groups = new List<dynamic>();
			groups.Add(GetPageGroup(GetPathToCurrentTemplateHtml));
			addPageSettings.collections = groups.ToArray();
			addPageSettings.currentLayout = _pageSelection.CurrentSelection.IdOfFirstAncestor;
			var settingsString = JsonConvert.SerializeObject(addPageSettings);
			return settingsString;
		}

		private dynamic GetPageGroup(string path)
		{
			dynamic pageGroup = new ExpandoObject();
			pageGroup.templateBookFolderUrl = MassageUrlForJavascript(Path.GetDirectoryName(path));
			pageGroup.templateBookUrl = MassageUrlForJavascript(path);
			return pageGroup;
		}

		private string GetPathToCurrentTemplateHtml
		{
			get
			{
				var templateBook = _bookSelection.CurrentSelection.FindTemplateBook();
				if (templateBook == null)
					return null;

				return templateBook.GetPathHtmlFile();
			}
		}

		private const string URL_PREFIX = "/bloom/localhost/";
		private static string MassageUrlForJavascript(string url)
		{
			var newUrl = URL_PREFIX + url;
			return newUrl.Replace(':', '$').Replace('\\', '/');
		}
	}
}
