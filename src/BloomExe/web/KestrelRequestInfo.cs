// Copyright (c) 2024 SIL International
// This software is licensed under the MIT License (http://opensource.org/licenses/MIT)

using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Web;
using Bloom.Book;
using Bloom.web;
using Bloom.web.controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Newtonsoft.Json.Linq;
using SIL.IO;
using SIL.Reporting;

namespace Bloom.Api
{
    /// <summary>
    /// Adapter that implements IRequestInfo for Kestrel/ASP.NET Core HttpContext.
    /// Phase 2.2 Implementation: Minimal implementation for API request handling.
    ///
    /// This adapter wraps HttpContext to provide the IRequestInfo interface,
    /// allowing existing BloomApiHandler code to work with Kestrel without modification.
    /// </summary>
    public class KestrelRequestInfo : IRequestInfo, IDisposable
    {
        private readonly HttpContext _context;
        private readonly HttpRequest _request;
        private readonly HttpResponse _response;
        private static readonly Uri s_dummyBaseUri = new Uri("http://localhost/");
        private NameValueCollection _queryStringList;
        private NameValueCollection _postData;
        private NameValueCollection _postDataJson;
        private string _responseContentType;
        private bool _haveOutput;
        private string _stringContent;
        private byte[] _rawPostData;
        private string _doNotCacheFolder;

        public KestrelRequestInfo(HttpContext context)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _request = context.Request;
            _response = context.Response;
        }

        #region IRequestInfo Properties

        public string LocalPathWithoutQuery
        {
            get
            {
                var rawUrl = RawUrl;
                var queryStart = rawUrl.IndexOf("?", StringComparison.Ordinal);
                var urlToDecode = queryStart == -1 ? rawUrl : rawUrl.Substring(0, queryStart);
                var pathWithoutLiteralPlusSigns = urlToDecode.Replace("+", "%2B");
                return HttpUtility.UrlDecode(pathWithoutLiteralPlusSigns);
            }
        }

        public string RequestContentType
        {
            get { return _request.ContentType; }
        }

        public string ResponseContentType
        {
            get { return _responseContentType; }
            set
            {
                _responseContentType = value;
                _response.ContentType = value;
            }
        }

        public string RawUrl
        {
            get
            {
                var path = _request.Path.HasValue ? _request.Path.Value : string.Empty;
                var queryString = _request.QueryString.HasValue
                    ? _request.QueryString.Value
                    : string.Empty;
                return path + queryString;
            }
        }

        public bool HaveOutput
        {
            get { return _haveOutput; }
        }

        public HttpMethods HttpMethod
        {
            get
            {
                return _request.Method.ToUpperInvariant() switch
                {
                    "GET" => HttpMethods.Get,
                    "POST" => HttpMethods.Post,
                    "PUT" => HttpMethods.Put,
                    "DELETE" => HttpMethods.Delete,
                    "OPTIONS" => HttpMethods.Options,
                    _ => HttpMethods.Get,
                };
            }
        }

        #endregion

        #region IRequestInfo Methods

        public void WriteCompleteOutput(string s)
        {
            if (_haveOutput)
                throw new InvalidOperationException("Output already written");

            WriteOutput(Encoding.UTF8.GetBytes(s));
        }

        private void WriteOutput(byte[] buffer)
        {
            var response = _response;
            response.ContentLength = (response.ContentLength ?? 0) + buffer.Length;
            response.Headers["Access-Control-Allow-Origin"] = "*";
            response.Headers["Access-Control-Allow-Headers"] = "*";

            try
            {
                response.Body.Write(buffer, 0, buffer.Length);
                response.Body.Flush();
            }
            catch (IOException e)
            {
                ReportHttpProblem(e);
            }
            _haveOutput = true;
        }

        private static void ReportHttpProblem(Exception e)
        {
            Logger.WriteEvent(
                "Could not write requested data to client: " + e.Message + e.StackTrace
            );
            Debug.WriteLine(e.Message);
        }

