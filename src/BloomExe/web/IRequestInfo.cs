// Copyright (c) 2014 SIL International
// This software is licensed under the MIT License (http://opensource.org/licenses/MIT)

namespace Bloom.web
{
	public interface IRequestInfo
	{
		string LocalPathWithoutQuery { get; }
		string ContentType { set; }
		void WriteCompleteOutput(string s);
		void ReplyWithFileContent(string path);
		void ReplyWithImage(string path);
		void WriteError(int errorCode);
		System.Collections.Specialized.NameValueCollection GetQueryString();
		System.Collections.Specialized.NameValueCollection GetPostData();
	}
}
