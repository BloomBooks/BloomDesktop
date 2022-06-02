using System.Windows.Forms;
using Newtonsoft.Json;

namespace Bloom.MiscUI
{

	/// <summary>
	/// This class is intended to replace the Windows.Forms MessageBox. It's fairly rudimentary at present, but
	/// already more flexible in that it allows arbitrary button options. Also, we can use MaterialUI style,
	/// making it more consistent with our other UI, and the message can be arbitrary HTML.
	/// As yet we only support MessageBoxIcon.Warning, but this can be added to (on the Typescript side)
	/// as needed.
	/// </summary>
	public class BloomMessageBox
	{
		public static string Show(IWin32Window owner, string messageHtml, MessageBoxButton[] rightButtons, MessageBoxIcon icon = MessageBoxIcon.None)
		{
			using (var dlg = new ReactDialog("messageBoxBundle", new
			{
				messageHtml,
				rightButtonDefinitions = rightButtons,
				icon = icon.ToString().ToLowerInvariant(),
				closeWithAPICall = true

			}))
			{
				dlg.Width = 500;
				dlg.Height = 200;
				// This dialog is neater without a task bar. We don't need to be able to
				// drag it around. There's nothing left to give it one if we don't set a title
				// and remove the control box.
				dlg.ControlBox = false;
				dlg.ShowDialog(owner);
				return dlg.CloseSource;
			}
		}
	}

	public class MessageBoxButton
	{
		[JsonProperty("text")]
		public string Text;
		[JsonProperty("id")]
		public string Id;
		[JsonProperty("default")]
		public bool Default;
	}
}
