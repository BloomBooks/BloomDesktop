// Copyright (c) 2014 SIL International
// This software is licensed under the MIT License (http://opensource.org/licenses/MIT)
using System;

namespace Bloom.web
{
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

		public void ReplyWithFileContent(string path)
		{
			ReplyImagePath = path;
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
