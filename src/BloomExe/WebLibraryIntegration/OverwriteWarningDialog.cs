using System.Windows.Forms;

namespace Bloom.WebLibraryIntegration
{
	public partial class OverwriteWarningDialog : Form
	{
		public OverwriteWarningDialog()
		{
			InitializeComponent();
		}

		protected override void OnLoad(System.EventArgs e)
		{
			base.OnLoad(e);
			// Fix a display glitch on Linux with the Mono SWF implementation.  The button were half off
			// the bottom of the dialog on Linux, but fine on Windows.
			if (SIL.PlatformUtilities.Platform.IsLinux)
			{
				if (ClientSize.Height < _replaceExistingButton.Location.Y + _replaceExistingButton.Height)
				{
					var delta = ClientSize.Height - (_replaceExistingButton.Location.Y + _replaceExistingButton.Height) - 4;
					_replaceExistingButton.Location = new System.Drawing.Point(_replaceExistingButton.Location.X, _replaceExistingButton.Location.Y + delta);
				}
				if (ClientSize.Height < _cancelButton.Location.Y + _cancelButton.Height)
				{
					var delta = ClientSize.Height - (_cancelButton.Location.Y + _cancelButton.Height) - 4;
					_cancelButton.Location = new System.Drawing.Point(_cancelButton.Location.X, _cancelButton.Location.Y + delta);
				}
			}
		}
	}
}
