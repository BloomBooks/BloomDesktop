using System;
using System.Linq;
using System.Windows.Forms;
using Bloom.Api;

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
                        var form = Application.OpenForms.Cast<Form>().Last();
                        request.ReplyWithText(
                            SIL.Windows.Forms.Keyboarding.KeyboardController.IsFormUsingInputProcessor(
                                form
                            )
                                ? "false"
                                : "true"
                        );
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
