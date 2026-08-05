using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading;
using Bloom.Properties;
using Bloom.WebLibraryIntegration;
using Newtonsoft.Json;
using Sentry;
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

        // A single shared HttpClient is the recommended pattern; reusing it avoids socket exhaustion.
        private static HttpClient s_httpClient = new HttpClient();

        // Test seam: lets tests inject an HttpClient backed by a fake handler.
        internal static void SetHttpClientForTests(HttpClient client)
        {
            s_httpClient = client;
        }

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
        /// Prefer providing it: as well as not blocking the caller, the background retrieval is given
        /// a far more generous timeout, so it is much more likely to actually get the live URLs on a
        /// slow connection. A blocking call has to give up quickly to avoid freezing the UI.
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
                    if (
                        TryGetUrlDataFromServer(inBackground: false)
                        && s_liveUrlCache.TryGetValue(urlType, out url)
                    )
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
                            TryGetUrlDataFromServer(inBackground: true)
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

        // Timeouts for retrieving the URL data from the server. There are two per case because
        // they cover different things: the "attempt" one becomes the S3 client's Timeout, which
        // bounds only connecting and receiving the response headers of a single attempt; the
        // "overall" one bounds the entire operation - every attempt, the SDK's backoff between
        // them, and reading the response body - and so is the real ceiling on how long we take.

        // When a caller needs the answer now, the calling thread is frozen until we return, and
        // it is often the UI thread (e.g. a Help menu item). So give up quickly and use the
        // fallback URL rather than making Bloom look hung. Note that the retry here only helps
        // for failures that come back fast (DNS failure, connection refused); if the first
        // attempt actually times out, the overall timeout cancels us before a second one could
        // achieve anything. That's the intended tradeoff: responsiveness over success rate.
        private static readonly TimeSpan kBlockingAttemptTimeout = TimeSpan.FromSeconds(2.5);
        private static readonly TimeSpan kBlockingOverallTimeout = TimeSpan.FromSeconds(3);
        private const int kBlockingMaxRetries = 1;

        // Nothing is waiting on a background retrieval: the caller already has a fallback URL and
        // will get the real one later through its acceptFinalUrl callback. So here we can be
        // patient enough to succeed even on a slow or flaky connection. Succeeding matters for
        // more than the URLs: failure sets _internetAvailable false for the rest of the run,
        // which switches off other online features, and reports to Sentry. Three attempts of 10
        // seconds each leaves plenty of room inside the 40 second ceiling for the SDK's backoff
        // between them, so all three can actually run.
        private static readonly TimeSpan kBackgroundAttemptTimeout = TimeSpan.FromSeconds(10);
        private static readonly TimeSpan kBackgroundOverallTimeout = TimeSpan.FromSeconds(40);
        private const int kBackgroundMaxRetries = 2;

        /// <summary>
        /// Retrieve the current URLs from the server and cache them all.
        /// </summary>
        /// <param name="inBackground">True when nothing is waiting on the result, so we can
        /// afford to be patient. False when a thread (often the UI thread) is blocked until we
        /// return, in which case we give up quickly and let the caller use a fallback URL.</param>
        private static bool TryGetUrlDataFromServer(bool inBackground)
        {
            // Once the internet has been found missing, don't bother trying it again for the duration of the program.
            // And if we got the data once, it's very unlikely we'll get something new by trying again.
            if (!_internetAvailable || _gotJsonFromServer)
                return false;
            // Two threads can get here at once and both send the query. That used to be described as
            // pathological, on the grounds that the one startup call would have set _gotJsonFromServer
            // within a few seconds; but now that the background retrieval is allowed up to
            // kBackgroundOverallTimeout, the window in which a blocking caller can arrive and find the
            // cache still empty is much wider. So it is worth knowing what happens: the blocking caller
            // sends its own second request rather than waiting on the one already in flight, and gets
            // its own (short) budget. No great harm done - they both put the same values into the
            // dictionary, and AddOrUpdate below keeps whichever arrived first - and it does mean a
            // blocking caller is never made to wait for the patient budget. Still not worth setting up
            // locks to guarantee only one thread sends the request.
            var attemptTimeout = inBackground ? kBackgroundAttemptTimeout : kBlockingAttemptTimeout;
            var overallTimeout = inBackground ? kBackgroundOverallTimeout : kBlockingOverallTimeout;
            var maxRetries = inBackground ? kBackgroundMaxRetries : kBlockingMaxRetries;
            try
            {
                using (var s3Client = new BloomS3Client(null))
                {
                    s3Client.Timeout = attemptTimeout;
                    s3Client.MaxErrorRetry = maxRetries;
                    // Timeout (above) only covers connecting and receiving the response headers;
                    // this overall timeout also covers reading the (small) response body and any
                    // retries, so a stalled connection can't leave us waiting forever.
                    // See BloomS3Client.DownloadFile.
                    var jsonContent = s3Client.DownloadFile(
                        BloomS3Client.BloomDesktopFiles,
                        kUrlLookupFileName,
                        overallTimeout
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
                    // We just talked to the server, so we know the internet is available. Say so
                    // even if something already concluded otherwise: while a patient background
                    // retrieval is running, a blocking one can time out and set this false, and
                    // we don't want that stale verdict switching off online features all session.
                    _internetAvailable = true;
                    return true; // we did the retrieval, it's worth checking the dictionary again.
                }
            }
            catch (Exception e)
            {
                _internetAvailable = false;
                var mode = ModeName(inBackground);
                // Invariant culture so this reads the same as the urlLookupBudgetSeconds tag
                // (see DescribeFailureForSentry) rather than picking up a comma decimal
                // separator on some machines and disagreeing with it.
                var msg =
                    $"Exception while attempting to get URL data from server ({mode} retrieval, {BudgetSecondsText(overallTimeout)}s budget)";
                Logger.WriteEvent($"{msg}: {e.Message}");
                NonFatalProblem.ReportSentryOnly(
                    e,
                    msg,
                    configureScope: scope =>
                        DescribeFailureForSentry(scope, mode, overallTimeout, e)
                );
            }
            return false;
        }

        /// <summary>
        /// The name we use, in logging and in Sentry, for which of the two time budgets was in force.
        /// </summary>
        internal static string ModeName(bool inBackground)
        {
            return inBackground ? "background" : "blocking";
        }

        /// <summary>
        /// Label a failed retrieval so that we can tell the three interesting cases apart in Sentry.
        /// The fingerprint is what does the real work, because it is what decides how events are
        /// grouped into issues; left to Sentry's default grouping, by exception and stack, our two
        /// cases would be indistinguishable from each other and from pre-BL-16575 events:
        /// - Events from before this change stay in the old issues (BLOOM-DESKTOP-ERZ / -2H2), and
        ///   nothing new can join them, since those were grouped by the default and these are not.
        ///   So anything appearing there is by definition from an older version of Bloom.
        /// - A "blocking" failure means a synchronous lookup lost the short race. It should be rare,
        ///   because startup normally fills the cache long before anything asks synchronously.
        /// - A "background" failure means the internet looked available and yet we could not fetch a
        ///   small file within the whole patient budget. That is genuinely surprising and worth
        ///   investigating, so it must never be buried among the routine quick timeouts.
        /// The tags are what make these searchable and filterable; note that the message passed to
        /// ReportSentryOnly becomes only a breadcrumb, which Sentry neither indexes nor groups by.
        /// </summary>
        internal static void DescribeFailureForSentry(
            Scope scope,
            string mode,
            TimeSpan overallTimeout,
            Exception e
        )
        {
            scope.SetTag("urlLookupMode", mode);
            scope.SetTag("urlLookupBudgetSeconds", BudgetSecondsText(overallTimeout));
            scope.SetFingerprint("UrlLookup.TryGetUrlDataFromServer", mode, e.GetType().FullName);
        }

        /// <summary>
        /// How we render a time budget wherever we report one. Invariant culture, so that the
        /// number reads the same in the log, the Sentry breadcrumb, and the Sentry tag, on every
        /// machine - a comma decimal separator in one of the three but not the others would make
        /// them look like they were describing different things.
        /// </summary>
        private static string BudgetSecondsText(TimeSpan budget)
        {
            return budget.TotalSeconds.ToString(CultureInfo.InvariantCulture);
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

        internal static bool TestInternetConnection(string url)
        {
            try
            {
                // We only care whether we can reach the site, so don't bother downloading the body.
                // The timeout starts inside the delegate so that thread-pool queueing delay (possible
                // when the pool is busy) doesn't count against the 2500ms budget for the request itself.
                // (RunSync executes on the thread pool so we don't deadlock if called on a thread
                // with a synchronization context, e.g. the WinForms UI thread.)
                using (
                    var response = Bloom.Utils.AsyncUtil.RunSync(async () =>
                    {
                        using (var cts = new CancellationTokenSource(2500))
                            return await s_httpClient.GetAsync(
                                url,
                                HttpCompletionOption.ResponseHeadersRead,
                                cts.Token
                            );
                    })
                )
                {
                    // Treat only a "clean" response as success, matching the old HttpWebRequest behavior
                    // (which threw, and so returned false, on 4xx/5xx). Otherwise a captive portal or
                    // proxy that answers with an error/interstitial would be mistaken for real internet.
                    var code = (int)response.StatusCode;
                    return code >= 200 && code < 400;
                }
            }
            catch (Exception ex)
            {
                // Being offline (or a timeout) shows up as an exception rather than a nice failure.
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
