using SIL.Reporting;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows.Forms;

namespace Bloom.MiscUI
{
	/// <summary>
	/// A dialog whose entire content is a react control. The constructor specifies
	/// the component and module. Note that currently the component must be added to
	/// WireUpReact.ts to make things work.
	/// All the interesting content and behavior is in the tsx file of the component.
	/// The connection is through the child ReactControl, which entirely fills the dialog.
	/// </summary>
	/// <remarks>To make a Form with its title rendered in HTML draggable, the caller
	/// can (after calling the ReactDialog constructor) just modify the instance's
	/// FormBorderStyle and ControlBox properties.
	/// This class is not thread safe, we need to call any public methods from the UI thread.
	/// </remarks>
	public partial class ReactDialog : Form, IBrowserDialog
	{
		public string CloseSource { get; set; } = null;

		private static readonly List<ReactDialog> _activeDialogs = new List<ReactDialog>();

		public ReactDialog(string javascriptBundleName, string reactComponentName, string urlQueryString = "")
		{
			InitializeComponent();
			FormClosing += ReactDialog_FormClosing;
			this.reactControl1.JavascriptBundleName = javascriptBundleName;
			this.reactControl1.ReactComponentName = reactComponentName;
			this.reactControl1.UrlQueryString = urlQueryString;
			_activeDialogs.Add(this);

		}

		public static void CloseCurrentModal(string labelOfUiElementUsedToCloseTheDialog=null)
		{
			Debug.Assert(Program.RunningOnUiThread || Program.RunningUnitTests, "ReactDialog must be called on UI thread.");
			if (_activeDialogs.Count == 0)
				return;

			// Closes the current dialog.
			try
			{
				var currentDialog = _activeDialogs[_activeDialogs.Count - 1];
				// On the off chance that something triggers CloseAllReactDialogs while we're closing this one,
				// we don't need to close this one again.
				_activeDialogs.Remove(currentDialog);
				// Optionally, the caller may provide a string value in the payload.  This string can be used to determine which button/etc that initiated the close action.
				currentDialog.CloseSource = labelOfUiElementUsedToCloseTheDialog;
				currentDialog.Close();
			}
			catch (Exception ex)
			{
				Logger.WriteError(ex);
			}
		}

		public static void CloseAllReactDialogs()
		{
			Debug.Assert(Program.RunningOnUiThread || Program.RunningUnitTests, "ReactDialog must be called on UI thread.");
			while (_activeDialogs.Count > 0)
			{
				CloseCurrentModal();
			}
		}

		private void ReactDialog_FormClosing(object sender, FormClosingEventArgs e)
		{
			_activeDialogs.Remove(this);
		}
	}
}
