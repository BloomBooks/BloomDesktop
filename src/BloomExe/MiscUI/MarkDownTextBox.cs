using System;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;
using Gecko;
using Markdig;
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
			// enable autolinks from text `http://`, `https://`, `ftp://`, `mailto:`, `www.xxx.yyy`
			var pipeline = new MarkdownPipelineBuilder().UseAutoLinks().UseCustomContainers().UseGenericAttributes().Build();
			_htmlLabel.HTML = Markdown.ToHtml(_markdown, pipeline);
		}

		[Browsable(true), Category("MarkDownText")]
		public string MarkDownText
		{
			get { return _markdown; }
			set{_markdown = value;}
		}
	}
}
