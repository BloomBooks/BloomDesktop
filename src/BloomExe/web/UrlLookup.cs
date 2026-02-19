using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Net;
using Bloom.Properties;
using Bloom.WebLibraryIntegration;
using Newtonsoft.Json;
using SIL.Reporting;

namespace Bloom.web
{
    // These should all match the corresponding json properties except the capitalization of the first letter
    public enum UrlType
    {
        LibrarySite,
        LibrarySiteSandbox,
        CheckForUpdates,
        UserSuggestions,
        Support,
        IssueTrackingSystem,
        IssueTrackingSystemBackend,
        LocalizingSystem,
        LastVersionForPreWindows10,
    }

    public static class ErrorLevelExtensions
    {
        public static string ToJsonPropertyString(this UrlType urlType)
        {
            string urlTypeAsString = urlType.ToString();
            return urlTypeAsString.Substring(0, 1).ToLowerInvariant()
                + urlTypeAsString.Substring(1);
        }
    }

    public static class BloomLibraryUrls
    {
        public static string BloomLibraryUrlPrefix => GetBloomLibraryUrlPrefix(false);

        public static string GetBloomLibraryUrlPrefix(bool forceUseProductionData)
        {
            return UrlLookup.LookupUrl(
                UrlType.LibrarySite,
                null,
                BookUpload.UseSandbox && !forceUseProductionData
            );
        }

        public static string BloomLibraryDetailPageUrlFromBookId(
            string bookId,
            bool myBooksBreadCrumb = false,
            bool forceUseProductionData = false
        )
        {
            return GetBloomLibraryUrlPrefix(forceUseProductionData)
                + (myBooksBreadCrumb ? "/my-books" : "")
                + "/book/"
                + bookId;
        }

        public static string BloomLibraryBooksWithMatchingIdListingUrl(
            string bookInstanceId,
            bool forceUseProductionData = false
        )
        {
            // Yep, this is ugly. We need to send "%3a" (an encoded colon) to the site because that's what it expects to make the search work.
            // But when we process this url in ExternalLinkController.HandleLink(), it will decode the url.
            // So we have to double encode it here.
            var doubleEncodedColon = "%253A";
            return $"{GetBloomLibraryUrlPrefix(forceUseProductionData)}/:search:bookInstanceId{doubleEncodedColon}{bookInstanceId}";
        }
    }

    public static class UrlLookup
    {
        //For source code (and fallback) purposes, current-services-urls.json lives in BloomExe/Resources.
        //But the live version is in S3 in the BloomS3Client.BloomDesktopFiles bucket.
        private const string kUrlLookupFileName = "current-service-urls.json";

        private static readonly ConcurrentDictionary<UrlType, string> s_liveUrlCache =
            new ConcurrentDictionary<UrlType, string>();

        private static bool _internetAvailable = true; // assume it's available to start out

        /// <summary>
        /// Look up the URL that corresponds to the specified type and params. A fallback URL may be
        /// returned if we're not online (which means it can't be used anyway).
        /// </summary>
        /// <param name="acceptFinalUrl">If this is null, and we haven't already retrieved the current URLs
        /// from the appropriate server, we'll do it now, which means this call  may take a while.
        /// If it is provided, we'll return the retrieved URL if we already have it. If not, we'll retrieve
        /// a fallback one, which in practice is going to be right unless this is an old version of Bloom
        /// and one of our main server URLs has changed for some reason. A retrieval will be started in the
        /// background, and when we get the data the correct value will be passed to acceptFinalUrl.
        /// </param>
        /// <returns></returns>
        public static string LookupUrl(
            UrlType urlType,
            Action<string> acceptFinalUrl,
            bool sandbox = false,
            bool excludeProtocolPrefix = false
        )
        {
            string fullUrl = LookupFullUrl(urlType, sandbox, acceptFinalUrl);
            if (excludeProtocolPrefix)
                return StripProtocol(fullUrl);
            return fullUrl;
        }

