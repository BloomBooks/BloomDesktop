using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using Bloom.Book;
using Palaso.IO;
using Palaso.Reporting;

namespace Bloom.web
{
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
			var r = info.RawUrl.Replace("/bloom/", "");
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
						var imgPath = FileLocator.GetFileDistributedWithApplication("root", "book.png");
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
				string path = FileLocator.GetFileDistributedWithApplication("root", r);


				//request.QueryString.GetValues()
				info.WriteCompleteOutput(File.ReadAllText(path));
			}
		}

		private void GetStoreBooks(IRequestInfo info)
		{
			//enhance: it will eventually work better to do sorting client-side, according to user's current prefs
			var reply = new StringBuilder();
			var list = new List<BookCollection>();
			list.AddRange(_sourceCollectionsesList.GetStoreCollections());

			list.Sort(CompareBookCollections);

			foreach (BookCollection collection in list)
			{
				reply.AppendFormat("<li class='collectionGroup'><h2>{0}</h2><ul class='collection'>", collection.Name);
				reply.Append(GetBookListItems(collection.GetBooks()));
				reply.AppendFormat(@"</ul></li>");
			}
			info.WriteCompleteOutput(reply.ToString());
		}

		private void GetLibraryBooks(IRequestInfo info)
		{
			var books = _booksInProjectLibrary.GetBooks();
			info.WriteCompleteOutput(GetBookListItems(books));
		}

		private static string GetBookListItems(IEnumerable<Book.Book> books)
		{
			var reply = new StringBuilder();
			if (books.Count() == 0)
			{
				reply.Append("<!--No Books -->");
			}
			foreach (var book in books)
			{
				//var pathRoot = Path.GetPathRoot(book.ThumbnailPath);
				//var thumbnailPath = book.ThumbnailPath.Replace(pathRoot, pathRoot.Replace(":",""));
				var thumbnailPath = "thumbnails/"+book.ThumbnailPath;

				reply.AppendFormat(
					@"<li class='bookLI'> <img class='book' src='{0}'></img><div class='bookTitle'>{1}</div>
						</li>",
					thumbnailPath, book.Title);
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


	public interface IRequestInfo
		{
			string RawUrl { get; }
			string ContentType { set; }
			void WriteCompleteOutput(string s);
			void ReplyWithImage(string path);
		}

		/// <summary>
		/// this makes it easier to test without actually going throught he http listener
		/// </summary>
		public class RequestInfo : IRequestInfo
		{
			private readonly HttpListenerContext _actualContext;

			public string RawUrl
			{
				get { return _actualContext.Request.RawUrl; }
			}

			public string ContentType
			{
				set { _actualContext.Response.ContentType = value; }
			}

			public RequestInfo(HttpListenerContext actualContext)
			{
				_actualContext = actualContext;
			}

			public void WriteCompleteOutput(string s)
			{
				WriteOutput(s, _actualContext.Response);
			}

			private static void WriteOutput(string responseString, HttpListenerResponse response)
			{
				byte[] buffer = Encoding.UTF8.GetBytes(responseString);

				response.ContentLength64 += buffer.Length;
				Stream output = response.OutputStream;
				output.Write(buffer, 0, buffer.Length);
				output.Close();
			}

			public void ReplyWithImage(string path)
			{
				var img = Image.FromFile(path);
				var output = _actualContext.Response.OutputStream;
				img.Save(output, ImageFormat.Png);
				output.Close();
			}
		}

		public class PretendRequestInfo : IRequestInfo
		{
			public string ReplyContents;
			public string ReplyImagePath;

			public PretendRequestInfo(string url)
			{
				RawUrl = url.Replace("http://localhost:8089", "");
			}

			public string RawUrl { get; set; }

			public string ContentType { get; set; }

			/// <summary>
			/// wrap so that it is easily consumed by our standard xml unit test stuff, which can't handled fragments
			/// </summary>
			public string ReplyContentsAsXml
			{
				get { return "<root>" + ReplyContents + "</root>"; }
			}

			public void WriteCompleteOutput(string s)
			{
				ReplyContents = s;
			}

			public void ReplyWithImage(string path)
			{
				ReplyImagePath = path;
			}
		}


}
