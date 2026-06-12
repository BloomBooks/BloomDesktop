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
                    if (_rabProjectService.IsActionInProgress)
                    {
                        request.Failed("A prepare/build/install action is already running.");
                        return;
                    }
                    _ = Task.Run(async () =>
                    {
                        var succeeded = false;
                        try
                        {
                            await _rabProjectService.PrepareAsync();
                            succeeded = true;
                        }
                        catch (Exception error)
                        {
                            // ReportFailure logs to the progress channel first so the error
                            // message lands in the ActionLogAccordion before the UI tears
                            // down the subscription in response to "actionComplete".
                            _rabProjectService.ReportFailure("Prepare", error);
                        }
                        _rabProjectService.SendActionCompleteEvent("prepare", succeeded);
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
                    if (_rabProjectService.IsActionInProgress)
                    {
                        request.Failed("A prepare/build/install action is already running.");
                        return;
                    }
                    _ = Task.Run(async () =>
                    {
                        var succeeded = false;
                        try
                        {
                            await _rabProjectService.BuildAsync();
                            succeeded = true;
                        }
                        catch (Exception error)
                        {
                            _rabProjectService.ReportFailure("Build", error);
                        }
                        _rabProjectService.SendActionCompleteEvent("build", succeeded);
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
                    if (_rabProjectService.IsActionInProgress)
                    {
                        request.Failed("A prepare/build/install action is already running.");
                        return;
                    }
                    _ = Task.Run(async () =>
                    {
                        var succeeded = false;
                        try
                        {
                            await _rabProjectService.InstallAsync();
                            succeeded = true;
                        }
                        catch (Exception error)
                        {
                            _rabProjectService.ReportFailure("Try on phone", error);
                        }
                        _rabProjectService.SendActionCompleteEvent("install", succeeded);
                    });
                    request.PostSucceeded();
                },
                false,
                requiresSync: false
            );
        }
    }
}
