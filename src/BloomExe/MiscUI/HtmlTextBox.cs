using System;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;
using Gecko;
using SIL.Windows.Forms.Extensions;

namespace Bloom.MiscUI
{
	public partial class HtmlTextBox : UserControl
	{
		private  string _html="Need to set the property \"HtmlText\"";

		public HtmlTextBox()
		{
			InitializeComponent();
			if (this.DesignModeAtAll())
				return;
		}

		private void HtmlTextBox_Load(object sender, EventArgs e)
		{
			if (this.DesignModeAtAll())
				return;
			_htmlLabel.HTML = _html;
		}

		[Browsable(true), Category("HtmlText")]
		public string HtmlText
		{
			get { return _html; }
			set{_html = value;}
		}
	}
}
