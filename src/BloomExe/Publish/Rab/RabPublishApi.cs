using System;
using System.Threading.Tasks;
using Bloom.Api;

namespace Bloom.Publish.Rab
{
    public class RabPublishApi
    {
        private const string kApiUrlPart = "publish/rab/";
        public const string kWebSocketContext = "publish-rab";

        private readonly RabProjectService _rabProjectService;

        public RabPublishApi(RabProjectService rabProjectService)
        {
            _rabProjectService = rabProjectService;
        }

        public void RegisterWithApiHandler(BloomApiHandler apiHandler)
        {
            // Keep the API layer thin: deserialize/route here and let RabProjectService own the workflow rules.
            apiHandler.RegisterEndpointHandler(
                kApiUrlPart + "status",
                request => request.ReplyWithJson(_rabProjectService.GetStatus()),
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
                kApiUrlPart + "setup",
                async request =>
                    await RunRabOperationAsync(
                        request,
                        () => _rabProjectService.SetupAsync(),
                        "Setup"
                    ),
                true
            );
            apiHandler.RegisterAsyncEndpointHandler(
                kApiUrlPart + "build",
                async request =>
                    await RunRabOperationAsync(
                        request,
                        () => _rabProjectService.BuildAsync(),
                        "Build"
                    ),
                true
            );
            apiHandler.RegisterAsyncEndpointHandler(
                kApiUrlPart + "install",
                async request =>
                    await RunRabOperationAsync(
                        request,
                        () => _rabProjectService.InstallAsync(),
                        "Try on phone"
                    ),
                true
            );
        }

        private async Task RunRabOperationAsync(
            ApiRequest request,
            Func<Task> operation,
            string actionName
        )
        {
            try
            {
                await operation();
            }
            catch (Exception error)
            {
                _rabProjectService.ReportFailure(actionName, error);
            }

            request.PostSucceeded();
        }
    }
}
