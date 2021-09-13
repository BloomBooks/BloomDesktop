using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace Bloom.MiscUI
{
	class Enterprise
	{
		/// <summary>Brings up a dialog that says it requires Bloom Enterprise and provides a button to the settings</summary>
		/// <param name="windowOwner">The WinForms control that owns the resulting dialog</param>
		/// <param name="title">The title of the new window. Optional - the title isn't very discoverable at all</param>
		/// <param name="width">The width of the dialog, in pixels. Optional - defaults to 280px</param>
		/// <param name="height">The height of the dialog in pixels. Optional - defaults to 235px, which allows twice as many lines as needed for the English version of the dialog</param>
		internal static void ShowRequiresEnterpriseNotice(IWin32Window windowOwner, string title = null, int width = 280, int height = 235)
		{
			using (var dlg = new ReactDialog("requiresBloomEnterpriseBundle", null, title))
			{
				dlg.Width = width;
				dlg.Height = height;

				dlg.ShowDialog(windowOwner);
			}
		}
	}
}