        public void ReplyWithFileContent(string path, string originalPath = null)
        {
            if (_haveOutput)
                throw new InvalidOperationException("Output already written");

            try
            {
                if (!RobustFile.Exists(path))
                {
                    Logger.WriteError("Server could not find " + path, new FileNotFoundException());
                    WriteError(404, "Server could not find " + path);
                    return;
                }

                using (var fs = RobustFile.OpenRead(path))
                {
                    _response.ContentType = BloomServer.GetContentType(Path.GetExtension(path));
                    _response.Headers["PathOnDisk"] = HttpUtility.UrlEncode(path);
                    _response.Headers["Access-Control-Allow-Origin"] = "*";

                    if (
                        path.EndsWith(".mp4", StringComparison.InvariantCultureIgnoreCase)
                        || path.EndsWith(".webm", StringComparison.InvariantCultureIgnoreCase)
                    )
                    {
                        _response.Headers["Accept-Ranges"] = "bytes";
                    }

                    string cacheControl = ShouldCache(path, originalPath)
                        ? "max-age=600000"
                        : "no-store";
                    _response.Headers["Cache-Control"] = cacheControl;

                    if (IsHeadRequest())
                    {
                        var lastModified = RobustFile.GetLastWriteTimeUtc(path).ToString("R");
                        _response.Headers["Last-Modified"] = lastModified;
                        _response.ContentLength = fs.Length;
                    }
                    else if (fs.Length < 2 * 1024 * 1024)
                    {
                        var buffer = new byte[fs.Length];
                        fs.Read(buffer, 0, buffer.Length);
                        _response.ContentLength = buffer.Length;
                        _response.Body.Write(buffer, 0, buffer.Length);
                        _response.Body.Flush();
                    }
                    else
                    {
                        _response.ContentLength = fs.Length;
                        var buffer = new byte[1024 * 512];
                        int read;
                        while ((read = fs.Read(buffer, 0, buffer.Length)) > 0)
                        {
                            _response.Body.Write(buffer, 0, read);
                        }
                        _response.Body.Flush();
                    }
                }

                _haveOutput = true;
            }
            catch (Exception ex)
            {
                WriteError(500, ex.Message);
            }
        }

        public void ReplyWithStreamContent(Stream input, string responseType)
        {
            if (_haveOutput)
                throw new InvalidOperationException("Output already written");

            _response.ContentType = responseType;
            var buffer = new byte[2 * 1024 * 1024];
            var length = input.Read(buffer, 0, buffer.Length);

            _response.ContentLength = length;
            _response.Headers["Access-Control-Allow-Origin"] = "*";

            if (IsHeadRequest())
            {
                var lastModified = DateTime.Now.ToString("R");
                _response.Headers["Last-Modified"] = lastModified;
            }
            else
            {
                _response.Body.Write(buffer, 0, length);
                _response.Body.Flush();
            }

            _haveOutput = true;
        }

        public void ReplyWithImage(string path, string originalPath = null)
        {
            if (!string.IsNullOrEmpty(path))
            {
                var pos = path.LastIndexOf('.');
                if (pos > 0)
                    _response.ContentType = BloomServer.GetContentType(path.Substring(pos));
            }
            ReplyWithFileContent(path, originalPath);
        }

        public void WriteError(int errorCode)
        {
            WriteError(errorCode, null);
        }

        public void WriteError(int errorCode, string errorDescription)
        {
            _response.StatusCode = errorCode;
            var sanitized = errorDescription != null ? SanitizeForAscii(errorDescription) : null;
            var feature = _context.Features.Get<IHttpResponseFeature>();
            if (feature != null && sanitized != null)
            {
                feature.ReasonPhrase = sanitized;
            }

            if (!string.IsNullOrEmpty(errorDescription))
            {
                var errorBytes = Encoding.UTF8.GetBytes(errorDescription);
                _response.Body.Write(errorBytes, 0, errorBytes.Length);
                _response.Body.Flush();
            }

            if (LocalPathWithoutQuery.ToLowerInvariant().EndsWith(".json"))
            {
                _response.ContentType = "application/json";
            }

            _haveOutput = true;
        }

