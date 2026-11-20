using System;
using System.Linq;
using System.Windows.Forms;
using Bloom.Api;
using Bloom.Utils;

namespace Bloom.web.controllers
{
    class KeyboardingConfigApi
    {
        public void RegisterWithApiHandler(BloomApiHandler apiHandler)
        {
            apiHandler.RegisterEndpointHandler(
                "keyboarding/useLongpress",
                (ApiRequest request) =>
                {
                    try
                    {
                        //detect if some keyboarding system is active, e.g. KeyMan. If it is, don't enable LongPress
                        // TSF keeps the active input method per thread, so ask on the form's (UI) thread.
                        // Use a MethodInvoker: WinForms calls it directly, while a Func<bool> goes
                        // through Delegate.DynamicInvoke, whose generated stub threw on ARM64 (BL-17067).
                        var form = Application.OpenForms.Cast<Form>().Last();
                        var usingInputProcessor = false;
                        form.Invoke(
                            (MethodInvoker)(
                                () =>
                                    usingInputProcessor =
                                        InputProcessorDetector.IsInputProcessorActive()
                            )
                        );
                        request.ReplyWithText(usingInputProcessor ? "false" : "true");
                    }
                    catch (Exception error)
                    {
                        request.ReplyWithText("true"); // This is arbitrary. I don't know if it's better to assume keyman, or not.
                        NonFatalProblem.Report(
                            ModalIf.None,
                            PassiveIf.All,
                            "Error checking for keyman",
                            "",
                            error
                        );
                    }
                },
                handleOnUiThread: false
            );
        }
    }
}
