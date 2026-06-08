using System;
using Bloom.Api;
using Bloom.Book;
using Bloom.CollectionTab;
using Bloom.Edit;
using Bloom.web;
using Bloom.WebLibraryIntegration;
using Bloom.Workspace;
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
        private readonly WorkspaceTabSelection _tabSelection;
        private readonly BookServer _bookServer;

        // Called by autofac, which creates the one instance and registers it with the server.
        public ExternalApi(
            BloomLibraryBookApiClient bloomLibraryBookApiClient,
            CollectionModel collectionModel,
            EditingModel editingModel,
            WorkspaceTabSelection tabSelection,
            BookServer bookServer
        )
        {
            _bloomLibraryBookApiClient = bloomLibraryBookApiClient;
            _collectionModel = collectionModel;
            _editingModel = editingModel;
            _tabSelection = tabSelection;
            _bookServer = bookServer;
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

            // This is called from outside Bloom (e.g. bloomlibrary.org) to bring the Bloom window to the front.
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

            // Called by an external utility to make the running Bloom select a particular book in the
            // collection (the one whose 'id' is supplied). This changes the current selection just as if
            // the user had clicked the book in the collection list.
            //
            // We only honor this when the Collection tab is active. Changing the selection while the user
            // is mid-edit would silently discard their unsaved page edits (EditingModel.OnBookSelectionChanged
            // clears _havePageToSave and tears down the live editor) and could leave the Edit tab in a bad
            // state. So if we're not on the Collection tab, we ignore the request rather than risk havoc.
            //
            // This must run on the UI thread because changing the selection updates the UI.
            apiHandler.RegisterEndpointHandler(
                "external/selectBook",
                HandleSelectBook,
                handleOnUiThread: true
            );

            // Called by an external utility (e.g. the PDF→Bloom converter) to run the full "make it
            // right" pass on a book in this collection (the one whose 'id' is supplied): bring it
            // structurally up to date, then process every page off-screen in a real browser the same
            // way visiting each page in the Edit tab does, and save it to disk. This is the step that
            // applies the browser-only fix-ups (image sizing, canvas-element layout, etc.) that raw
            // generated HTML is missing. The call blocks until processing is complete.
            //
            // This must run on the UI thread because it creates and pumps an off-screen WebView2.
            apiHandler.RegisterEndpointHandler(
                "external/process-book",
                HandleProcessBook,
                handleOnUiThread: true
            );
        }

        private void HandleProcessBook(ApiRequest request)
        {
            if (request.HttpMethod == HttpMethods.Options)
            {
                // Allow a CORS preflight request to succeed (as the other external endpoints do).
                request.PostSucceeded();
                return;
            }
            if (request.HttpMethod != HttpMethods.Post)
            {
                request.Failed("external/process-book only supports POST");
                return;
            }

            string id = null;
            try
            {
                var data = Newtonsoft.Json.Linq.JObject.Parse(request.RequiredPostJson());
                id = (string)data["id"];
                if (string.IsNullOrEmpty(id))
                {
                    request.Failed("external/process-book requires a book 'id'");
                    return;
                }

                // Processing rewrites the book on disk. Only do it from the Collection tab, so we
                // never write a book out from under the live editor or fight its save/navigate state
                // machine. (The converter should land here right after adding/selecting the book.)
                if (_tabSelection.ActiveTab != WorkspaceTab.collection)
                {
                    request.Failed(
                        "external/process-book is only allowed while the Collection tab is active"
                    );
                    return;
                }

                var editableCollection = _collectionModel.TheOneEditableCollection;
                var collectionPath = editableCollection.PathToDirectory;
                var bookInfo = _collectionModel.BookInfoFromCollectionAndId(collectionPath, id);
                if (bookInfo == null)
                {
                    // The converter has very likely just written this book to disk, and our in-memory
                    // collection cache doesn't know about it yet (the cache only refreshes on an explicit
                    // reload or a debounced file-watcher event, neither of which is guaranteed to have run
                    // by the time we get here). Rescan the collection from disk and look again before
                    // giving up, mirroring how external/updateBook handles a newly-appeared book.
                    _collectionModel.ReloadEditableCollection();
                    bookInfo = _collectionModel.BookInfoFromCollectionAndId(collectionPath, id);
                }
                if (bookInfo == null)
                {
                    request.Failed("external/process-book could not find a book with id " + id);
                    return;
                }

                // Process a fresh Book read from disk rather than any in-memory selection, so we
                // don't disturb the state of the currently-selected book object.
                var book = _bookServer.GetBookFromBookInfo(bookInfo);
                var pageCount = BookProcessor.ProcessBook(book);

                // If we just rewrote the book that is currently selected, refresh the collection's
                // view of it (list metadata + thumbnail) so the UI reflects the new on-disk content.
                var selected = _collectionModel.GetSelectedBookOrNull();
                if (selected != null && selected.ID == id)
                {
                    _collectionModel.ReloadEditableCollection();
                    var refreshedInfo = _collectionModel.BookInfoFromCollectionAndId(
                        collectionPath,
                        id
                    );
                    if (refreshedInfo != null)
                        _collectionModel.UpdateThumbnailAsync(
                            _collectionModel.GetBookFromBookInfo(refreshedInfo)
                        );
                }

                request.ReplyWithJson(new { processed = pageCount });
            }
            catch (Exception e)
            {
                Logger.WriteError("external/process-book failed for book id " + id, e);
                request.Failed("external/process-book failed: " + e.Message);
            }
        }

        private void HandleSelectBook(ApiRequest request)
        {
            if (request.HttpMethod == HttpMethods.Options)
            {
                // Allow a CORS preflight request to succeed (as the other external endpoints do).
                request.PostSucceeded();
                return;
            }
            if (request.HttpMethod != HttpMethods.Post)
            {
                request.Failed("external/selectBook only supports POST");
                return;
            }

            string id = null;
            try
            {
                // Parse with Newtonsoft rather than Bloom's DynamicJson for consistency with updateBook
                // (DynamicJson's JSON->XML conversion can choke on Windows paths and other content).
                var data = Newtonsoft.Json.Linq.JObject.Parse(request.RequiredPostJson());
                id = (string)data["id"];
                if (string.IsNullOrEmpty(id))
                {
                    request.Failed("external/selectBook requires a book 'id'");
                    return;
                }

                // Only change the selection when the Collection tab is active. If the user is editing
                // or publishing, quietly ignore the request rather than discard their work or disrupt
                // their current tab. We still report success so the caller isn't treated as an error.
                if (_tabSelection.ActiveTab != WorkspaceTab.collection)
                {
                    Logger.WriteEvent(
                        "external/selectBook ignored for book id "
                            + id
                            + " because the Collection tab is not active (ActiveTab="
                            + _tabSelection.ActiveTab
                            + ")"
                    );
                    request.PostSucceeded();
                    return;
                }

                var editableCollection = _collectionModel.TheOneEditableCollection;
                var collectionPath = editableCollection.PathToDirectory;
                var bookInfo = _collectionModel.BookInfoFromCollectionAndId(collectionPath, id);
                if (bookInfo == null)
                {
                    request.Failed("external/selectBook could not find a book with id " + id);
                    return;
                }

                _collectionModel.SelectBook(_collectionModel.GetBookFromBookInfo(bookInfo));

                request.PostSucceeded();
            }
            catch (Exception e)
            {
                Logger.WriteError("external/selectBook failed for book id " + id, e);
                request.Failed("external/selectBook failed: " + e.Message);
            }
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
