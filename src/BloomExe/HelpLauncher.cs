using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using Bloom.Api;
using SIL.IO;

namespace Bloom
{
	public class HelpLauncher
	{
		public static void Show(Control parent)
		{
			Help.ShowHelp(parent, FileLocator.GetFileDistributedWithApplication("Bloom.chm"));
		}
		public static void Show(Control parent, string topic)
		{
			Show(parent, "Bloom.chm", topic);
		}

		public static void Show(Control parent, string helpFileName, string topic)
		{
			Help.ShowHelp(parent, FileLocator.GetFileDistributedWithApplication(helpFileName), topic);
		}

		public static void RegisterWithServer(EnhancedImageServer server)
		{
			server.RegisterEndpointHandler("help/.*", (request) =>
			{
				var topic = request.LocalPath().ToLowerInvariant().Replace("api/help","");
				Show(Application.OpenForms.Cast<Form>().Last(), topic);
				//if this is called from a simple html anchor, we don't want the browser to do anything
				request.SucceededDoNotNavigate();
			}, true); // opening a form, definitely UI thread
		}
	}
}
