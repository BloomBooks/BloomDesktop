// Copyright (c) 2014 SIL International
// This software is licensed under the MIT License (http://opensource.org/licenses/MIT)

using System.IO;

namespace Bloom.Api
{
    public enum HttpMethods
    {
        Get,
        Put,
        Post,
        Delete,
        Options
    };

    public interface IRequestInfo
    {
        string LocalPathWithoutQuery { get; }
        string RequestContentType { get; }
        string ResponseContentType { set; }
        string RawUrl { get; }
        bool HaveOutput { get; }
        void WriteCompleteOutput(string s);
        void ReplyWithFileContent(string path, string originalPath = null);
        void ReplyWithStreamContent(Stream input, string responseType);
        void ReplyWithImage(string path, string originalPath = null);
        void WriteError(int errorCode);
        void WriteError(int errorCode, string errorDescription);
        System.Collections.Specialized.NameValueCollection GetQueryParameters();
        System.Collections.Specialized.NameValueCollection GetPostDataWhenFormEncoded();
        string GetPostJson();
        string GetPostString(bool unescape = true);
        HttpMethods HttpMethod { get; }
        void ExternalLinkSucceeded();
        string DoNotCacheFolder { set; }
        byte[] GetRawPostData();
        Stream GetRawPostStream();
    }
}