        private static string LookupFullUrl(
            UrlType urlType,
            bool sandbox = false,
            Action<string> acceptFinalUrl = null
        )
        {
            if (sandbox)
                urlType = GetSandboxUrlType(urlType);

            string url;
            if (s_liveUrlCache.TryGetValue(urlType, out url))
                return url;
            if (!Program.RunningUnitTests)
            {
                // (If we're running unit tests, we can go with the default URLs.
                // Otherwise, try to get the real ones, now or later.)
                if (acceptFinalUrl == null)
                {
                    // If it really is necessary, you can remove this message. It's just designed to make someone think
                    // if adding a call that might slow things down and send the query twice. If that happens, consider
                    // adding some locking to make sure the actual server query only gets sent once.
                    Debug.Fail(
                        "If at all possible, you should provide an appropriate acceptFinalUrl param when looking up a url during startup."
                    );
                    // We need the true value now. Get it.
                    if (TryGetUrlDataFromServer() && s_liveUrlCache.TryGetValue(urlType, out url))
                    {
                        return url;
                    }

                    Logger.WriteEvent("Unable to look up URL type " + urlType);
                }
                else
                {
                    // We can live with a fallback value for now, but get the real one in the background,
                    // and then deliver it.
                    var backgroundWorker = new BackgroundWorker();
                    backgroundWorker.DoWork += (sender, args) =>
                    {
                        if (
                            TryGetUrlDataFromServer()
                            && s_liveUrlCache.TryGetValue(urlType, out url)
                        )
                        {
                            acceptFinalUrl(url);
                        }
                        else
                        {
                            Logger.WriteEvent("Unable to look up URL type " + urlType);
                        }
                    };
                    backgroundWorker.RunWorkerAsync();
                }
            }

            var fallbackUrl = LookupFallbackUrl(urlType);
            Logger.WriteEvent($"Using fallback URL: {fallbackUrl}");
            return fallbackUrl;
        }

        private static UrlType GetSandboxUrlType(UrlType urlType)
        {
            switch (urlType)
            {
                case UrlType.LibrarySite:
                case UrlType.LibrarySiteSandbox:
                    return UrlType.LibrarySiteSandbox;
                default:
                    // ReSharper disable once LocalizableElement
                    throw new ArgumentOutOfRangeException(
                        "urlType",
                        urlType,
                        "There is no sandbox version for this url type."
                    );
            }
        }

        private static bool _gotJsonFromServer;

        private static bool TryGetUrlDataFromServer()
        {
            // Once the internet has been found missing, don't bother trying it again for the duration of the program.
            // And if we got the data once, it's very unlikely we'll get something new by trying again.
            if (!_internetAvailable || _gotJsonFromServer)
                return false;
            // It's pathologically possible that two threads at about the same time come here and both send
            // the query. If so, no great harm done...they'll both put the same values into the dictionary.
            // And in practice, it won't happen...one call to this, and only one, happens very early in
            // Bloom's startup code, and after that _gotJsonFromServer will be true.
            // I don't think it's worth the effort to set up locks and guarantee that only on thread
            // sends the request.
            try
            {
                using (var s3Client = new BloomS3Client(null))
                {
                    s3Client.Timeout = TimeSpan.FromMilliseconds(2500.0);
                    s3Client.ReadWriteTimeout = TimeSpan.FromMilliseconds(3000.0);
                    s3Client.MaxErrorRetry = 1;
                    var jsonContent = s3Client.DownloadFile(
                        BloomS3Client.BloomDesktopFiles,
                        kUrlLookupFileName
                    );
                    Urls urls = JsonConvert.DeserializeObject<Urls>(jsonContent);
                    // cache them all, so we don't have to repeat the server request.
                    foreach (UrlType urlType in Enum.GetValues(typeof(UrlType)))
                    {
                        var url = urls.GetUrlById(urlType.ToJsonPropertyString());
                        if (!string.IsNullOrWhiteSpace(url))
                        {
                            s_liveUrlCache.AddOrUpdate(urlType, url, (type, s) => s);
                        }
                    }
                    // Do this only after we populated the dictionary; we definitely don't want
                    // another thread to return false because it thinks things are already loaded
                    // when the value it wanted isn't in the dictionary.
                    _gotJsonFromServer = true;
                    return true; // we did the retrieval, it's worth checking the dictionary again.
                }
            }
            catch (Exception e)
            {
                _internetAvailable = false;
                var msg = $"Exception while attempting get URL data from server";
                Logger.WriteEvent($"{msg}: {e.Message}");
                NonFatalProblem.ReportSentryOnly(e, msg);
            }
            return false;
        }

