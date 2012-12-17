using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using Palaso.IO;

namespace Bloom
{
	public class HelpLauncher
	{
		public static void Show(Control parent)
		{
			Help.ShowHelp(parent, FileLocator.GetFileDistributedWithApplication("Bloom.CHM"));
		}
		public static void Show(Control parent, string topic)
		{
			Show(parent, "Bloom.CHM", topic);
		}

		public static void Show(Control parent, string helpFileName, string topic)
		{
			Help.ShowHelp(parent, FileLocator.GetFileDistributedWithApplication(helpFileName), topic);
		}
	}
}
