using System.Linq;
using System.Windows.Forms;
using Bloom.Api;

namespace Bloom.web.controllers
{
	class KeybordingConfigApi
	{
		public void RegisterWithServer(EnhancedImageServer server)
		{
			server.RegisterEndpointHandler("keyboarding/useLongPress", (ApiRequest request) => 
			{
				//detect if some keyboarding system is active, e.g. KeyMan. If it is, don't enable LongPress
				var form = Application.OpenForms.Cast<Form>().Last();
				request.ReplyWithText(SIL.Windows.Forms.Keyboarding.KeyboardController.IsFormUsingInputProcessor(form)?"false":"true");
			});
		}
	}
}
