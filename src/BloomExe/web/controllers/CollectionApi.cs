using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Dynamic;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Bloom.Api;
using Bloom.Book;
using Bloom.Collection;
using Bloom.CollectionTab;
using Bloom.MiscUI;
using Bloom.Properties;
using Bloom.Spreadsheet;
using Bloom.TeamCollection;
using Bloom.ToPalaso;
using Bloom.Workspace;
using DesktopAnalytics;
using Gecko.WebIDL;
using L10NSharp;
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
		private readonly BookSelection _bookSelection;

		private int _thumbnailEventsToWaitFor = -1;

		private object _thumbnailEventsLock = new object();
		// public so that WorkspaceView can set it in constructor.
		// We'd prefer to just let the WorkspaceView be a constructor arg passed to this by Autofac,
		// but that throws an exception, probably there is some circularity.
		public WorkspaceView WorkspaceView;
		public 	 CollectionApi(CollectionSettings settings, LibraryModel libraryModel, BookSelection bookSelection)
		{
			_settings = settings;
			_libraryModel = libraryModel;
			_bookSelection = bookSelection;
		}

		public void RegisterWithApiHandler(BloomApiHandler apiHandler)
		{
			apiHandler.RegisterEndpointHandlerExact(kApiUrlPart + "list", HandleListRequest, true);

			apiHandler.RegisterEndpointHandlerExact(kApiUrlPart + "name", request =>
			{
				// always null? request.ReplyWithText(_collection.Name);
				request.ReplyWithText(_settings.CollectionName);
			}, true);

			apiHandler.RegisterEndpointHandlerExact(kApiUrlPart + "books", HandleBooksRequest, false, false);
			apiHandler.RegisterEndpointHandlerExact(kApiUrlPart + "book/thumbnail", HandleThumbnailRequest, false, false);

			// Note: the get part of this doesn't need to run on the UI thread, or even requiresSync. If it gets called a lot, consider
			// using different patterns for get and set so we can not use the uI thread for get.
			apiHandler.RegisterEndpointHandlerExact(kApiUrlPart + "selected-book-id", request =>
			{
				switch (request.HttpMethod)
				{
					case HttpMethods.Get:
						request.ReplyWithText("" + _libraryModel.GetSelectedBookOrNull()?.ID);
						break;
					case HttpMethods.Post:
						var book = GetBookObjectFromPost(request);
						if (book.FolderPath != _bookSelection?.CurrentSelection?.FolderPath)
						{
							_libraryModel.SelectBook(book);
						}

						request.PostSucceeded();
						break;
				}
			}, true);

			apiHandler.RegisterEndpointHandlerExact(kApiUrlPart + "duplicateBook/", (request) =>
			{
				_libraryModel.DuplicateBook(GetBookObjectFromPost(request));
				request.PostSucceeded();
			}, true);
			apiHandler.RegisterEndpointHandlerExact(kApiUrlPart + "deleteBook/", (request) =>
			{
				var collection = GetCollectionOfRequest(request);
				_libraryModel.DeleteBook(GetBookObjectFromPost(request), collection);
				request.PostSucceeded();
			}, true);
			apiHandler.RegisterEndpointHandlerExact(kApiUrlPart + "collectionProps/", HandleCollectionProps, false, false);

			apiHandler.RegisterEndpointHandlerExact(kApiUrlPart + "makeBloompack/", (request) =>
				{
					_libraryModel.MakeReaderTemplateBloompack();
					request.PostSucceeded();
				},
				true);

			apiHandler.RegisterEndpointHandlerExact(kApiUrlPart + "doChecksOfAllBooks/", (request) =>
				{
					_libraryModel.DoChecksOfAllBooks();
					request.PostSucceeded();
				},
				true);

			apiHandler.RegisterEndpointHandlerExact(kApiUrlPart + "rescueMissingImages/", (request) =>
				{
					_libraryModel.RescueMissingImages();
					request.PostSucceeded();
				},
				true);

			apiHandler.RegisterEndpointHandlerExact(kApiUrlPart + "doUpdatesOfAllBooks/", (request) =>
				{
					_libraryModel.DoUpdatesOfAllBooks();
					request.PostSucceeded();
				},
				true);

		}

		// needs to be thread-safe
		private void HandleCollectionProps(ApiRequest request)
		{
			var collection = GetCollectionOfRequest(request);
			dynamic props = new ExpandoObject();
			props.isFactoryInstalled = collection.IsFactoryInstalled;
			props.containsDownloadedBooks = collection.ContainsDownloadedBooks;
			request.ReplyWithJson(JsonConvert.SerializeObject(props));
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
				}).ToArray();
			// The goal here is to draw the book buttons before we tie up the UI thread for a long time loading
			// the previously selected book and showing it. So we're going to wait until we get a few requests
			// for the thumbnails used in book buttons. But we need to be sure we will actually get those requests,
			// otherwise, doing the selected book stuff and hiding the splash screen and so on could be permanently
			// prevented. Unfortunately, the buttons are lazy, and in a pathological case the user might drag the
			// splitters and size the window so only one of these buttons is visible. Even in such a case, there
			// should be at least one more visible in the templates area. So I think we can be confident of
			// getting at least two requests. Fortunately, waiting for those seems to be enough to make it look
			// as if we're prioritizing the whole primary collection.
			// We need the count to be at least one, even if the main collection is empty, so that the milestone
			// will always be reported. 
			lock (_thumbnailEventsLock)
			{
				if (collection.Type == BookCollection.CollectionType.TheOneEditableCollection)
					_thumbnailEventsToWaitFor = Math.Max(Math.Min(infos.Length, 2), 1);
			}

			var json = DynamicJson.Serialize(infos);
			request.ReplyWithJson(json);
		}

		// This needs to be thread-safe.
		private BookCollection GetCollectionOfRequest(ApiRequest request)
		{
			var id = request.RequiredParam("collection-id").Trim();
			var collection = _libraryModel.GetBookCollections().FirstOrDefault(c => c.PathToDirectory == id);
			if (collection == null)
			{
				request.Failed($"Collection named '{id}' was not found.");
			}

			return collection;
		}

		public void HandleThumbnailRequest(ApiRequest request)
		{
			var bookInfo = GetBookInfoFromRequestParam(request);
			lock (_thumbnailEventsLock)
			{
				if (_thumbnailEventsToWaitFor > 0)
				{
					_thumbnailEventsToWaitFor--;
					if (_thumbnailEventsToWaitFor == 0)
						StartupScreenManager.StartupMilestoneReached("collectionButtonsDrawn");
				}
			}

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

		// Needs to be thread-safe
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
