using System;
using System.Windows.Forms;

namespace Bloom
{
	/// <summary>
	/// This is a debugging tool for developers to allow access to Mozilla memory management (garbage collection)
	/// information.  Despite the name, this class could be used to display almost any url.  If we can figure out
	/// how to add the "about:ccdump" extension to our mozilla setup, that would be a reasonable additional
	/// possibility for displaying.
	/// </summary>
	public partial class AboutMemory : Form
	{
		public AboutMemory()
		{
			InitializeComponent();
			_browser1.ContextMenuProvider = x => { return true; }; // replace standard menu commands with none
			FirstLinkUrl = "https://developer.mozilla.org/en-US/docs/Mozilla/Performance/about:memory";
			SecondLinkUrl = "https://developer.mozilla.org/en-US/docs/Mozilla/Performance/GC_and_CC_logs";
		}

		public void Navigate(string url)
		{
			_browser1.Focus();
			_browser1.Navigate(url, false);
			_browser1.Focus();
		}

		public string FirstLinkMessage
		{
			set
			{
				_linkLabel1.Text = value;
				_linkLabel1.Visible = !String.IsNullOrEmpty(_linkLabel1.Text);
			}
			get { return _linkLabel1.Text; }
		}

		public string FirstLinkUrl { get; set; }

		public string SecondLinkMessage
		{
			set
			{
				_linkLabel2.Text = value;
				_linkLabel2.Visible = !String.IsNullOrEmpty(_linkLabel2.Text);
			}
			get { return _linkLabel2.Text;  }
		}

		public string SecondLinkUrl { get; set; }

		private void linkLabel1_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
		{
			if (!String.IsNullOrEmpty(FirstLinkUrl))
				SIL.Program.Process.SafeStart(FirstLinkUrl);
		}

		private void _linkLabel2_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
		{
			if (!String.IsNullOrEmpty(SecondLinkUrl))
				SIL.Program.Process.SafeStart(SecondLinkUrl);
		}
	}
}
