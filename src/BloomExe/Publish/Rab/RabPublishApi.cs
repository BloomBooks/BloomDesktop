using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Threading.Tasks;
using Bloom.Api;
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

        /// <summary>
        /// WebSocket event id sent when a prepare/build/install action finishes.
        /// The message payload is "{action}:success" or "{action}:failure"
        /// (e.g. "build:success").
        /// </summary>
        public const string kWebSocketEventId_ActionComplete = "actionComplete";

        private readonly RabProjectService _rabProjectService;
        private readonly PublishView _publishView;

        public RabPublishApi(RabProjectService rabProjectService, PublishView publishView)
        {
            _rabProjectService = rabProjectService;
            _publishView = publishView;
        }

        /// <summary>
        /// Enables or disables the main workspace tabs (Collections/Edit/Publish) for the duration
        /// of a prepare/build/install action. While an action runs — i.e. while its Cancel button is
        /// showing — the operation is modal: the user cannot navigate to another workspace tab until
        /// it finishes or is cancelled. Mirrors how a BloomLibrary upload locks the tabs (see
        /// LibraryPublishApi.SetParentControlsState). The switcher between publish tools on the
        /// Publish tab follows the same lock: WorkspaceView reports it to the browser as
        /// "navigationLocked" and PublishTabPane disables the other tools while it is set (BL-16654).
        /// SetTabsEnabled remains a single shared flag rather than a count, deliberately: now that the
        /// publish-tool switcher is locked too, the user cannot start a second publish operation while
        /// one is running, so two of them can no longer overlap and race to unlock the tabs.
        /// </summary>
        private void SetWorkspaceTabsEnabled(bool enable)
        {
            _publishView?.WorkspaceView?.SetTabsEnabled(enable);
        }

        /// <summary>
        /// Report how one of the long-running App Builder stages turned out.
        ///
        /// This is the only continuous coverage this path can have. Nothing under Publish/Rab has
        /// ever reported anything; the feature shipped as "initial, unpolished, experimental"; and
        /// CI never runs the real RAB build at all -- only a developer who deliberately sets
        /// BLOOM_RUN_RAB_MANUAL_TESTS=1 exercises it end to end. So whether the toolchain works on
        /// real machines, and where it breaks, is something we can otherwise only learn from
        /// support traffic. BL-16469 improved the error messages; this is how we find out which
        /// errors people actually hit.
        ///
        /// One event, "Publish App", with the stage as a property rather than three event names.
        /// And one event per stage that finishes rather than one per publish run: a build the user
        /// never goes on to install still reports its own result and its own duration, which a
        /// single furthest-stage-reached event would fold away.
        ///
        /// The prepare stage is not reported. It is fast and entirely local, so it tells us
        /// nothing about the toolchain; build and install are where a real RAB installation breaks.
        /// </summary>
        /// <param name="stage">"build" or "install".</param>
        /// <param name="result">"success", "cancelled" or "failed".</param>
        /// <param name="error">The exception, when it failed. We send its type as a normalised
        /// errorKind and never the raw Gradle log, which is enormous and full of file paths.</param>
        private void TrackRabAction(
            string stage,
            string result,
            Stopwatch stopwatch,
            Exception error = null
        )
        {
            // Everything here is wrapped, not just the send. Gathering the properties calls
            // GetStatus(), which reads and parses files, and this method is called from inside the
            // try/catch that decides whether the action succeeded -- so an I/O hiccup here would
            // have written "the build failed" to the log of a build that finished fine.
            // (BloomAnalytics.Track guards its own send; this covers the gathering above it.)
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
                BloomAnalytics.Track("Publish App", properties);
            }
            catch (Exception e)
            {
                Logger.WriteEvent(
                    $"[analytics] FAILED to report the App Builder {stage} outcome: {e.Message}"
                );
            }
        }

        /// <summary>
        /// Adds the App Builder endpoints to Bloom's API handler.
        /// </summary>
        public void RegisterWithApiHandler(BloomApiHandler apiHandler)
        {
            // Keep the API layer thin: deserialize/route here and let RabProjectService own the workflow rules.

            // Status and size reads don't need the sync lock — they're pure reads that can run
            // concurrently with a background build without corrupting shared state.
            apiHandler.RegisterEndpointHandler(
                kApiUrlPart + "status",
                request => request.ReplyWithJson(_rabProjectService.GetStatus()),
                false,
                requiresSync: false
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
                false,
                requiresSync: false
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

            // The three long-running actions (prepare, build, install) fire a background task and
            // return immediately so the sync lock is not held for their entire duration.
            // Progress updates arrive via the "publish-rab" websocket channel as before.
            // Completion is signalled via the "actionComplete" websocket event.
            apiHandler.RegisterEndpointHandler(
                kApiUrlPart + "prepare",
                request =>
                {
                    if (!_rabProjectService.TryBeginAction("prepare"))
                    {
                        request.Failed("A prepare/build/install action is already running.");
                        return;
                    }
                    // Lock the workspace tabs for the duration so the action is modal (see
                    // SetWorkspaceTabsEnabled); re-enabled in the finally below.
                    SetWorkspaceTabsEnabled(false);
                    _ = Task.Run(async () =>
                    {
                        var succeeded = false;
                        try
                        {
                            try
                            {
                                await _rabProjectService.PrepareAsync();
                                succeeded = true;
                            }
                            catch (OperationCanceledException)
                                when (_rabProjectService.IsCancellationRequested)
                            {
                                // Only a real user cancel lands here; an OperationCanceledException
                                // from library code (e.g. a download timeout) falls through to the
                                // failure handler below so it's reported and logged, not silently
                                // shown as "cancelled". ReportCancellation logs before the UI tears
                                // down the subscription.
                                _rabProjectService.ReportCancellation("Prepare");
                            }
                            catch (Exception error)
                            {
                                // ReportFailure logs to the progress channel first so the error
                                // message lands in the ActionLogAccordion before the UI tears
                                // down the subscription in response to "actionComplete".
                                _rabProjectService.ReportFailure("Prepare", error);
                            }
                        }
                        finally
                        {
                            // ClearAction before SendActionCompleteEvent so the slot stays claimed
                            // until the client is notified, preventing a status-poll in the gap
                            // from incorrectly clearing the client's busyAction via recovery logic.
                            _rabProjectService.ClearAction();
                            SetWorkspaceTabsEnabled(true);
                            _rabProjectService.SendActionCompleteEvent("prepare", succeeded);
                        }
                    });
                    request.PostSucceeded();
                },
                false,
                requiresSync: false
            );
            apiHandler.RegisterEndpointHandler(
                kApiUrlPart + "build",
                request =>
                {
                    if (!_rabProjectService.TryBeginAction("build"))
                    {
                        request.Failed("A prepare/build/install action is already running.");
                        return;
                    }
                    SetWorkspaceTabsEnabled(false);
                    _ = Task.Run(async () =>
                    {
                        var succeeded = false;
                        var stopwatch = Stopwatch.StartNew();
                        try
                        {
                            try
                            {
                                await _rabProjectService.BuildAsync();
                                succeeded = true;
                                TrackRabAction("build", "success", stopwatch);
                            }
                            catch (OperationCanceledException)
                                when (_rabProjectService.IsCancellationRequested)
                            {
                                _rabProjectService.ReportCancellation("Build");
                                TrackRabAction("build", "cancelled", stopwatch);
                            }
                            catch (Exception error)
                            {
                                _rabProjectService.ReportFailure("Build", error);
                                TrackRabAction("build", "failed", stopwatch, error);
                            }
                        }
                        finally
                        {
                            _rabProjectService.ClearAction();
                            SetWorkspaceTabsEnabled(true);
                            _rabProjectService.SendActionCompleteEvent("build", succeeded);
                        }
                    });
                    request.PostSucceeded();
                },
                false,
                requiresSync: false
            );
            apiHandler.RegisterEndpointHandler(
                kApiUrlPart + "install",
                request =>
                {
                    if (!_rabProjectService.TryBeginAction("install"))
                    {
                        request.Failed("A prepare/build/install action is already running.");
                        return;
                    }
                    SetWorkspaceTabsEnabled(false);
                    _ = Task.Run(async () =>
                    {
                        var succeeded = false;
                        var stopwatch = Stopwatch.StartNew();
                        try
                        {
                            try
                            {
                                await _rabProjectService.InstallAsync();
                                succeeded = true;
                                TrackRabAction("install", "success", stopwatch);
                            }
                            catch (OperationCanceledException)
                                when (_rabProjectService.IsCancellationRequested)
                            {
                                _rabProjectService.ReportCancellation("Try on phone");
                                TrackRabAction("install", "cancelled", stopwatch);
                            }
                            catch (Exception error)
                            {
                                _rabProjectService.ReportFailure("Try on phone", error);
                                TrackRabAction("install", "failed", stopwatch, error);
                            }
                        }
                        finally
                        {
                            _rabProjectService.ClearAction();
                            SetWorkspaceTabsEnabled(true);
                            _rabProjectService.SendActionCompleteEvent("install", succeeded);
                        }
                    });
                    request.PostSucceeded();
                },
                false,
                requiresSync: false
            );
            apiHandler.RegisterEndpointHandler(
                kApiUrlPart + "cancel",
                request =>
                {
                    _rabProjectService.RequestCancellation();
                    request.PostSucceeded();
                },
                false,
                requiresSync: false
            );
        }
    }
}
