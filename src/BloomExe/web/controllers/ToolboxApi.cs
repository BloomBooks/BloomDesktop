using System.IO;
using System.Linq;
using Bloom.Api;
using Bloom.Book;

namespace BloomTests.web.controllers
{
	/// <summary>
	/// Handles Api requests for the toolbox itself.
	/// </summary>
	public class ToolboxApi
	{
		private readonly BookSelection _bookSelection;

		// Called by autofac, which creates the one instance and registers it with the server.
		public ToolboxApi(BookSelection _bookSelection)
		{
			this._bookSelection = _bookSelection;
		}

		public void RegisterWithServer(BloomServer server)
		{
			server.RegisterEndpointHandler("toolbox/enabledTools", HandleEnabledToolsRequest, true);
			server.RegisterEndpointHandler("toolbox/fileExists", HandleFileExistsRequest, true);
		}

		private Bloom.Book.Book CurrentBook => _bookSelection.CurrentSelection;

		public void HandleEnabledToolsRequest(ApiRequest request)
		{
			lock (request)
			{
				request.ReplyWithText(string.Join(",",CurrentBook.BookInfo.Tools.Where(t =>t.Enabled).Select(t => t.ToolId)));
			}
		}

		public void HandleFileExistsRequest(ApiRequest request)
		{
			lock (request)
			{
				var fileName = request.RequiredParam("filename");
				var path = Path.Combine(_bookSelection.CurrentSelection.FolderPath, fileName);
				request.ReplyWithText(File.Exists(path) ? "true" : "false");
			}
		}
	}
}