        public void WriteRedirect(string url, bool permanent)
        {
            if (_haveOutput)
                throw new InvalidOperationException("Output already written");

            _response.StatusCode = permanent ? 301 : 302;
            var encodedUrl = EncodeRedirectUrl(url);
            _response.Headers["Location"] = encodedUrl;
            _response.Headers["Access-Control-Allow-Origin"] = "*";
            _haveOutput = true;
        }

        public NameValueCollection GetQueryParameters()
        {
            if (_queryStringList == null)
            {
                var rawQuery = _request.QueryString.HasValue
                    ? _request.QueryString.Value
                    : string.Empty;
                _queryStringList = HttpUtility.ParseQueryString(rawQuery ?? string.Empty);
            }
            return _queryStringList;
        }

        public NameValueCollection GetPostDataWhenFormEncoded()
        {
            Debug.Assert(
                RequestContentType != null
                    && RequestContentType.StartsWith("application/x-www-form-urlencoded")
            );
            if (_postData == null)
            {
                if (!RequestHasEntityBody())
                    return null;

                _postData = new NameValueCollection();
                var pairs = GetStringContent().Split('&');
                foreach (var pair in pairs)
                {
                    if (string.IsNullOrEmpty(pair))
                        continue;
                    var kvp = pair.Split('=');
                    if (kvp.Length == 1)
                        _postData.Add(UnescapeString(kvp[0]), string.Empty);
                    else
                        _postData.Add(UnescapeString(kvp[0]), UnescapeString(kvp[1]));
                }
            }
            return _postData;
        }

        public NameValueCollection GetPostDataWhenSimpleJsonEncoded()
        {
            if (_postDataJson == null)
            {
                Debug.Assert(
                    RequestContentType != null && RequestContentType.StartsWith("application/json")
                );

                if (!RequestHasEntityBody())
                    return null;

                _postDataJson = new NameValueCollection();
                var json = GetPostJson();
                if (!string.IsNullOrWhiteSpace(json))
                {
                    var jsonParsed = JObject.Parse(json);
                    foreach (var pair in jsonParsed)
                    {
                        _postDataJson.Add(pair.Key, pair.Value?.ToString());
                    }
                }
            }
            return _postDataJson;
        }

        public string GetPostJson()
        {
            Debug.Assert(
                RequestContentType != null
                    && RequestContentType.ToLowerInvariant().Contains("application/json"),
                "Expected content-type application/json"
            );
            return GetPostStringInner();
        }

        public string GetPostString(bool unescape = true)
        {
            Debug.Assert(
                RequestContentType != null
                    && RequestContentType.ToLowerInvariant().Contains("text/plain"),
                "Expected content-type text/plain"
            );
            return GetPostStringInner(unescape);
        }

        public void ExternalLinkSucceeded()
        {
            _response.StatusCode = 200;
            _haveOutput = true;
        }

        public string DoNotCacheFolder
        {
            set { _doNotCacheFolder = value?.Replace('\\', '/'); }
        }

        public byte[] GetRawPostData()
        {
            if (_rawPostData == null)
            {
                if (!RequestHasEntityBody())
                    return null;

                EnableBuffering();
                _request.Body.Position = 0;
                using (var ms = new MemoryStream())
                {
                    _request.Body.CopyTo(ms);
                    _rawPostData = ms.ToArray();
                }
                _request.Body.Position = 0;
            }
            return _rawPostData;
        }

        public Stream GetRawPostStream()
        {
            var data = GetRawPostData();
            if (data == null)
                return null;
            return new MemoryStream(data, writable: false);
        }

        #endregion

        #region Private Helpers

