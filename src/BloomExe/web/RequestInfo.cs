// Copyright (c) 2014 SIL International
// This software is licensed under the MIT License (http://opensource.org/licenses/MIT)

using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Web;
using Bloom.Book;
using Bloom.web;
using Bloom.web.controllers;
using SIL.IO;
using SIL.Reporting;

namespace Bloom.Api
{
    /// <summary>
    /// this makes it easier to test without actually going through the http listener
    /// </summary>
    public class RequestInfo : IRequestInfo
    {
        private readonly IHttpListenerContext _actualContext;
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
                var pathWithoutLiteralPlusSigns = urlToDecode.Replace("+", "%2B");
                return HttpUtility.UrlDecode(pathWithoutLiteralPlusSigns);

                // This uses the wrong encoding to decode the LocalPath. (BL-3750)
                // See unit test LocalPathWithoutQuery_SpecialCharactersDecodedCorrectly for example.
                //var uri = _actualContext.Request.Url;
                //return uri.LocalPath + HttpUtility.UrlDecode(uri.Fragment);
            }
        }

        // Gets the Content Type of the REQUEST (as opposed to the response). No point having a setter for the request.
        public string RequestContentType
        {
            get => _actualContext.Request.ContentType;
        }

        // Sets the Content Type of the RESPONSE
        public string ResponseContentType
        {
            set { _actualContext.Response.ContentType = value; }
        }

        public string HttpMethod
        {
            get { return _actualContext.Request.HttpMethod; }
        }

        public RequestInfo(IHttpListenerContext context)
        {
            _actualContext = context;
        }

        //used when an anchor has given us info, but we don't actually want the browser to navigate
        public void ExternalLinkSucceeded()
        {
            _actualContext.Response.StatusCode = 200; //Completed
            HaveOutput = true;
        }

        public string DoNotCacheFolder { get; set; }

        public void WriteCompleteOutput(string s)
        {
            WriteOutput(Encoding.UTF8.GetBytes(s), _actualContext.Response);
        }

        private void WriteOutput(byte[] buffer, HttpListenerResponse response)
        {
            response.ContentLength64 += buffer.Length;
            // This is particularly useful in allowing the bloom-player used in the BloomPUB preview
            // to access the current preview book. Also allows local browsers running bloom-player
            // to access it.
            response.AppendHeader("Access-Control-Allow-Origin", "*");
            // Allows bloomlibrary.org to call the external endpoints.
            response.AppendHeader("Access-Control-Allow-Headers", "*");
            Stream output = response.OutputStream;
            try
            {
                output.Write(buffer, 0, buffer.Length);
                output.Close();
            }
            catch (HttpListenerException e)
            {
                ReportHttpListenerProblem(e);
            }
            HaveOutput = true;
        }

        private static void ReportHttpListenerProblem(HttpListenerException e)
        {
            // We may well be unable to write if, while we were gathering the data, the user switched
            // pages or something similar so that the page that requested the data is gone. This seems
            // to produce this particular exception type.
            Logger.WriteEvent(
                "Could not write requested data to JavaScript: " + e.Message + e.StackTrace
            );
            Debug.WriteLine(e.Message);
        }

        public bool HaveOutput { get; private set; }

        public void ReplyWithFileContent(string path, string originalPath = null)
        {
            //Deal with BL-3153, where the file was still open in another thread
            FileStream fs;
            if (!RobustFile.Exists(path))
            {
                // Earlier there was concern that we were coming here to look for .wav file existence, but that task
                // is now handled in the "/bloom/api/audio" endpoint. So if we get here, we're looking for a different file.
                // Besides, if we don't set HaveOutput to true (w/WriteError), we'll have other problems.
                Logger.WriteError("Server could not find" + path, new FileNotFoundException());
                WriteError(404, "Server could not find " + path);
                return;
            }

            try
            {
                fs = RobustFile.OpenRead(path);
            }
            catch (Exception error)
            {
                // Something odd happened while trying to read the file. Maybe the file is locked by another process?
                // Let's not throw an error, but we'll record it in the log.
                // BL-12237 actually had a FileNotFoundException here, in a Team Collection setting, which should
                // have been caught by the RobustFile.Exists() above. So we'll just log it and continue.
                // The important thing for avoiding a big ugly EndpointHandler error (in the case of BL-12237) is to
                // set HaveOutput to true, which WriteError() does.
                Logger.WriteError("Server could not read " + path, error);
                WriteError(500, "Server could not read " + path + ": " + error.Message);
                return;
            }

            try
            {
                _actualContext.Response.ContentLength64 = fs.Length;
                _actualContext.Response.AppendHeader("PathOnDisk", HttpUtility.UrlEncode(path));
                _actualContext.Response.AppendHeader("Access-Control-Allow-Origin", "*");
                if (path.EndsWith(".mp4", StringComparison.InvariantCultureIgnoreCase))
                {
                    // Apparently Chrome/WebView2 is more picky than Firefox about the need for an Accept-Ranges header if you're going to
                    // request a range in a file like a video (or perhaps audio). Without this extra header, setting the currentTime in
                    // SignLanguageTool.tsx setCurrentVideoPoint() doesn't work at all!
                    // https://stackoverflow.com/questions/36783521/why-does-setting-currenttime-of-html5-video-element-reset-time-in-chrome
                    _actualContext.Response.AppendHeader("Accept-Ranges", "bytes");
                }

                // 60000s is about a week...if someone spends longer editing one book, well, files will get loaded one more time...
                // When we want the browser NOT to cache, we still need to specify the "no-store" value. Otherwise, the browser may
                // impose a default that is LONGER than we want (since this is mainly to avoid stale assets during development,
                // though we also avoid caching book folder stuff in case the user is doing something like directly editing
                // images).
                // For years of GeckoFx, we were not setting the Cache-Control header at all if ShouldCache() returned true.
                // It seems we were expecting that to mean the browser wouldn't cache. Now we're not sure if we truly weren't
                // caching (due to some setting or specific behavior of Geckofx) or if we were getting lucky with the browser default.
                // When we moved to Webview2, in an attempt to solve some caching issues (too much caching),
                // we started setting the Cache-Control max-age to 10 seconds.
                // However, it was shown with several bugs (such as BL-12437, BL-12440) that this was too long for some cases.
                // We're going back to the idea that ShouldCache() == false means we don't want the browser to cache at all.
                // But now we're making it explicit by setting the Cache-Control header to "no-store".
                // A possible enhancement would be to change ShouldCache to return an enum (and change its name)
                // such that we cache for the session, a short time (1 second?), or not at all. But for now, binary is ok.
                // (At one point, we thought we wanted a short cache time for avatar images for Team Collections, but
                // those don't even go through the Bloom server.)
                string cacheControl = ShouldCache(path, originalPath)
                    ? "max-age=600000"
                    : "no-store";
                _actualContext.Response.AppendHeader("Cache-Control", cacheControl);

                // A HEAD request (rather than a GET or POST request) is a request for just headers, and nothing can be written
                // to the OutputStream. It is normally used to check if the contents of the file have changed without taking the
                // time and bandwidth needed to download the full contents of the file. The 2 pieces of information being returned
                // are the Content-Length and Last-Modified headers. The requestor can use this information to determine if the
                // contents of the file have changed, and if they have changed the requestor can then decide if the file needs to
                // be reloaded. It is useful when debugging with tools which automatically reload the page when something changes.
                if (_actualContext.Request.HttpMethod == "HEAD")
                {
                    var lastModified = RobustFile.GetLastWriteTimeUtc(path).ToString("R");

                    // Originally we were returning the Last-Modified header with every response, but we discovered that this was
                    // causing Geckofx to cache the contents of the files. This made debugging difficult because, even if the file
                    // changed, Geckofx would use the cached file rather than requesting the updated file from the localhost.
                    _actualContext.Response.AppendHeader("Last-Modified", lastModified);
                }
                else if (fs.Length < 2 * 1024 * 1024)
                {
                    // This buffer size was picked to be big enough for any of the standard files we load in every page.
                    // Profiling indicates it is MUCH faster to use Response.Close() rather than writing to the output stream,
                    // though the gain may be illusory since the final 'false' argument allows our code to proceed without waiting
                    // for the complete data transfer. At a minimum, it makes this thread available to work on another
                    // request sooner.
                    var buffer = new byte[fs.Length];
                    fs.Read(buffer, 0, (int)fs.Length);
                    // The client may not read the whole stream (e.g. paused video). I don't know whether that could lead
                    // to a delay in Close() returning; probably not. But just to be safe, make sure we aren't holding
                    // on to the file.
                    fs.Dispose();
                    fs = null;
                    _actualContext.Response.Close(buffer, false);
                }
                else
                {
                    // For really big (typically image) files, use the old buffered approach.
                    // Here we have to be careful. The client may not read the whole file content (e.g.,
                    // it may be a long video, and the user may pause it). We don't want to keep the file
                    // locked, even for read, because the user may decide to delete it.
                    try
                    {
                        var buffer = new byte[1024 * 512]; //512KB
                        int read;
                        while ((read = fs.Read(buffer, 0, buffer.Length)) > 0)
                        {
                            long pos = fs.Position;
                            fs.Dispose();
                            fs = null; // prevent double dispose
                            _actualContext.Response.OutputStream.Write(buffer, 0, read);
                            try
                            {
                                fs = RobustFile.OpenRead(path);
                            }
                            catch (FileNotFoundException)
                            {
                                // and we've made it possible to delete (or move) the file in the middle
                                // of our read, so it may be gone. If so, just pretend it ended with
                                // what we already returned.
                                break;
                            }

                            fs.Seek(pos, SeekOrigin.Begin);
                        }

                        _actualContext.Response.OutputStream.Close();
                    }
                    catch (HttpListenerException e)
                    {
                        // If the page is gone and no longer able to accept the data, just log it.
                        ReportHttpListenerProblem(e);
                    }
                }
            }
            finally
            {
                if (fs != null)
                    fs.Dispose();
            }

            HaveOutput = true;
        }

        public void ReplyWithStreamContent(Stream input, string responseType)
        {
            ResponseContentType = responseType;
            var buffer = new byte[2 * 1024 * 1024]; // hopefully plenty big enough for any resource we want to return this way
            var length = input.Read(buffer, 0, buffer.Length);

            _actualContext.Response.ContentLength64 = length;
            _actualContext.Response.AppendHeader("Access-Control-Allow-Origin", "*");

            // Any reason to cache?
            //_actualContext.Response.AppendHeader("Cache-Control",
            //	"max-age=600000"); // about a week...if someone spends longer editing one book, well, files will get loaded one more time...

            // A HEAD request (rather than a GET or POST request) is a request for just headers, and nothing can be written
            // to the OutputStream. It is normally used to check if the contents of the file have changed without taking the
            // time and bandwidth needed to download the full contents of the file. The 2 pieces of information being returned
            // are the Content-Length and Last-Modified headers. The requestor can use this information to determine if the
            // contents of the file have changed, and if they have changed the requestor can then decide if the file needs to
            // be reloaded. It is useful when debugging with tools which automatically reload the page when something changes.
            if (_actualContext.Request.HttpMethod == "HEAD")
            {
                var lastModified = DateTime.Now.ToString("R");

                // Originally we were returning the Last-Modified header with every response, but we discovered that this was
                // causing Geckofx to cache the contents of the files. This made debugging difficult because, even if the file
                // changed, Geckofx would use the cached file rather than requesting the updated file from the localhost.
                _actualContext.Response.AppendHeader("Last-Modified", lastModified);
            }
            else
            {
                _actualContext.Response.Close(buffer, false);
            }

            HaveOutput = true;
        }

        readonly HashSet<string> _cacheableExtensions = new HashSet<string>(
            new[] { ".js", ".css", ".jpg", ".jpeg", ".svg", ".png", ".woff2" }
        );

        private bool ShouldCache(string path, string originalPath)
        {
            bool bypassCache = false;
#if DEBUG
            // Developers never want caching...interferes with trying new versions of stuff.
            // So, obviously, you want to comment this line out to test caching.
            bypassCache = true;
#endif
            if (bypassCache)
                return false;

            if (path.EndsWith(ProblemReportApi.ScreenshotName))
                return false; // Otherwise we can get stale screenshot images from our ProblemReportApi
            if (string.IsNullOrEmpty(DoNotCacheFolder))
                return false; // if for some reason this hasn't been set, play safe and don't cache.
            // if we're using a lower resolution version of an image (with a generated filename),
            // we want ShouldCache to do its tests on the original filename.
            var folderToCheck = (originalPath ?? path).Replace('\\', '/');
            if (folderToCheck.StartsWith(DoNotCacheFolder))
                return false; // in the folder we never cache, typically the editable project folder.)
            if (
                folderToCheck.StartsWith(
                    Bloom.Publish.Epub.EpubMaker.EpubExportRootFolder.Replace('\\', '/')
                )
            )
                return false; // ePUB export files should not be cached.  See https://silbloom.myjetbrains.com/youtrack/issue/BL-6253.
            if (folderToCheck.Contains(PublishApi.kStagingFolder))
                return false; // Don't cache when showing BloomPUB preview
            if (BookStorage.CssFilesThatAreDynamicallyUpdated.Contains(Path.GetFileName(path)))
                return false;

            // The preview iframe uses urls like /book-preview/index.htm, which means urls
            // inside it like "src='image.jpg'" translate to something like
            // /book-preview/image.jpg, which is not book-specific.
            // If we allow these to be cached, we could use an image
            // from one book when displaying another book. And pathologically, it might not
            // actually be the same image! See BL-11239.
            // Now, the path passed to this method is of course resolved to a specific file
            // in a specific book folder. But if the browser is allowed to cache the result of asking
            // for /book-preview/Image1.jpg, it will not know that /book-preview/Image1.jpg
            // could mean something quite different in a different book.
            if (RawUrl.StartsWith("/book-preview/"))
                return false;

            if (RawUrl.EndsWith("no-cache=true"))
                return false;

            return _cacheableExtensions.Contains(Path.GetExtension(path));
        }

        public void ReplyWithImage(string path, string originalPath = null)
        {
            if (path != null)
            {
                var pos = path.LastIndexOf('.');
                if (pos > 0)
                    _actualContext.Response.ContentType = BloomServer.GetContentType(
                        path.Substring(pos)
                    );
            }
            ReplyWithFileContent(path, originalPath);
        }

        public void WriteError(int errorCode, string errorDescription)
        {
            _actualContext.Response.StatusCode = errorCode;
            // This is an area where HTTP is stuck in pre-Unicode ASCII days.
            _actualContext.Response.StatusDescription = SanitizeForAscii(errorDescription);
            // The firefox javascript engine apparently considers empty json data to be xml
            // and tries to parse it as such if we don't specify that it is actually json.
            // This happens before we even see the data in the axios.get().then().catch() code!
            // See https://issues.bloomlibrary.org/youtrack/issue/BL-7900.
            if (LocalPathWithoutQuery.ToLowerInvariant().EndsWith(".json"))
                _actualContext.Response.ContentType = "application/json";
            _actualContext.Response.Close();
            HaveOutput = true;
        }

        private string SanitizeForAscii(string errorDescription)
        {
            // Tab ("\t") is okay, but no other ASCII control characters.  Non-ASCII Unicode characters will be trashed
            // anyway (by discarding the high byte I think) if they don't cause problems, so trash them deterministically.
            // If the description is non-Roman, it will turn into all question marks, but that's life.
            var bldr = new StringBuilder();
            foreach (var ch in errorDescription.ToCharArray())
            {
                if ((ushort)ch < 32 && ch != '\t' || (ushort)ch >= 127)
                    bldr.Append("?");
                else
                    bldr.Append(ch);
            }
            return bldr.ToString();
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
            if (_queryStringList == null)
            {
                _queryStringList = HttpUtility.ParseQueryString(
                    this._actualContext.Request.Url.Query
                );
            }

            return _queryStringList;
        }

        public string GetPostJson()
        {
            Debug.Assert(
                _actualContext.Request.ContentType != null,
                "The backend expected this post to have content-type of application/json but it ContentType is null. One cause of this is that the parameter given to axios.post() is undefined."
            );
            Debug.Assert(
                _actualContext.Request.ContentType.ToLowerInvariant().Contains("application/json"),
                "The backend expected this post to have content-type application/json. With Axios.Post, this happens if you just give an object as the data. Or you can add the parameter {header: {'Content-Type': 'application/json'}} to the post call."
            );
            return GetPostStringInner();
        }

        public string GetPostString(bool unescape = true)
        {
            Debug.Assert(
                _actualContext.Request.ContentType.ToLowerInvariant().Contains("text/plain"),
                "The backend expected this post to have content-type text/plain."
            );
            return GetPostStringInner(unescape);
        }

        private string GetPostStringInner(bool unescape = true)
        {
            var request = _actualContext.Request;
            if (!request.HasEntityBody)
                return string.Empty;

            var stringContent = GetStringContent();
            if (unescape)
                return UnescapeString(stringContent);
            return stringContent;
        }

        // you can only read from the stream once. But this makes for a fragile API for this class, where
        // asking the same thing twice, you'll get null the second time. So we store the contents.
        private string _stringContent;

        private string GetStringContent()
        {
            var request = _actualContext.Request;
            if (_stringContent == null)
            {
                using (var body = request.InputStream)
                {
                    // request.ContentEncoding is set to Encoding.Default (the system's default encoding) if it's not explicity set.
                    // We almost certainly want UTF-8 instead of the system's default encoding for transport between javascript and C#.
                    // (Of course, the system's default encoding could be be UTF-8, but that's unlikely even for Windows 10 in 2022.)
                    // See https://issues.bloomlibrary.org/youtrack/issue/BL-11053.
                    using (StreamReader reader = new StreamReader(body, Encoding.UTF8))
                    {
                        _stringContent = reader.ReadToEnd();
                    }
                }
            }
            return _stringContent;
        }

        public byte[] GetRawPostData()
        {
            var request = _actualContext.Request;

            if (!request.HasEntityBody)
                return null;

            using (var input = request.InputStream)
            {
                byte[] buffer = new byte[16 * 1024];
                using (MemoryStream ms = new MemoryStream())
                {
                    int read;
                    while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        ms.Write(buffer, 0, read);
                    }

                    return ms.ToArray();
                }
            }
        }

        /// <summary>
        /// Get the data from the request as a stream. Caller is responsible to dispose of it.
        /// </summary>
        /// <returns></returns>
        public Stream GetRawPostStream()
        {
            var request = _actualContext.Request;

            if (!request.HasEntityBody)
                return null;

            return request.InputStream;
        }

        public NameValueCollection GetPostDataWhenFormEncoded()
        {
            Debug.Assert(RequestContentType.StartsWith("application/x-www-form-urlencoded"));
            if (_postData == null)
            {
                var request = _actualContext.Request;

                if (!request.HasEntityBody)
                    return null;

                _postData = new NameValueCollection();
                var pairs = GetStringContent().Split('&');
                foreach (var pair in pairs)
                {
                    var kvp = pair.Split('=');
                    if (kvp.Length == 1)
                        _postData.Add(UnescapeString(kvp[0]), String.Empty);
                    else
                        _postData.Add(UnescapeString(kvp[0]), UnescapeString(kvp[1]));
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
