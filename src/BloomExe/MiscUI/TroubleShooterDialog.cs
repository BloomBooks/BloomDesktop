using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Bloom.MiscUI
{
	/// <summary>
	/// This is the beginnings of a dialog for troubleshooting Bloom, especially performance problems.
	/// </summary>
	public partial class TroubleShooterDialog : Form
	{
		public TroubleShooterDialog()
		{
			InitializeComponent();
		}

		public static void ShowTroubleShooter()
		{
			// Enhance: we could simplify this logic if we save these settings.
			if (TheOneInstance?.IsDisposed ?? false)
			{
				// This happens if the user closes it directly. We need a new one, but want to keep the state.
				var old = TheOneInstance;
				TheOneInstance = new TroubleShooterDialog();
				TheOneInstance.suppressPreviewCheckbox.Checked = old.suppressPreviewCheckbox.Checked;
			}
			if (TheOneInstance == null)
				TheOneInstance = new TroubleShooterDialog(); // first time
			((Control) TheOneInstance).Show();
		}

		public static void HideTroubleShooter()
		{
			if (TheOneInstance?.Visible??false)
				TheOneInstance.Hide();
		}

		public static TroubleShooterDialog TheOneInstance; // might be disposed if the user closes it

		public static bool SuppressBookPreview => TheOneInstance?.suppressPreviewCheckbox.Checked??false;
	}
}
