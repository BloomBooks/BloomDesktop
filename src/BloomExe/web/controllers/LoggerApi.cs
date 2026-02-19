using Bloom.Api;
using SIL.Reporting;

namespace Bloom.web.controllers
{
    /// <summary>
    /// Handles API requests related to logging.
    /// </summary>
    public class LoggerApi
    {
        public void RegisterWithApiHandler(BloomApiHandler apiHandler)
        {
            apiHandler.RegisterEndpointHandler("logger/writeEvent", HandleLoggerWriteEvent, false);
        }

        private void HandleLoggerWriteEvent(ApiRequest request)
        {
            var eventMsg = request.RequiredPostString();
            Logger.WriteEvent(eventMsg);
            request.PostSucceeded();
        }
    }
}
