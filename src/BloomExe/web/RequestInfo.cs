// Copyright (c) 2014 SIL International
// This software is licensed under the MIT License (http://opensource.org/licenses/MIT)

using System;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;
using System.Web;
using SIL.IO;
using SIL.Reporting;


namespace Bloom.Api
{
	/// <summary>
	/// this makes it easier to test without actually going through the http listener
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
				var queryStart = RawUrl.IndexOf("?", StringComparison.Ordinal);
				var urlToDecode = queryStart == -1 ? RawUrl : RawUrl.Substring(0, queryStart);
				// this (done in fix for BL-3750) by itself caused us to lose + signs, which according to http://stackoverflow.com/a/1006074/723299 are *not* to be
				// replaced by space if they are in the "path component" of the url (hence the approach comment out below). In the "query" component,
				// plus signs do have a special meaning. So if this is correct, UrlDecode appears to be WRONG in its
				// handling of + signs. They should be treated literally, not turned into spaces.
				//no:	return HttpUtility.UrlDecode(urlToDecode);
				
				// So let's workaround that problem with UrlDecode and still do decoding on the path component:
				var pathWithoutLiteralPlusSigns = urlToDecode.Replace("+","%2B");
				return HttpUtility.UrlDecode(pathWithoutLiteralPlusSigns);

				// This uses the wrong encoding to decode the LocalPath. (BL-3750)
				// See unit test LocalPathWithoutQuery_SpecialCharactersDecodedCorrectly for example.
				//var uri = _actualContext.Request.Url;
				//return uri.LocalPath + HttpUtility.UrlDecode(uri.Fragment);
			}
		}

		public string ContentType
		{
			set { _actualContext.Response.ContentType = value; }
		}

		public string HttpMethod
		{
			get { return _actualContext.Request.HttpMethod; }
		}

		public RequestInfo(HttpListenerContext actualContext)
		{
			_actualContext = actualContext;
		}

		//used when an anchor has given us info, but we don't actually want the browser to navigate
		public void SucceededDoNotNavigate()
		{
			_actualContext.Response.StatusCode = 202; //Accepted. Request accepted but not completed yet, it will continue asynchronously.
			HaveOutput = true;
			return;
		}

		public void WriteCompleteOutput(string s)
		{
			WriteOutput(Encoding.UTF8.GetBytes(s), _actualContext.Response);
		}

		private void WriteOutput(byte[] buffer, HttpListenerResponse response)
		{
			response.ContentLength64 += buffer.Length;
			Stream output = response.OutputStream;
			output.Write(buffer, 0, buffer.Length);
			output.Close();
			HaveOutput = true;
		}

		public bool HaveOutput { get; private set; }

		public void ReplyWithFileContent(string path)
		{
			//Deal with BL-3153, where the file was still open in another thread
			FileStream fs;
			if(!RobustFile.Exists(path))
			{
				//for audio, at least, this is not really an error. We constantly are asking if audio already exists for the current segment
				//enhance: maybe audio should go through a different path, e.g. "/bloom/audio/somefile.wav"
				//then this path COULD write and error
				//Logger.WriteError("Server could not find" + path);
				_actualContext.Response.StatusCode = 404;
				return;
			}

			try
			{
				fs = RobustFile.OpenRead(path);
			}
			catch(Exception error)
			{

				Logger.WriteError("Server could not read " + path, error);
				_actualContext.Response.StatusCode = 500;
				return;
			}

			using(fs)
			{
				_actualContext.Response.ContentLength64 = fs.Length;
				_actualContext.Response.AppendHeader("PathOnDisk", HttpUtility.UrlEncode(path));
					//helps with debugging what file is being chosen

				// A HEAD request (rather than a GET or POST request) is a request for just headers, and nothing can be written
				// to the OutputStream. It is normally used to check if the contents of the file have changed without taking the
				// time and bandwidth needed to download the full contents of the file. The 2 pieces of information being returned
				// are the Content-Length and Last-Modified headers. The requestor can use this information to determine if the
				// contents of the file have changed, and if they have changed the requestor can then decide if the file needs to
				// be reloaded. It is useful when debugging with tools which automatically reload the page when something changes.
				if(_actualContext.Request.HttpMethod == "HEAD")
				{
					var lastModified = RobustFile.GetLastWriteTimeUtc(path).ToString("R");

					// Originally we were returning the Last-Modified header with every response, but we discovered that this was
					// causing Geckofx to cache the contents of the files. This made debugging difficult because, even if the file
					// changed, Geckofx would use the cached file rather than requesting the updated file from the localhost.
					_actualContext.Response.AppendHeader("Last-Modified", lastModified);
				}
				else
				{
					var buffer = new byte[1024*512]; //512KB
					int read;
					while((read = fs.Read(buffer, 0, buffer.Length)) > 0)
						_actualContext.Response.OutputStream.Write(buffer, 0, read);
				}

			}

			_actualContext.Response.OutputStream.Close();
			HaveOutput = true;
		}

		public void ReplyWithImage(string path)
		{
			var pos = path.LastIndexOf('.');
			if(pos > 0)
				_actualContext.Response.ContentType = ServerBase.GetContentType(path.Substring(pos));

			ReplyWithFileContent(path);
		}

		public void WriteError(int errorCode, string errorDescription)
		{
			_actualContext.Response.StatusCode = errorCode;
			_actualContext.Response.StatusDescription = errorDescription;
			_actualContext.Response.Close();
			HaveOutput = true;
		}

		public void WriteError(int errorCode)
		{
			WriteError(errorCode, "File not found");
		}

		/// <summary>
		/// Processes the QueryString, decoding the values if needed
		/// </summary>
		/// <returns></returns>
		public NameValueCollection GetQueryParameters()
		{
			if(_queryStringList == null)
			{
				_queryStringList = HttpUtility.ParseQueryString(this._actualContext.Request.Url.Query);
			}

			return _queryStringList;
		}

		public string GetPostJson()
		{
			 Debug.Assert(_actualContext.Request.ContentType.ToLowerInvariant().Contains("application/json"),"The backend expected this post to have content-type application/json. With Axios.Post, this happens if you just give an object as the data. Or you can add the parameter {header: {'Content-Type': 'application/json'}} to the post call.");
			return GetPostStringInner();
		}

		public string GetPostString()
		{
			Debug.Assert(_actualContext.Request.ContentType.ToLowerInvariant().Contains("text/plain"), "The backend expected this post to have content-type text/plain.");
			return GetPostStringInner();
		}

		private string GetPostStringInner()
		{
			var request = _actualContext.Request;
			if (!request.HasEntityBody)
				return string.Empty;

			using(var body = request.InputStream)
			{
				using(StreamReader reader = new StreamReader(body, request.ContentEncoding))
				{
					var inputString = reader.ReadToEnd();
					return UnescapeString(inputString);
				}
			}
		}

		public NameValueCollection GetPostDataWhenFormEncoded()
		{
			if(_postData == null)
			{
				var request = _actualContext.Request;

				if(!request.HasEntityBody)
					return null;

				_postData = new NameValueCollection();

				using(var body = request.InputStream)
				{
					using(StreamReader reader = new StreamReader(body, request.ContentEncoding))
					{
						var inputString = reader.ReadToEnd();
						var pairs = inputString.Split('&');
						foreach(var pair in pairs)
						{
							var kvp = pair.Split('=');
							if(kvp.Length == 1)
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

		HttpMethods IRequestInfo.HttpMethod
		{
			get
			{
				HttpMethods v;
				HttpMethods.TryParse(this.HttpMethod, true, out v);
				return v;
			}
		}
	}
}
