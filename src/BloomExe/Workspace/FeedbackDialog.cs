using System;
using System.Diagnostics;
using System.Windows.Forms;
using Palaso.IO;

namespace Bloom.Workspace
{
	//we started out using the widget, and that can still be found in distfiles/feedback.html.
	//It had these problems:
	// * couldn't remember who you are, and couldn't use things like last pass
	// * the widget didn't do auto-lookup as you type
	// * the widget either steered you towards a support ticket (if I enabled that at user voice), or hid support (if I said "feedback only" on uservoice).  Whereas
	//			the forum page emphasizes ideas, but does have a support link & icon.

	public partial class FeedbackDialog : Form
	{
		public delegate FeedbackDialog Factory();//autofac uses this

		private readonly IChangeableFileLocator _fileLocator;

		public FeedbackDialog(IChangeableFileLocator fileLocator)
		{
			_fileLocator = fileLocator;
			InitializeComponent();
		}

		public void Show()
		{
			// Ideally, we would load the page in our own window, but we need to deal with the popup and maybe cookie issues:
			//		* didn't work well with google-login
			//		* geckofx9 had various errors related to popup windows (e.g. login via google), and thos popup windows were yucky anyhow

			//ShowDialog();

			//So now, we just run your browser

			Process.Start("http://bloombooks.uservoice.com/forums/153625-general");
		}

		private void FeedbackDialog_Load(object sender, EventArgs e)
		{
			_browser.Navigate("http://bloombooks.uservoice.com/forums/153625-general",false);
		}

		private void _okButton_Click(object sender, EventArgs e)
		{
			DialogResult = DialogResult.OK;
			Close();

		}


		private void _cancelButton_Click(object sender, EventArgs e)
		{
			DialogResult = DialogResult.Cancel;
			Close();
		}
	}
}
