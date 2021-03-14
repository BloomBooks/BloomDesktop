using System;
using System.Windows.Forms;
using Bloom.web.controllers;

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
	/// FormBorderStyle and ControlBox properties
	/// </remarks>
	public partial class ReactDialog : Form, IBrowserDialog
	{
		public string CloseSource { get; set; } = null;

		public ReactDialog(string javascriptBundleName, string reactComponentName, string urlQueryString = "")
		{
			InitializeComponent();
			this.reactControl1.JavascriptBundleName = javascriptBundleName;
			this.reactControl1.ReactComponentName = reactComponentName;
			this.reactControl1.UrlQueryString = urlQueryString;
		}

		protected override void OnShown(EventArgs e)
		{
			base.OnShown(e);
			CommonApi.CurrentDialog = this; // allows common/closeReactDialog to close it.
		}

		protected override void OnClosed(EventArgs e)
		{
			base.OnClosed(e);
			CommonApi.CurrentDialog = null;
		}
	}
}
