using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
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


	public interface IRequestInfo
		{
			string LocalPathWithoutQuery { get; }
			string ContentType { set; }
			void WriteCompleteOutput(string s);
			void ReplyWithImage(string path);
		void WriteError(int errorCode);
		}

		/// <summary>
		/// this makes it easier to test without actually going throught he http listener
		/// </summary>
		public class RequestInfo : IRequestInfo
		{
			private readonly HttpListenerContext _actualContext;

			public string LocalPathWithoutQuery
			{
				get { return _actualContext.Request.Url.LocalPath; }
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
				var isJPEG = !path.EndsWith(".png");

				_actualContext.Response.ContentType = isJPEG ? "image/png" : "image/jpeg";

				//problems around here? See: http://www.west-wind.com/weblog/posts/2006/Oct/19/Common-Problems-with-rendering-Bitmaps-into-ASPNET-OutputStream
				using (var image = Image.FromFile(path))
				{
					//				var output = _actualContext.Response.OutputStream;
					//				img.Save(output, Path.GetExtension(path)==".jpg"? ImageFormat.Jpeg : ImageFormat.Png);
					//				output.Close();

					//On Vista an XP, I would get a "generic GDI+ error" when I saved the image I just loaded.
					//The workaround (see about link) is to make a copy and stream that

					using (Bitmap workAroundCopy = new Bitmap(image))
					{
						if (isJPEG)
						{
							workAroundCopy.Save(_actualContext.Response.OutputStream, System.Drawing.Imaging.ImageFormat.Jpeg);
							_actualContext.Response.Close();
						}
						else //PNG's reportedly need this further special treatment:
						{
							using (MemoryStream ms = new MemoryStream())
							{
								workAroundCopy.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
								ms.WriteTo(_actualContext.Response.OutputStream);
								_actualContext.Response.Close();
							}
						}
					}
				}

				//_actualContext.Response.Close();
			}

			public void WriteError(int errorCode)
			{
				_actualContext.Response.StatusCode = errorCode;
				_actualContext.Response.StatusDescription = "File not found";
				_actualContext.Response.Close();
			}
		}

		public class PretendRequestInfo : IRequestInfo
		{
			public string ReplyContents;
			public string ReplyImagePath;
			//public HttpListenerContext Context; //todo: could we mock a context and then all but do away with this pretend class by subclassing the real one?
			public long StatusCode;

			public PretendRequestInfo(string url)
			{
				LocalPathWithoutQuery = url.Replace("http://localhost:8089", "");
			}

			public string LocalPathWithoutQuery { get; set; }

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

			public void WriteError(int errorCode)
			{
				StatusCode = errorCode;
			}
		}


}
