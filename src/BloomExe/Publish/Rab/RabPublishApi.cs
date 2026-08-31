using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Threading.Tasks;
using Bloom.Api;
using DesktopAnalytics;
using SIL.Reporting;

namespace Bloom.Publish.Rab
{
    /// <summary>
    /// Registers the Bloom-side API endpoints that drive the Reading App Builder prepare, build, and install workflow.
    /// </summary>
    public class RabPublishApi
    {
        private const string kApiUrlPart = "publish/rab/";
        public const string kWebSocketContext = "publish-rab";

        private readonly RabProjectService _rabProjectService;

        public RabPublishApi(RabProjectService rabProjectService)
        {
            _rabProjectService = rabProjectService;
        }

        /// <summary>
        /// Adds the App Builder endpoints to Bloom's API handler.
        /// </summary>
        public void RegisterWithApiHandler(BloomApiHandler apiHandler)
        {
            // Keep the API layer thin: deserialize/route here and let RabProjectService own the workflow rules.
            apiHandler.RegisterEndpointHandler(
                kApiUrlPart + "status",
                request => request.ReplyWithJson(_rabProjectService.GetStatus()),
                true
            );
            apiHandler.RegisterEndpointHandler(
                kApiUrlPart + "settings",
                request =>
                {
                    if (request.HttpMethod == HttpMethods.Get)
                    {
                        request.ReplyWithJson(_rabProjectService.GetAppSettings());
                        return;
                    }

                    _rabProjectService.SaveAppSettings(
                        Newtonsoft.Json.JsonConvert.DeserializeObject<RabAppSettings>(
                            request.RequiredPostJson()
                        )
                    );
                    request.PostSucceeded();
                },
                true
            );
            apiHandler.RegisterEndpointHandler(
                kApiUrlPart + "default-settings",
                request => request.ReplyWithJson(_rabProjectService.GetDefaultSettings()),
                true
            );
            apiHandler.RegisterEndpointHandler(
                kApiUrlPart + "icon-choices",
                request => request.ReplyWithJson(_rabProjectService.GetAvailableIconChoices()),
                true
            );
            apiHandler.RegisterEndpointHandler(
                kApiUrlPart + "books",
                request =>
                {
                    _rabProjectService.SaveTrackedBooks(
                        Newtonsoft.Json.JsonConvert.DeserializeObject<RabTrackedBookInfo[]>(
                            request.RequiredPostJson()
                        )
                    );
                    request.PostSucceeded();
                },
                true
            );
            apiHandler.RegisterEndpointHandler(
                kApiUrlPart + "size-estimates",
                request => request.ReplyWithJson(_rabProjectService.GetSizeEstimates()),
                true
            );
            apiHandler.RegisterEndpointHandler(
                kApiUrlPart + "reset-bloompub-cache",
                request =>
                {
                    _rabProjectService.ResetBloomPubCacheForScreenSession();
                    request.PostSucceeded();
                },
                true
            );
            apiHandler.RegisterAsyncEndpointHandler(
                kApiUrlPart + "open",
                async request =>
                {
                    await _rabProjectService.OpenInRabAndWaitForWindowAsync();
                    request.PostSucceeded();
                },
                true
            );
            apiHandler.RegisterAsyncEndpointHandler(
                kApiUrlPart + "prepare",
                async request =>
                    await RunRabOperationAsync(
                        request,
                        () => _rabProjectService.PrepareAsync(),
                        "Prepare"
                    ),
                true
            );
            apiHandler.RegisterAsyncEndpointHandler(
                kApiUrlPart + "build",
                async request =>
                    await RunRabOperationAsync(
                        request,
                        () => _rabProjectService.BuildAsync(),
                        "Build",
                        "build"
                    ),
                true
            );
            apiHandler.RegisterAsyncEndpointHandler(
                kApiUrlPart + "install",
                async request =>
                    await RunRabOperationAsync(
                        request,
                        () => _rabProjectService.InstallAsync(),
                        "Try on phone",
                        "install"
                    ),
                true
            );
        }

