// Copyright (c) 2024 SIL International
// This software is licensed under the MIT License (http://opensource.org/licenses/MIT)

using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Text;
using System.Web;
using Microsoft.AspNetCore.Http;

namespace Bloom.Api
{
    /// <summary>
    /// Adapter that implements IRequestInfo for Kestrel/ASP.NET Core HttpContext.
    /// Phase 2.2 Implementation: Minimal implementation for API request handling.
    /// 
    /// This adapter wraps HttpContext to provide the IRequestInfo interface,
    /// allowing existing BloomApiHandler code to work with Kestrel without modification.
    /// </summary>
    public class KestrelRequestInfo : IRequestInfo
    {
        private readonly HttpContext _context;
        private NameValueCollection _queryStringList;
        private NameValueCollection _postData;
        private NameValueCollection _postDataJson;
        private string _responseContentType;
        private bool _haveOutput = false;

        public KestrelRequestInfo(HttpContext context)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
        }

        #region IRequestInfo Properties

        public string LocalPathWithoutQuery
        {
            get
            {
                var path = _context.Request.Path.Value;
                var queryStart = path.IndexOf("?", StringComparison.Ordinal);
                var urlToDecode = queryStart == -1 ? path : path.Substring(0, queryStart);
                
                // Handle URL decoding (same as original RequestInfo to avoid + sign issues)
                var pathWithoutLiteralPlusSigns = urlToDecode.Replace("+", "%2B");
                return HttpUtility.UrlDecode(pathWithoutLiteralPlusSigns);
            }
        }

        public string RequestContentType
        {
            get { return _context.Request.ContentType; }
        }

        public string ResponseContentType
        {
            get { return _responseContentType; }
            set { 
                _responseContentType = value;
                _context.Response.ContentType = value;
            }
        }

        public string RawUrl
        {
            get 
            { 
                var path = _context.Request.Path.Value;
                var queryString = _context.Request.QueryString.Value;
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
                return _context.Request.Method.ToUpperInvariant() switch
                {
                    "GET" => HttpMethods.Get,
                    "POST" => HttpMethods.Post,
                    "PUT" => HttpMethods.Put,
                    "DELETE" => HttpMethods.Delete,
                    "OPTIONS" => HttpMethods.Options,
                    _ => HttpMethods.Get
                };
            }
        }

        #endregion

        #region IRequestInfo Methods

        public void WriteCompleteOutput(string s)
        {
            if (_haveOutput)
                throw new InvalidOperationException("Output already written");

            _haveOutput = true;
            _context.Response.Body.Write(Encoding.UTF8.GetBytes(s));
        }

