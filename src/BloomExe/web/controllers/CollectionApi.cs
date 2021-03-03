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
		public 	 CollectionApi(CollectionSettings settings, LibraryModel libraryModel)
		{
			_settings = settings;
			_libraryModel = libraryModel;
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
			var infos = collection.GetBookInfos()
				.Select(info =>
				{
					//var book = _libraryModel.GetBookFromBookInfo(info);
					return new
						{id = info.Id, title = info.Title, collectionId = collection.PathToDirectory };
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
			//try
			//{
				var bookInfo = GetBookInfoFromRequestParam(request);

				// TODO: This is just a hack to get something showing. It can't make new thumbnails
				string path = Path.Combine(bookInfo.FolderPath, "thumbnail.png");
				if (RobustFile.Exists(path))
					request.ReplyWithImage(path);
				else request.Failed("Thumbnail doesn't exist, and making a new thumbnail is not yet implemented.");
			//}
			//catch(Exception e)
			//{
			//	request.Failed(e.Message);
			//}
		}

		private BookInfo GetBookInfoFromRequestParam(ApiRequest request)
		{
			var bookId = request.RequiredParam("book-id");
			// TODO don't assume what collection it is
			//var collectionId = request.RequiredParam("collection-id");
			return GetCollectionOfRequest(request).GetBookInfos().FirstOrDefault(info => info.Id == bookId);
		}
		private BookInfo GetBookInfoFromPost(ApiRequest request)
		{
			var bookId = request.RequiredPostString();
			// TODO don't assume what collection it is
			//var collectionId = request.RequiredParam("collection-id");
			return GetCollectionOfRequest(request).GetBookInfos().FirstOrDefault(info => info.Id == bookId);
		}

		private Book.Book GetBookObjectFromPost(ApiRequest request)
		{
			var info = GetBookInfoFromPost(request);
			return _libraryModel.GetBookFromBookInfo(info);

		}
	}
}
