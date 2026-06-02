using System;
using Bloom.Api;
using Bloom.Book;
using Bloom.CollectionTab;
using Bloom.Edit;
using Bloom.web;
using Bloom.WebLibraryIntegration;
using SIL.Reporting;

namespace Bloom.web.controllers
{
    /// <summary>
    /// API functions which are called from outside of Bloom
    /// </summary>
    public class ExternalApi
    {
        public static event EventHandler LoginSuccessful;

        private BloomLibraryBookApiClient _bloomLibraryBookApiClient;
        private readonly CollectionModel _collectionModel;
        private readonly EditingModel _editingModel;

        // Called by autofac, which creates the one instance and registers it with the server.
        public ExternalApi(
            BloomLibraryBookApiClient bloomLibraryBookApiClient,
            CollectionModel collectionModel,
            EditingModel editingModel
        )
        {
            _bloomLibraryBookApiClient = bloomLibraryBookApiClient;
            _collectionModel = collectionModel;
            _editingModel = editingModel;
        }

        public void RegisterWithApiHandler(BloomApiHandler apiHandler)
        {
            // This is called from bloomlibrary.org after a successful login.
            apiHandler.RegisterEndpointHandler(
                "external/login",
                request =>
                {
                    if (request.HttpMethod == HttpMethods.Post)
                    {
                        var requestData = DynamicJson.Parse(request.RequiredPostJson());
                        string token = requestData.sessionToken;
                        string email = requestData.email;
                        string userId = requestData.userId;
                        //Debug.WriteLine("Got login data " + email + " with token " + token + " and id " + userId);
                        _bloomLibraryBookApiClient.SetLoginData(
                            email,
                            userId,
                            token,
                            BookUpload.Destination
                        );
                        LoginSuccessful?.Invoke(this, null);

                        request.PostSucceeded();

                        Shell.ComeToFront();
                    }
                    else if (request.HttpMethod == HttpMethods.Options)
                    {
                        // blorg will send an OPTIONS request; if we don't respond successfully, things go badly.
                        request.PostSucceeded();
                    }
                },
                false
            );

            // This is called from bloomlibrary.org after a successful logout.
            apiHandler.RegisterEndpointHandler(
                "external/bringToFront",
                request =>
                {
                    if (request.HttpMethod == HttpMethods.Post)
                    {
                        request.PostSucceeded();

                        Shell.ComeToFront();
                    }
                    else if (request.HttpMethod == HttpMethods.Options)
                    {
                        // blorg will send an OPTIONS request; if we don't respond successfully, things go badly.
                        request.PostSucceeded();
                    }
                },
                false
            );

            // Called by an external utility (e.g. a book-conversion tool) after it has written or
            // overwritten a book folder in this collection on disk. We make the running Bloom show the
            // current state of that book: a brand-new book is added to the collection list; a re-imported
            // existing book has its display refreshed. If the re-imported book happens to be the one open
            // in the Edit tab, we throw away any unsaved edits and reload it from disk.
            //
            // This must run on the UI thread because it can reload the Edit tab's view.
            apiHandler.RegisterEndpointHandler(
                "external/updateBook",
                HandleUpdateBook,
                handleOnUiThread: true
            );
        }

        private void HandleUpdateBook(ApiRequest request)
        {
            if (request.HttpMethod == HttpMethods.Options)
            {
                // Allow a CORS preflight request to succeed (as the login endpoint does).
                request.PostSucceeded();
                return;
            }
            if (request.HttpMethod != HttpMethods.Post)
            {
                request.Failed("external/updateBook only supports POST");
                return;
            }

            string id = null;
            try
            {
                // Note: we parse with Newtonsoft rather than Bloom's DynamicJson because the body
                // typically contains a Windows folderPath, and DynamicJson's JSON->XML conversion
                // throws on the backslashes in such paths.
                var data = Newtonsoft.Json.Linq.JObject.Parse(request.RequiredPostJson());
                id = (string)data["id"];
                if (string.IsNullOrEmpty(id))
                {
                    request.Failed("external/updateBook requires a book 'id'");
                    return;
                }

                var editableCollection = _collectionModel.TheOneEditableCollection;
                var collectionPath = editableCollection.PathToDirectory;
                var bookInfo = _collectionModel.BookInfoFromCollectionAndId(collectionPath, id);

                bool added = bookInfo == null;

                if (added)
                {
                    // A new book appeared on disk. Rescan the collection so it shows up in the list,
                    // then locate it so we can name it in the toast and build its thumbnail.
                    _collectionModel.ReloadEditableCollection();
                    bookInfo = _collectionModel.BookInfoFromCollectionAndId(collectionPath, id);
                }
                else
                {
                    // The book already existed and has been re-imported/overwritten on disk.
                    var selected = _collectionModel.GetSelectedBookOrNull();
                    if (selected != null && selected.ID == id)
                    {
                        // It's the book currently open in the Edit tab. Discard any unsaved edits to it
                        // and reload it from disk. We do NOT touch the editor for any other book, so a
                        // user editing an unrelated book never loses work.
                        _editingModel.ReloadCurrentBookDiscardingEdits();
                    }
                    // Re-read the collection so the list (titles, sort order) reflects the new content,
                    // then refresh the thumbnail below.
                    _collectionModel.ReloadEditableCollection();
                    bookInfo = _collectionModel.BookInfoFromCollectionAndId(collectionPath, id);
                }

                string title = bookInfo?.Title ?? bookInfo?.QuickTitleUserDisplay ?? "";
                if (bookInfo != null)
                {
                    // GetBookFromBookInfo returns the current selection (already reloaded above) when this
                    // is the selected book, otherwise a fresh Book read from disk; either way the thumbnail
                    // reflects the new content.
                    _collectionModel.UpdateThumbnailAsync(
                        _collectionModel.GetBookFromBookInfo(bookInfo)
                    );
                }

                // Intentionally NOT localized: this is a developer/operator-facing notification driven by
                // an external automation tool. We include a timestamp and keep the toast up for a few
                // minutes so the user can see, after the fact, that (and when) an external update landed.
                var timestamp = DateTime.Now.ToString("h:mm:ss tt");
                var verb = added ? "Added" : "Updated";
                var message = $"{verb} book \"{title}\" ({timestamp})";
                ToastService.ShowToast(text: message, durationSeconds: 180);

                request.PostSucceeded();
            }
            catch (Exception e)
            {
                Logger.WriteError("external/updateBook failed for book id " + id, e);
                request.Failed("external/updateBook failed: " + e.Message);
            }
        }
    }
}
