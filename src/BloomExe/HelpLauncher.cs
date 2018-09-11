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
			// Help.ShowHelp() obeys the current environment, which includes LD_LIBRARY_PATH on Linux.
			// This can prevent Bloom Help from displaying web sites if firefox is not the default browser.
			// (External links do exist when viewing help, even though most links are internal to the help
			// file and not subject to this display glitch.)  So we must ensure that LD_LIBRARY_PATH is
			// cleared (its normal state) before invoking Help.ShowHelp(), then restored for the sake of
			// the rest of the program.  See https://silbloom.myjetbrains.com/youtrack/issue/BL-5993.
			var libpath = Environment.GetEnvironmentVariable("LD_LIBRARY_PATH");
			if (!String.IsNullOrEmpty(libpath))
				Environment.SetEnvironmentVariable("LD_LIBRARY_PATH", null);

			Help.ShowHelp(parent, FileLocationUtilities.GetFileDistributedWithApplication("Bloom.chm"));

			if (!String.IsNullOrEmpty(libpath))
				Environment.SetEnvironmentVariable("LD_LIBRARY_PATH", libpath);
		}

		public static void Show(Control parent, string topic)
		{
			Show(parent, "Bloom.chm", topic);
		}

		public static void Show(Control parent, string helpFileName, string topic)
		{
			// See the comments above related to BL-5993.
			var libpath = Environment.GetEnvironmentVariable("LD_LIBRARY_PATH");
			if (!String.IsNullOrEmpty(libpath))
				Environment.SetEnvironmentVariable("LD_LIBRARY_PATH", null);

			Help.ShowHelp(parent, FileLocationUtilities.GetFileDistributedWithApplication(helpFileName), topic);

			if (!String.IsNullOrEmpty(libpath))
				Environment.SetEnvironmentVariable("LD_LIBRARY_PATH", libpath);
		}

		public static void RegisterWithApiHandler(BloomApiHandler apiHandler)
		{
			apiHandler.RegisterEndpointHandler("help/.*", (request) =>
			{
				var topic = request.LocalPath().Replace("api/help","");
				Show(Application.OpenForms.Cast<Form>().Last(), topic);
				//if this is called from a simple html anchor, we don't want the browser to do anything
				request.ExternalLinkSucceeded();
			}, true); // opening a form, definitely UI thread
		}
	}
}
