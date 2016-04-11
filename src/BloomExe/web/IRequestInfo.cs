// Copyright (c) 2014 SIL International
// This software is licensed under the MIT License (http://opensource.org/licenses/MIT)

namespace Bloom.Api
{
	public enum HttpMethods
	{
		Get,
		Put,
		Post,
		Delete
	};

	public interface IRequestInfo
	{
		string LocalPathWithoutQuery { get; }
		string ContentType { set; }
		string RawUrl { get; }
		bool HaveOutput { get; }
		void WriteCompleteOutput(string s);
		void ReplyWithFileContent(string path);
		void ReplyWithImage(string path);
		void WriteError(int errorCode);
		void WriteError(int errorCode, string errorDescription);
		System.Collections.Specialized.NameValueCollection GetQueryString();
		System.Collections.Specialized.NameValueCollection GetPostDataWhenFormEncoded();
		string GetPostJson();
		HttpMethods HttpMethod { get; }
	}
}
