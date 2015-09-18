// Copyright (c) 2014 SIL International
// This software is licensed under the MIT License (http://opensource.org/licenses/MIT)

using System;
using System.Collections.Specialized;
using System.IO;
using System.Net;
using System.Text;
using System.Web;


namespace Bloom.web
{
	/// <summary>
	/// this makes it easier to test without actually going throught he http listener
	/// </summary>
	public class RequestInfo : IRequestInfo
	{
		private readonly HttpListenerContext _actualContext;
		private NameValueCollection _queryStringList;
		private NameValueCollection _postData;

		public string LocalPathWithoutQuery
		{
			get
			{
				// The problem with LocalPath alone is that it stops when it encounters even an
				// encoded #.  Since Bloom doesn't worry about internal addresses, and does allow
				// book titles (and thus file names) to have a # character, we need to piece together
				// the original information.  Note that LocalPath removes all Http escaping, but
				// Fragment does not.  See https://jira.sil.org/browse/BL-951 for details.
				return _actualContext.Request.Url.LocalPath + HttpUtility.UrlDecode(_actualContext.Request.Url.Fragment);
			}
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
				_actualContext.Response.AppendHeader("PathOnDisk", HttpUtility.UrlEncode(path));//helps with debugging what file is being chosen

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
			var pos = path.LastIndexOf('.');
			if (pos > 0)
				_actualContext.Response.ContentType = ServerBase.GetContentType(path.Substring(pos));

			ReplyWithFileContent(path);
		}

		public void WriteError(int errorCode, string errorDescription)
		{
			_actualContext.Response.StatusCode = errorCode;
			_actualContext.Response.StatusDescription = errorDescription;
			_actualContext.Response.Close();
		}

		public void WriteError(int errorCode)
		{
			WriteError(errorCode, "File not found");
		}

		/// <summary>
		/// Processes the QueryString, decoding the values if needed
		/// </summary>
		/// <returns></returns>
		public NameValueCollection GetQueryString()
		{
			// UrlDecode the values, if needed
			if (_queryStringList == null)
			{
				var qs = _actualContext.Request.QueryString;

				_queryStringList = new NameValueCollection();

				foreach (var key in qs.AllKeys)
				{
					var val = qs[key];
					if (val.Contains("%") || val.Contains("+"))
						val = Uri.UnescapeDataString(val.Replace('+', ' '));

					_queryStringList.Add(key, val);
				}

			}

			return _queryStringList;
		}

		public NameValueCollection GetPostData()
		{
			if (_postData == null)
			{
				var request = _actualContext.Request;

				if (!request.HasEntityBody)
					return null;

				_postData = new NameValueCollection();

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
								_postData.Add(UnescapeString(kvp[0]), String.Empty);
							else
								_postData.Add(UnescapeString(kvp[0]), UnescapeString(kvp[1]));
						}
					}
				}
			}

			return _postData;

		}

		private static string UnescapeString(string value)
		{
			return Uri.UnescapeDataString(value.Replace("+", " "));
		}

		public string RawUrl
		{
			get { return _actualContext.Request.RawUrl; }
		}
	}
}
