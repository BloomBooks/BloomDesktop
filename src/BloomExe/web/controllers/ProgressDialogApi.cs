using System;
using Bloom.Api;
using Bloom.MiscUI;

namespace Bloom.web.controllers
{
    public class ProgressDialogApi
    {
        private static Action _cancelHandler;

        public static void SetCancelHandler(Action cancelHandler)
        {
            _cancelHandler = cancelHandler;
        }

        public void RegisterWithApiHandler(BloomApiHandler apiHandler)
        {
            apiHandler.RegisterEndpointHandler("progress/cancel", Cancel, false, false);
            apiHandler.RegisterEndpointHandler(
                "progress/closed",
                BrowserProgressDialog.HandleProgressDialogClosed,
                false
            );
            // Doesn't need sync because all it does is set a flag, which nothing else modifies.
            // Mustn't need sync because we may be processing another request (e.g., creating a TC) when we launch the dialog
            // that we want to know is ready to receive messages.
            apiHandler.RegisterEndpointHandler(
                "progress/ready",
                BrowserProgressDialog.HandleProgressReady,
                false,
                false
            );
        }

        private void Cancel(ApiRequest request)
        {
            // if it's null, and that causes a throw, well... that *is* an error situation
            _cancelHandler();
            request.PostSucceeded();
        }
    }
}
