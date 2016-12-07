using System;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;
using Gecko;
using MarkdownSharp;
using SIL.Windows.Forms.Extensions;

namespace Bloom.MiscUI
{
	public partial class MarkDownTextBox : UserControl
	{
		private  string _markdown="Need to set the property \"MarkDownText\"";

		public MarkDownTextBox()
		{
			InitializeComponent();
			if(this.DesignModeAtAll())
				return;
		}

		private void MarkDownTextBox_Load(object sender, EventArgs e)
		{
			if(this.DesignModeAtAll())
				return;
			 var markdownTransformer = new Markdown();
			_htmlLabel.HTML = markdownTransformer.Transform(_markdown);
		}

		[Browsable(true), Category("MarkDownText")]
		public string MarkDownText
		{
			get { return _markdown; }
			set{_markdown = value;}
		}
	}
}
