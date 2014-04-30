// Copyright (c) 2014 SIL International
// This software is licensed under the MIT License (http://opensource.org/licenses/MIT)

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using Bloom.Book;
using Bloom.Collection;
using Palaso.IO;
using Palaso.Reporting;

namespace Bloom.web
{
	//Though I didn't use it yet, I've since seen this an insteresting tiny example of a minimal server: https://gist.github.com/369432

	public class BloomServer : IDisposable
	{
		private readonly CollectionSettings _collectionSettings;
		private readonly BookCollection _booksInProjectLibrary;
		private readonly SourceCollectionsList _sourceCollectionsesList;
		private readonly HtmlThumbNailer _thumbNailer;
		private HttpListener _listener;

		public BloomServer(CollectionSettings collectionSettings, BookCollection booksInProjectLibrary,
						   SourceCollectionsList sourceCollectionsesList, HtmlThumbNailer thumbNailer)
		{
			_collectionSettings = collectionSettings;
			_booksInProjectLibrary = booksInProjectLibrary;
			_sourceCollectionsesList = sourceCollectionsesList;
			_thumbNailer = thumbNailer;
		}

		public void Start()
		{
			_listener = new HttpListener();
			_listener.AuthenticationSchemes = AuthenticationSchemes.Anonymous;
			_listener.Prefixes.Add("http://localhost:8089/bloom/");
			//nb: had trouble with 8080. Remember to enable this with (windows 7 up): netsh http add urlacl url=http://localhost:8089/bloom user=everyone
			_listener.Start();
			_listener.BeginGetContext(new AsyncCallback(GetContextCallback), _listener);
		}


		private void GetContextCallback(IAsyncResult ar)
		{
			if (_listener == null || !_listener.IsListening)
				return; //strangely, this callback is fired when we close downn the listener

			try
			{
				HttpListenerContext context = _listener.EndGetContext(ar);
				HttpListenerRequest request = context.Request;
				MakeReply(new RequestInfo(context));
				_listener.BeginGetContext(new AsyncCallback(GetContextCallback), _listener);
			}
			catch (Exception error)
			{
				Logger.WriteEvent(error.Message);
#if DEBUG
				throw;
#endif
			}
		}

		/// <summary>
		/// This is designed to be easily unit testable by not taking actual HttpContext, but doing everything through this IRequestInfo object
		/// </summary>
		/// <param name="info"></param>
		public void MakeReply(IRequestInfo info)
		{
			var r = info.LocalPathWithoutQuery.Replace("/bloom/", "");
			r = r.Replace("library/", "");
			if (r.Contains("libraryContents"))
			{
				GetLibraryBooks(info);
			}
			else if (r == "libraryName")
			{
				info.WriteCompleteOutput(_collectionSettings.CollectionName + " Library");
			}
			else if (r.Contains("SourceCollectionsList"))
			{
				GetStoreBooks(info);
			}
			else if(r.StartsWith("thumbnails/"))
			{
				r = r.Replace("thumbnails/", "");
				r = r.Replace("%5C", "/");
				r = r.Replace("%20", " ");
				if (File.Exists(r))
				{
					info.ReplyWithImage(r);
				}
			}
			else if (r.EndsWith(".png"))
			{
				info.ContentType = "image/png";

				r = r.Replace("thumbnail", "");
				//if (r.Contains("thumb"))
				{
					if (File.Exists(r))
					{
						info.ReplyWithImage(r);
					}
					else
					{
						var imgPath = FileLocator.GetFileDistributedWithApplication("BloomBrowserUI", "book.png");
						info.ReplyWithImage(imgPath);
						//book.GetThumbNailOfBookCoverAsync(book.Type != Book.Book.BookType.Publication,image => RefreshOneThumbnail(book, image));
					}
				}
//				else
//				{
//					var imgPath = FileLocator.GetFileDistributedWithApplication("root", "ui", "book.png");
//					info.ReplyWithImage(imgPath);
//				}
			}
			else
			{
				string path = FileLocator.GetFileDistributedWithApplication("BloomBrowserUI", r);


				//request.QueryString.GetValues()
				info.WriteCompleteOutput(File.ReadAllText(path));
			}
		}

		private void GetStoreBooks(IRequestInfo info)
		{
			//enhance: it will eventually work better to do sorting client-side, according to user's current prefs
			var reply = new StringBuilder();
			var list = new List<BookCollection>();
			list.AddRange(_sourceCollectionsesList.GetSourceCollections());

			list.Sort(CompareBookCollections);

			foreach (BookCollection collection in list)
			{
				reply.AppendFormat("<li class='collectionGroup'><h2>{0}</h2><ul class='collection'>", collection.Name);
				reply.Append(GetBookListItems(collection.GetBookInfos()));
				reply.AppendFormat(@"</ul></li>");
			}
			info.WriteCompleteOutput(reply.ToString());
		}

		private void GetLibraryBooks(IRequestInfo info)
		{
			var books = _booksInProjectLibrary.GetBookInfos();
			info.WriteCompleteOutput(GetBookListItems(books));
		}

		private static string GetBookListItems(IEnumerable<Book.BookInfo> bookInfos)
		{
			var reply = new StringBuilder();
			if (bookInfos.Count() == 0)
			{
				reply.Append("<!--No Books -->");
			}
			foreach (var bookInfo in bookInfos)
			{
				//var pathRoot = Path.GetPathRoot(book.ThumbnailPath);
				//var thumbnailPath = book.ThumbnailPath.Replace(pathRoot, pathRoot.Replace(":",""));
				var thumbnailPath = "";//TODO "thumbnails/" + bookInfo.ThumbnailPath;

				reply.AppendFormat(
					@"<li class='bookLI'> <img class='book' src='{0}'></img><div class='bookTitle'>{1}</div>
						</li>",
					thumbnailPath, bookInfo.QuickTitleUserDisplay);
			}
			return reply.ToString();
		}

		public void Dispose()
		{
			if (_listener != null)
			{
				_listener.Close();
			}
			_listener = null;
		}

		private static int CompareBookCollections(BookCollection x, BookCollection y)
		{
			if (x.Name == y.Name)
				return 0;
			if (x.Name.ToLower().Contains("templates"))
				return -1;
			if (y.Name.ToLower().Contains("templates"))
				return 1;
			return 0;
		}
	}




}