        public void ReplyWithFileContent(string path, string originalPath = null)
        {
            if (_haveOutput)
                throw new InvalidOperationException("Output already written");

            try
            {
                if (!File.Exists(path))
                {
                    WriteError(404);
                    return;
                }

                var fileBytes = File.ReadAllBytes(path);
                var contentType = GetContentType(path);
                _context.Response.ContentType = contentType;
                _context.Response.Body.Write(fileBytes, 0, fileBytes.Length);
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

            _context.Response.ContentType = responseType;
            input.CopyTo(_context.Response.Body);
            _haveOutput = true;
        }

        public void ReplyWithImage(string path, string originalPath = null)
        {
            if (_haveOutput)
                throw new InvalidOperationException("Output already written");

            try
            {
                if (!File.Exists(path))
                {
                    WriteError(404);
                    return;
                }

                var fileBytes = File.ReadAllBytes(path);
                var contentType = GetContentType(path);
                _context.Response.ContentType = contentType;
                _context.Response.Headers.Add("Cache-Control", "max-age=2592000"); // 30 days
                _context.Response.Body.Write(fileBytes, 0, fileBytes.Length);
                _haveOutput = true;
            }
            catch (Exception ex)
            {
                WriteError(500, ex.Message);
            }
        }

        public void WriteError(int errorCode)
        {
            WriteError(errorCode, null);
        }

        public void WriteError(int errorCode, string errorDescription)
        {
            _context.Response.StatusCode = errorCode;
            _haveOutput = true;

            if (!string.IsNullOrEmpty(errorDescription))
            {
                var errorBytes = Encoding.UTF8.GetBytes(errorDescription);
                _context.Response.Body.Write(errorBytes, 0, errorBytes.Length);
            }
        }

        public void WriteRedirect(string url, bool permanent)
        {
            if (_haveOutput)
                throw new InvalidOperationException("Output already written");

            _context.Response.StatusCode = permanent ? 301 : 302;
            _context.Response.Headers.Add("Location", url);
            _haveOutput = true;
        }

        public NameValueCollection GetQueryParameters()
        {
            if (_queryStringList == null)
            {
                _queryStringList = new NameValueCollection();
                foreach (var key in _context.Request.Query.Keys)
                {
                    var values = _context.Request.Query[key];
                    foreach (var value in values)
                    {
                        _queryStringList.Add(key, value);
                    }
                }
            }
            return _queryStringList;
        }

        public NameValueCollection GetPostDataWhenFormEncoded()
        {
            if (_postData == null)
            {
                _postData = new NameValueCollection();
                
                if (_context.Request.ContentType?.Contains("application/x-www-form-urlencoded") == true)
                {
                    _context.Request.Form.TryGetValue(string.Empty, out var dummy);
                    foreach (var key in _context.Request.Form.Keys)
                    {
                        var values = _context.Request.Form[key];
                        foreach (var value in values)
                        {
                            _postData.Add(key, value);
                        }
                    }
                }
            }
            return _postData;
        }

        public NameValueCollection GetPostDataWhenSimpleJsonEncoded()
        {
            // For Phase 2.2, this is a placeholder
            // Full implementation would parse JSON from POST body
            if (_postDataJson == null)
            {
                _postDataJson = new NameValueCollection();
            }
            return _postDataJson;
        }

        public string GetPostJson()
        {
            // Read JSON from POST body
            using (var reader = new StreamReader(_context.Request.Body, Encoding.UTF8, true, 1024, true))
            {
                return reader.ReadToEnd();
            }
        }

        public string GetPostString(bool unescape = true)
        {
            using (var reader = new StreamReader(_context.Request.Body, Encoding.UTF8, true, 1024, true))
            {
                var content = reader.ReadToEnd();
                if (unescape)
                {
                    content = HttpUtility.UrlDecode(content);
                }
                return content;
            }
        }

        public void ExternalLinkSucceeded()
        {
            // Placeholder for Phase 2.2
            // Would record analytics about external links
        }

        public string DoNotCacheFolder
        {
            set 
            { 
                // Placeholder for Phase 2.2
                // Would configure caching behavior
            }
        }

        public byte[] GetRawPostData()
        {
            using (var ms = new MemoryStream())
            {
                _context.Request.Body.CopyTo(ms);
                return ms.ToArray();
            }
        }

        public Stream GetRawPostStream()
        {
            return _context.Request.Body;
        }

        #endregion

        #region Private Helpers

        private string GetContentType(string filePath)
        {
            var ext = Path.GetExtension(filePath).ToLowerInvariant();
            return ext switch
            {
                ".html" => "text/html",
                ".htm" => "text/html",
                ".css" => "text/css",
                ".js" => "application/javascript",
                ".json" => "application/json",
                ".jpg" => "image/jpeg",
                ".jpeg" => "image/jpeg",
                ".png" => "image/png",
                ".gif" => "image/gif",
                ".svg" => "image/svg+xml",
                ".ico" => "image/x-icon",
                ".pdf" => "application/pdf",
                ".txt" => "text/plain",
                ".xml" => "application/xml",
                _ => "application/octet-stream"
            };
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            // HttpContext is managed by ASP.NET Core, so we don't dispose it
            // But we can clear our state
            _queryStringList = null;
            _postData = null;
            _postDataJson = null;
        }

        #endregion
    }
}
