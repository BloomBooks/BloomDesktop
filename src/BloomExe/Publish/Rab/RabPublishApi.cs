using System;
using System.Threading.Tasks;
using Bloom.Api;

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
        /// LibraryPublishApi.SetParentControlsState). The publish-tool switcher on the Publish tab is
        /// blocked separately on the React side (PublishTabPane).
        /// </summary>
        private void SetWorkspaceTabsEnabled(bool enable)
        {
            _publishView?.WorkspaceView?.SetTabsEnabled(enable);
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
                        try
                        {
                            try
                            {
                                await _rabProjectService.BuildAsync();
                                succeeded = true;
                            }
                            catch (OperationCanceledException)
                                when (_rabProjectService.IsCancellationRequested)
                            {
                                _rabProjectService.ReportCancellation("Build");
                            }
                            catch (Exception error)
                            {
                                _rabProjectService.ReportFailure("Build", error);
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
                        try
                        {
                            try
                            {
                                await _rabProjectService.InstallAsync();
                                succeeded = true;
                            }
                            catch (OperationCanceledException)
                                when (_rabProjectService.IsCancellationRequested)
                            {
                                _rabProjectService.ReportCancellation("Try on phone");
                            }
                            catch (Exception error)
                            {
                                _rabProjectService.ReportFailure("Try on phone", error);
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
