using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Dynamic;
using System.IO;
using System.Linq;
using Bloom.Api;
using Bloom.Book;
using Bloom.Collection;
using Bloom.CollectionTab;
using Bloom.TeamCollection;
using Bloom.ToPalaso;
using Gecko.WebIDL;
using Newtonsoft.Json;
using SIL.IO;
using SIL.Linq;
using SIL.PlatformUtilities;
using SIL.Reporting;

namespace Bloom.web.controllers
{

	public class CollectionApi
	{
		private readonly CollectionSettings _settings;
		private readonly LibraryModel _libraryModel;
		public const string kApiUrlPart = "collections/";
		private readonly BloomWebSocketServer _webSocketServer;
		public 	 CollectionApi(CollectionSettings settings, LibraryModel libraryModel, BloomWebSocketServer webSocketServer)
		{
			_settings = settings;
			_libraryModel = libraryModel;
			_webSocketServer = webSocketServer;
		}

		public void RegisterWithApiHandler(BloomApiHandler apiHandler)
		{
			apiHandler.RegisterEndpointHandler(kApiUrlPart + "list", HandleListRequest, true);

			apiHandler.RegisterEndpointHandler(kApiUrlPart + "name", request =>
			{
				// always null? request.ReplyWithText(_collection.Name);
				request.ReplyWithText(_settings.CollectionName);
			}, true);

			apiHandler.RegisterEndpointHandler(kApiUrlPart + "books", HandleBooksRequest, true);
			apiHandler.RegisterEndpointHandler(kApiUrlPart + "book", HandleThumbnailRequest, true);

			apiHandler.RegisterEndpointHandler(kApiUrlPart + "selected-book-id", request =>
			{
				switch (request.HttpMethod)
				{
					case HttpMethods.Get:
						request.ReplyWithText("" + _libraryModel.GetSelectedBookOrNull()?.ID);
						break;
					case HttpMethods.Post:
						var book = GetBookObjectFromPost(request);
						_libraryModel.SelectBook(book);
						request.PostSucceeded();
						break;
				}
			}, true);

			apiHandler.RegisterEndpointHandler(kApiUrlPart+"duplicateBook", HandleDuplicateBook, true);
			apiHandler.RegisterEndpointHandler(kApiUrlPart + "deleteBook", HandleDeleteBook, true);
		}

		private void HandleDuplicateBook(ApiRequest request)
		{
			var book = GetBookObjectFromPost(request);
			var collection = GetCollectionOfRequest(request);
			var newBookDir = book.Storage.Duplicate();

			// Get rid of any TC status we copied from the original, so Bloom treats it correctly as a new book.
			BookStorage.RemoveLocalOnlyFiles(newBookDir);

			// reload the collection
			// I hope we can get rid of this when we retire the old LibraryListView, but for now we need to keep both views up to date.
			// optimize: we only need to reload the first (editable) collection; better yet, we only need to add the one new book to it.
			_libraryModel.ReloadCollections();

			_webSocketServer.SendEvent("editableCollectionList", "reload:" + collection.PathToDirectory);

			var dupInfo = _libraryModel.TheOneEditableCollection.GetBookInfos()
				.FirstOrDefault(info => info.FolderPath == newBookDir);
			if (dupInfo != null)
			{
				var newBook = _libraryModel.GetBookFromBookInfo(dupInfo);
				// Select the new book
				_libraryModel.SelectBook(newBook);
				BookHistory.AddEvent(newBook, BookHistoryEventType.Created, $"Duplicated from existing book \"{book.Title}\"");
			}

			request.PostSucceeded();
		}

		private void HandleDeleteBook(ApiRequest request)
		{
			var book = GetBookObjectFromPost(request);
			_libraryModel.DeleteBook(book);
			request.PostSucceeded();
		}

		// List out all the collections we have loaded
		public void HandleListRequest(ApiRequest request)
		{
			dynamic output = new List<dynamic>();
			_libraryModel.GetBookCollections().ForEach(c =>
			{
				Debug.WriteLine($"collection: {c.Name}-->{c.PathToDirectory}");
				output.Add(
					new
					{
						id = c.PathToDirectory,
						name = c.Name
					});
			});
			request.ReplyWithJson(JsonConvert.SerializeObject(output));
		}
		public void HandleBooksRequest(ApiRequest request)
		{
			var collection = GetCollectionOfRequest(request);
			if (collection == null)
			{
				return; // have already called request failed at this point
			}

			// Note: the winforms version used ImproveAndRefreshBookButtons(), which may load the whole book.

			var infos = collection.GetBookInfos()
				.Select(info =>
				{
					//var book = _libraryModel.GetBookFromBookInfo(info);
					return new
						{id = info.Id, title = info.QuickTitleUserDisplay, collectionId = collection.PathToDirectory, folderName= Path.GetFileName(info.FolderPath) };
				});
			var json = DynamicJson.Serialize(infos);
			request.ReplyWithJson(json);
		}

		private BookCollection GetCollectionOfRequest(ApiRequest request)
		{
			var id = request.RequiredParam("collection-id").Trim();
			var collection = _libraryModel.GetBookCollections().Find(c => c.PathToDirectory == id);
			if (collection == null)
			{
				request.Failed($"Collection named '{id}' was not found.");
			}

			return collection;
		}

		public void HandleThumbnailRequest(ApiRequest request)
		{
			var bookInfo = GetBookInfoFromRequestParam(request);

			// TODO: This is just a hack to get something showing. It can't make new thumbnails
			string path = Path.Combine(bookInfo.FolderPath, "thumbnail.png");
			if (RobustFile.Exists(path))
				request.ReplyWithImage(path);
			else request.Failed("Thumbnail doesn't exist, and making a new thumbnail is not yet implemented.");
		}

		private BookInfo GetBookInfoFromRequestParam(ApiRequest request)
		{
			var bookId = request.RequiredParam("book-id");
			return GetCollectionOfRequest(request).GetBookInfos().FirstOrDefault(info => info.Id == bookId);
		}
		private BookInfo GetBookInfoFromPost(ApiRequest request)
		{
			var bookId = request.RequiredPostString();
			return GetCollectionOfRequest(request).GetBookInfos().FirstOrDefault(info => info.Id == bookId);
		}

		private Book.Book GetBookObjectFromPost(ApiRequest request)
		{
			var info = GetBookInfoFromPost(request);
			return _libraryModel.GetBookFromBookInfo(info);

		}
	}
}
