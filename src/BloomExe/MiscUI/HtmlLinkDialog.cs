using System.Windows.Forms;

namespace Bloom.MiscUI
{
	/// <summary>
	/// This dialog can be use in place of a MessageBox when the text has
	/// a link to an HTML page on the network.  It uses an internal geckofx
	/// based web display to show the message, including the HTML link.
	/// </summary>
	public partial class HtmlLinkDialog : Form
	{
		public HtmlLinkDialog(string msg, string title = "")
		{
			InitializeComponent();
			this.htmlLabel.HTML = msg;
			this.Text = title;
		}
	}
}
