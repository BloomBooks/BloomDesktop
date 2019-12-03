using System;
using System.Windows.Forms;
using SIL.Windows.Forms.Progress;

namespace Bloom.MiscUI
{
	/// <summary>
	/// This is the beginnings of a dialog for troubleshooting Bloom, especially performance problems.
	/// A single instance can be launched from the Advanced page of the Settings dialog, and left running.
	/// The one instance, if any, is kept in TheOneInstance. Note that we keep it (and use the last settings)
	/// even once it is closed (and disposed).
	/// </summary>
	public partial class TroubleShooterDialog : Form
	{
		private readonly LogBox _logBox;
		public TroubleShooterDialog()
		{
			InitializeComponent();
			// I can't figure out how to get a LogBox into Designer, so the easiest thing is to
			// put something else and replace it here.
			_logBox = new LogBox();
			_logBox.Anchor = logboxPlaceholder.Anchor;
			_logBox.Location = logboxPlaceholder.Location;
			_logBox.Size = logboxPlaceholder.Size;
			_logBox.ShowDetailsMenuItem = false;
			_logBox.ShowDiagnosticsMenuItem = false;
			Controls.Remove(logboxPlaceholder);
			Controls.Add(_logBox);
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

		public static bool MakeEmptyPageThumbnails => TheOneInstance?.makeEmptyPageThumbnailsCheckbox.Checked ?? false;

		public static void Report(string message, params object[] args)
		{
			if (TheOneInstance == null || TheOneInstance.IsDisposed)
				return;
			var mainMessage = string.Format(message, args);
			TheOneInstance._logBox.WriteMessage(DateTime.Now.ToString("hh:mm:ss.fff") + ": " + mainMessage);
		}
	}
}