        /// <summary>
        /// Check whether or not the internet is currently available.  This may delay 5 seconds if the computer
        /// is on a local network, but the internet is inaccessible. It does not check for connectivity to
        /// an Amazon or other site we actually use, though. Those could be blocked.
        /// </summary>
        /// <remarks>
        /// credit is due to http://stackoverflow.com/questions/520347/how-do-i-check-for-a-network-connection
        /// and https://forums.xamarin.com/discussion/19491/check-internet-connectivity.
        /// </remarks>
        public static bool CheckGeneralInternetAvailability(bool okToDoSlowCheckAgain)
        {
            // The next line detects whether the computer is hooked up to a local network, wired or wireless.
            // If it's not on a network at all, we know the Internet isn't available!
            var networkConnected =
                System.Net.NetworkInformation.NetworkInterface.GetIsNetworkAvailable();
            if (!networkConnected)
            {
                _internetAvailable = false;
                return false;
            }

            if (!okToDoSlowCheckAgain && !_internetAvailable)
            {
                return false;
            }

            // Test whether we can talk to a known site of interest on the internet.  This will tell us
            // close enough whether or not the internet is available.
            // From https://www.reddit.com/r/sysadmin/comments/1f9kv4/what_are_some_public_ips_that_are_ok_to/ it's
            // not clear if it's better to use google.com or example.com. Since google is blocked in some countries,
            // I think example.com (run by the  Internet Assigned Numbers Authority) is safer.
            // If example.com fails, we should try another website: at least one tester could not access example.com
            // for no apparent reason.  It's probably safer to avoid google for this backup check to satisfy my paranoia.
            // (After all, if the site is blocked, then attempts to access it might be logged as suspicious.)
            // I chose what should be an innocuous university site that should always be available.  If neither site
            // can be contacted, then give up and say the internet isn't available.  Trying only two sites limits the
            // time waiting to 5 seconds when the internet is inaccessible but the computer is on a local network.
            _internetAvailable = TestInternetConnection("https://example.com");
            if (!_internetAvailable)
                _internetAvailable = TestInternetConnection("https://mit.edu");
            return _internetAvailable;
        }

        private static bool TestInternetConnection(string url)
        {
            try
            {
                var iNetRequest = (HttpWebRequest)WebRequest.Create(url);
                iNetRequest.Timeout = 2500;
                iNetRequest.KeepAlive = false;
                var iNetResponse = iNetRequest.GetResponse();
                iNetResponse.Close();
                return true;
            }
            catch (WebException ex)
            {
                Bloom.Utils.MiscUtils.SuppressUnusedExceptionVarWarning(ex);
                return false;
            }
        }

        /// <summary>
        /// Return the cached variable indicating Internet availability.
        /// </summary>
        public static bool FastInternetAvailable
        {
            get { return _internetAvailable; }
        }

        private static string LookupFallbackUrl(UrlType urlType)
        {
            Urls urls = JsonConvert.DeserializeObject<Urls>(Resources.CurrentServiceUrls);
            return urls.GetUrlById(urlType.ToJsonPropertyString());
        }

        private static string StripProtocol(string fullUrl)
        {
            int colonSlashSlashIndex = fullUrl.IndexOf("://", StringComparison.Ordinal);
            if (colonSlashSlashIndex < 0)
                return fullUrl;
            return fullUrl.Substring(colonSlashSlashIndex + 3);
        }
    }

    [SuppressMessage("ReSharper", "InconsistentNaming")]
    class Urls
    {
        public List<Url> urls { get; set; }

        public string GetUrlById(string id)
        {
            return urls.FirstOrDefault(u => u.id == id)?.url;
        }
    }

    [SuppressMessage("ReSharper", "InconsistentNaming")]
    class Url
    {
        public string id { get; set; }
        public string url { get; set; }
    }
}
