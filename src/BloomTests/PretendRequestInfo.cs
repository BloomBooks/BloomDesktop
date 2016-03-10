// Copyright (c) 2014 SIL International
// This software is licensed under the MIT License (http://opensource.org/licenses/MIT)
using System.Collections.Specialized;
using System.IO;
using System.Text;

namespace Bloom.web
{
	public class PretendRequestInfo : IRequestInfo
	{
		public string ReplyContents;
		public string ReplyImagePath;
		//public HttpListenerContext Context; //todo: could we mock a context and then all but do away with this pretend class by subclassing the real one?
		public long StatusCode;
		public string StatusDescription;

		public PretendRequestInfo(string url, bool forPrinting = false, bool forSrcAttr = false)
		{
			if (forPrinting)
				url = url.Replace("/bloom/", "/bloom/OriginalImages/");

			// In the real request, RawUrl does not include this prefix
			RawUrl = url.Replace("http://localhost:8089", "");

			// When JavaScript inserts a real path into the html it replaces the three magic html characters with these substitutes.
			// For this PretendRequestInfo we simulate that by doing the replace here in the url.
			if (forSrcAttr)
				url = EnhancedImageServer.SimulateJavaScriptHandlingOfHtml(url);

			// Reducing the /// emulates a behavior of the real HttpListener
			LocalPathWithoutQuery = url.Replace("http://localhost:8089", "").Replace("/bloom/OriginalImages///", "/bloom/OriginalImages/").Replace("/bloom///", "/bloom/").UnescapeCharsForHttp();
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

		// get is required to fulfil interface contract. Not currently used in tests.
		// set is required to satisfy (Team City version of) compiler for a valid auto-implemented property.
		public bool HaveOutput { get; set; }

		public void WriteCompleteOutput(string s)
		{
			var buffer = Encoding.UTF8.GetBytes(s);
			ReplyContents = Encoding.UTF8.GetString(buffer);
		}

		public void ReplyWithFileContent(string path)
		{
			ReplyImagePath = path;
			WriteCompleteOutput(File.ReadAllText(path));
		}

		public void ReplyWithImage(string path)
		{
			ReplyImagePath = path;
		}

		public void WriteError(int errorCode, string errorDescription)
		{
			StatusCode = errorCode;
			StatusDescription = errorDescription;
		}

		public void WriteError(int errorCode)
		{
			StatusCode = errorCode;
		}

		public NameValueCollection GetQueryString()
		{
			return new NameValueCollection();
		}

		public NameValueCollection GetPostData()
		{
			return new NameValueCollection();
		}

		public string GetPostJson()
		{
			return "";
		}

		public string RawUrl { get; private set; }
	}
}
