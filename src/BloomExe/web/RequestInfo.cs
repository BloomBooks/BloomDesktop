// Copyright (c) 2014 SIL International
// This software is licensed under the MIT License (http://opensource.org/licenses/MIT)

using System;
using System.Collections.Specialized;
using System.Drawing;
using System.IO;
using System.Net;
using System.Text;

namespace Bloom.web
{
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
			WriteOutput(Encoding.UTF8.GetBytes(s), _actualContext.Response);
		}

		private static void WriteOutput(byte[] buffer, HttpListenerResponse response)
		{
			response.ContentLength64 += buffer.Length;
			Stream output = response.OutputStream;
			output.Write(buffer, 0, buffer.Length);
			output.Close();
		}

		public void ReplyWithFileContent(string path)
		{
			var buffer = new byte[1024 * 512]; //512KB
			var lastModified = File.GetLastWriteTimeUtc(path).ToString("R");

			using (var fs = File.OpenRead(path))
			{
				_actualContext.Response.ContentLength64 = fs.Length;

				// A HEAD request (rather than a GET or POST request) is a request for just headers, and nothing can be written
				// to the OutputStream. It is normally used to check if the contents of the file have changed without taking the
				// time and bandwidth needed to download the full contents of the file. The 2 pieces of information being returned
				// are the Content-Length and Last-Modified headers. The requestor can use this information to determine if the
				// contents of the file have changed, and if they have changed the requestor can then decide if the file needs to
				// be reloaded. It is useful when debugging with tools which automatically reload the page when something changes.
				if (_actualContext.Request.HttpMethod == "HEAD")
				{
					// Originally we were returning the Last-Modified header with every response, but we discovered that this was
					// causing Geckofx to cache the contents of the files. This made debugging difficult because, even if the file
					// changed, Geckofx would use the cached file rather than requesting the updated file from the localhost.
					_actualContext.Response.AppendHeader("Last-Modified", lastModified);
				}
				else
				{
					int read;
					while ((read = fs.Read(buffer, 0, buffer.Length)) > 0)
						_actualContext.Response.OutputStream.Write(buffer, 0, read);
				}

			}

			_actualContext.Response.OutputStream.Close();
		}

		public void ReplyWithImage(string path)
		{
			var isJPEG = !path.EndsWith(".png");

			_actualContext.Response.ContentType = isJPEG ? "image/png" : "image/jpeg";

			if (Palaso.PlatformUtilities.Platform.IsMono)
			{
				ReplyWithFileContent(path);
				return;
			}

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

		public NameValueCollection GetQueryString()
		{
			return _actualContext.Request.QueryString;
		}

		public NameValueCollection GetPostData()
		{
			var request = _actualContext.Request;

			if (!request.HasEntityBody)
				return null;

			var returnVal = new NameValueCollection();

			using (var body = request.InputStream)
			{
				using (StreamReader reader = new StreamReader(body, request.ContentEncoding))
				{
					var inputString = reader.ReadToEnd();
					var pairs = inputString.Split('&');
					foreach (var pair in pairs)
					{
						var kvp = pair.Split('=');
						if (kvp.Length == 1)
							returnVal.Add(UnescapeString(kvp[0]), String.Empty);
						else
							returnVal.Add(UnescapeString(kvp[0]), UnescapeString(kvp[1]));
					}
				}
			}

			return returnVal;
		}

		private static string UnescapeString(string value)
		{
			return Uri.UnescapeDataString(value.Replace("+", " "));
		}
	}
}