        /// <summary>
        /// Runs one App Builder action, reporting any failure to the user, and (when
        /// <paramref name="analyticsStage"/> is given) reporting the outcome to analytics.
        /// </summary>
        /// <param name="actionName">How the action is named to the user when it fails.</param>
        /// <param name="analyticsStage">"build" or "install"; null for prepare, which is fast and
        /// local, so it is deliberately not reported.</param>
        private async Task RunRabOperationAsync(
            ApiRequest request,
            Func<Task> operation,
            string actionName,
            string analyticsStage = null
        )
        {
            var stopwatch = Stopwatch.StartNew();
            Exception failure = null;
            try
            {
                await operation();
            }
            catch (Exception error)
            {
                failure = error;
                _rabProjectService.ReportFailure(actionName, error);
            }

            // Reported only after the operation's own outcome has been settled and shown to the
            // user, so that nothing done in the name of analytics can change what the user is
            // told about their build.
            if (analyticsStage != null)
                TrackRabAction(
                    analyticsStage,
                    failure == null ? "success" : "failed",
                    stopwatch,
                    failure
                );

            request.PostSucceeded();
        }

        /// <summary>
        /// Report how one of the long-running App Builder stages turned out. CI never runs a real
        /// Reading App Builder build, so field telemetry is this path's only continuous coverage.
        /// </summary>
        /// <param name="stage">"build" or "install".</param>
        /// <param name="result">"success" or "failed". (6.5 also reports "cancelled"; this branch
        /// has no way to cancel one of these actions, so that value never occurs here.)</param>
        /// <param name="error">The exception, when it failed. We send its type as a normalised
        /// errorKind and never the raw Gradle log, which is enormous and full of file paths.</param>
        private void TrackRabAction(
            string stage,
            string result,
            Stopwatch stopwatch,
            Exception error = null
        )
        {
            // Everything here is wrapped, not just the send: gathering the properties calls
            // GetStatus(), which reads and parses files, so it can fail on its own. Recording an
            // event must never be able to affect what it is observing.
            try
            {
                var properties = new Dictionary<string, string>
                {
                    { "stage", stage },
                    { "result", result },
                    {
                        "elapsedSeconds",
                        Math.Round(stopwatch.Elapsed.TotalSeconds, 1)
                            .ToString(CultureInfo.InvariantCulture)
                    },
                };
                if (error != null)
                    properties["errorKind"] = error.GetType().Name;
                // A status read costs a little file poking, which is nothing next to the action that
                // just finished, and it is the only place the book count and artifact size live.
                var status = _rabProjectService.GetStatus();
                if (status != null)
                {
                    properties["bookCount"] = (status.TrackedBooks?.Length ?? 0).ToString();
                    if (status.ApkSizeBytes > 0)
                        properties["apkSizeMB"] = Math.Round(
                                status.ApkSizeBytes / 1024.0 / 1024.0,
                                1
                            )
                            .ToString(CultureInfo.InvariantCulture);
                }
                Analytics.Track("Publish App", properties);
                // Logged as well as sent, because DEBUG builds construct DesktopAnalytics with
                // allowTracking:false: without this line there is no way to tell "fired but not
                // sent" from "never fired" without shipping to alpha. Passed to WriteEvent as the
                // whole message and never as a format string, so a brace arriving in an exception
                // type name cannot turn into a FormatException.
                Logger.WriteEvent(
                    $"[analytics] Publish App -- stage={stage}, result={result}"
                        + (
                            Analytics.AllowTracking
                                ? ""
                                : " (NOT SENT: tracking is off in this build)"
                        )
                );
            }
            catch (Exception e)
            {
                Logger.WriteEvent(
                    $"[analytics] FAILED to report the App Builder {stage} outcome: {e.Message}"
                );
            }
        }
    }
}
