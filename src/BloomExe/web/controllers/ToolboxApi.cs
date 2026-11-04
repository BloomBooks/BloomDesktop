using System.IO;
using System.Linq;
using Bloom.Api;
using Bloom.Book;
using Bloom.Edit;
using SIL.IO;

namespace BloomTests.web.controllers
{
    /// <summary>
    /// Handles Api requests for the toolbox itself.
    /// </summary>
    public class ToolboxApi
    {
        private readonly BookSelection _bookSelection;

        /// <summary>
        /// Set by the EditingView, as for the other APIs that need it.
        /// </summary>
        public EditingModel Model { get; set; }

        // Called by autofac, which creates the one instance and registers it with the server.
        public ToolboxApi(BookSelection _bookSelection)
        {
            this._bookSelection = _bookSelection;
        }

        public void RegisterWithApiHandler(BloomApiHandler apiHandler)
        {
            apiHandler.RegisterEndpointHandler(
                "toolbox/enabledTools",
                HandleEnabledToolsRequest,
                true
            );
            apiHandler.RegisterEndpointHandler("toolbox/fileExists", HandleFileExistsRequest, true);
            apiHandler.RegisterBooleanEndpointHandler(
                "toolbox/decodable",
                null,
                (request, b) =>
                {
                    CurrentBook.SetIsDecodable(b);
                    // That changed the book's body, outside the page, so the page's own save
                    // would not write it.
                    Model.NoteBookChangedOutsideThePage();
                },
                true
            );
            apiHandler.RegisterBooleanEndpointHandler(
                "toolbox/leveled",
                null,
                (request, b) =>
                {
                    CurrentBook.SetIsLeveled(b);
                    // That changed the book's body, outside the page, so the page's own save
                    // would not write it.
                    Model.NoteBookChangedOutsideThePage();
                },
                true
            );
        }

        private Bloom.Book.Book CurrentBook => _bookSelection.CurrentSelection;

        public void HandleEnabledToolsRequest(ApiRequest request)
        {
            var book = CurrentBook;
            // CurrentBook can legitimately be null for this call (BL-16017)
            if (book?.BookInfo?.Tools == null)
                request.ReplyWithText("");
            else
                request.ReplyWithText(
                    string.Join(
                        ",",
                        book.BookInfo.Tools.Where(t => t.Enabled).Select(t => t.ToolId)
                    )
                );
        }

        public void HandleFileExistsRequest(ApiRequest request)
        {
            var fileName = request.RequiredParam("filename");
            var folderPath = CurrentBook?.FolderPath;
            if (folderPath == null)
            {
                request.ReplyWithText("false");
            }
            else
            {
                var path = Path.Combine(folderPath, fileName);
                request.ReplyWithText(RobustFile.Exists(path) ? "true" : "false");
            }
        }
    }
}
