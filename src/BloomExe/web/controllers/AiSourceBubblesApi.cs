using System;
using System.Net;
using System.Threading.Tasks;
using Bloom.AiSourceBubbles;
using Bloom.Api;
using Newtonsoft.Json.Linq;

namespace Bloom.web.controllers
{
    public class AiSourceBubblesApi
    {
        private const string kApiUrlPart = "aiSourceBubbles/";
        private readonly AiSourceBubblesService _aiSourceBubblesService;

        public AiSourceBubblesApi(AiSourceBubblesService aiSourceBubblesService)
        {
            _aiSourceBubblesService = aiSourceBubblesService;
        }

        /// <summary>
        /// Registers API endpoints for AI Source Bubbles.
        /// </summary>
        public void RegisterWithApiHandler(BloomApiHandler apiHandler)
        {
            apiHandler.RegisterAsyncEndpointHandler(
                kApiUrlPart + "translate",
                HandleTranslateAsync,
                false,
                false
            );
        }

        private async Task HandleTranslateAsync(ApiRequest request)
        {
            if (request.HttpMethod != HttpMethods.Post)
            {
                request.Failed(HttpStatusCode.MethodNotAllowed, "Only POST is supported.");
                return;
            }

            try
            {
                var requestJson = JObject.Parse(request.RequiredPostJson());
                var response = await _aiSourceBubblesService.TranslateAsync(
                    new AiSourceBubblesTranslateRequest
                    {
                        SourceText = requestJson["sourceText"]?.Value<string>(),
                        SourceLanguageTag = requestJson["sourceLanguageTag"]?.Value<string>(),
                    }
                );
                request.ReplyWithJson(response);
            }
            catch (ArgumentException e)
            {
                request.Failed(HttpStatusCode.BadRequest, e.Message);
            }
            catch (InvalidOperationException e)
            {
                request.Failed(HttpStatusCode.BadRequest, e.Message);
            }
        }
    }
}