        private bool IsHeadRequest()
        {
            return string.Equals(_request.Method, "HEAD", StringComparison.OrdinalIgnoreCase);
        }

        private bool RequestHasEntityBody()
        {
            if ((_request.ContentLength ?? 0) > 0)
                return true;

            EnableBuffering();
            return _request.Body.Length > 0;
        }

        private void EnableBuffering()
        {
            if (!_request.Body.CanSeek)
            {
                _request.EnableBuffering();
            }
        }

        private string GetStringContent()
        {
            if (_stringContent == null)
            {
                if (!RequestHasEntityBody())
                    return string.Empty;

                EnableBuffering();
                _request.Body.Position = 0;
                using (
                    var reader = new StreamReader(_request.Body, Encoding.UTF8, true, 1024, true)
                )
                {
                    _stringContent = reader.ReadToEnd();
                }
                _request.Body.Position = 0;
            }
            return _stringContent;
        }

        private string GetPostStringInner(bool unescape = true)
        {
            if (!RequestHasEntityBody())
                return string.Empty;

            var stringContent = GetStringContent();
            if (unescape)
                return UnescapeString(stringContent);
            return stringContent;
        }

        private static string UnescapeString(string value)
        {
            return Uri.UnescapeDataString(value.Replace('+', ' '));
        }

        readonly HashSet<string> _cacheableExtensions = new HashSet<string>(
            new[] { ".js", ".css", ".jpg", ".jpeg", ".svg", ".png", ".woff2" }
        );

        private bool ShouldCache(string path, string originalPath)
        {
            bool bypassCache = false;
#if DEBUG
            bypassCache = true;
#endif
            if (bypassCache)
                return false;

            if (path.EndsWith(ProblemReportApi.ScreenshotName))
                return false;
            if (string.IsNullOrEmpty(_doNotCacheFolder))
                return false;

            var folderToCheck = (originalPath ?? path).Replace('\\', '/');
            if (folderToCheck.StartsWith(_doNotCacheFolder))
                return false;
            if (
                folderToCheck.StartsWith(
                    Bloom.Publish.Epub.EpubMaker.EpubExportRootFolder.Replace('\\', '/')
                )
            )
                return false;
            if (folderToCheck.Contains(PublishApi.kStagingFolder))
                return false;
            if (BookStorage.CssFilesThatAreDynamicallyUpdated.Contains(Path.GetFileName(path)))
                return false;
            if (RawUrl.StartsWith("/book-preview/"))
                return false;
            if (RawUrl.EndsWith("no-cache=true"))
                return false;

            return _cacheableExtensions.Contains(Path.GetExtension(path));
        }

        private static string EncodeRedirectUrl(string url)
        {
            if (string.IsNullOrEmpty(url))
                return url;

            if (Uri.TryCreate(url, UriKind.Absolute, out var absolute))
                return absolute.AbsoluteUri;

            if (Uri.TryCreate(s_dummyBaseUri, url, out var combined))
            {
                if (url.StartsWith("?", StringComparison.Ordinal))
                    return combined.Query + combined.Fragment;
                if (url.StartsWith("#", StringComparison.Ordinal))
                    return combined.Fragment;

                var pathAndQuery = combined.PathAndQuery;
                if (!url.StartsWith("/", StringComparison.Ordinal))
                    pathAndQuery = pathAndQuery.TrimStart('/');
                return string.Concat(pathAndQuery, combined.Fragment);
            }

            return Uri.EscapeDataString(url);
        }

        private string SanitizeForAscii(string errorDescription)
        {
            var builder = new StringBuilder();
            foreach (var ch in errorDescription)
            {
                if ((ushort)ch < 32 && ch != '\t' || (ushort)ch >= 127)
                    builder.Append("?");
                else
                    builder.Append(ch);
            }
            return builder.ToString();
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            _queryStringList = null;
            _postData = null;
            _postDataJson = null;
            _stringContent = null;
            _rawPostData = null;
        }

        #endregion
    }
}
