using System;
using System.Collections.Generic;
using System.Drawing;
using System.Dynamic;
using System.IO;
using System.Linq;
using Bloom.Api;
using Bloom.Book;
using Bloom.Collection;
using Bloom.CollectionTab;
using Bloom.Edit;
using Bloom.Properties;
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
		public const string kApiUrlPart = "collection/";
		public 	 CollectionApi(CollectionSettings settings, LibraryModel libraryModel)
		{
			_settings = settings;
			_libraryModel = libraryModel;
		}

		public void RegisterWithApiHandler(BloomApiHandler apiHandler)
		{
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

		public void HandleBooksRequest(ApiRequest request)
		{
			var infos = _libraryModel.TheOneEditableCollection.GetBookInfos()
				.Select(info =>
				{
					var book = _libraryModel.GetBookFromBookInfo(info);
					return new
						{id = info.Id, title = info.Title};
				});
			var json = DynamicJson.Serialize(infos);
			request.ReplyWithJson(json);
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
			// TODO don't assume what collection it is
			//var collectionId = request.RequiredParam("collection-id");
			return _libraryModel.TheOneEditableCollection.GetBookInfos().FirstOrDefault(info => info.Id == bookId);
		}
		private BookInfo GetBookInfoFromPost(ApiRequest request)
		{
			var bookId = request.RequiredPostString();
			// TODO don't assume what collection it is
			//var collectionId = request.RequiredParam("collection-id");
			return _libraryModel.TheOneEditableCollection.GetBookInfos().FirstOrDefault(info => info.Id == bookId);
		}

		private Book.Book GetBookObjectFromPost(ApiRequest request)
		{
			var info = GetBookInfoFromPost(request);
			return _libraryModel.GetBookFromBookInfo(info);

		}
	}
}
