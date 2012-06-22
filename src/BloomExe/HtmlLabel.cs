using System;
using System.ComponentModel;
using System.Windows.Forms;
using Gecko;
using Palaso.Extensions;

namespace Bloom
{
	/// <summary>
	/// Any links (web or file path) are cause the browser or file explorer to open.
	/// </summary>
	public partial class HtmlLabel : UserControl
	{
		private GeckoWebBrowser _browser;
		private string _html;

		public HtmlLabel()
		{
			FontSize = 9;
			ColorName = "black";
			InitializeComponent();
		}


		/// <summary>
		/// Just a simple html string, no html, head, body tags.
		/// </summary>
		[Browsable(true),CategoryAttribute("Text")]
		public string HTML
		{
			get { return _html; }
			set
			{
				_html = value;
				if (this.DesignModeAtAll())
					return;

				if (_browser!=null)
				{
					_browser.Visible = !string.IsNullOrEmpty(_html);
					if(_browser.Visible)
						_browser.LoadHtml("<span style=\"color:"+ColorName+"; font-family:Segoe UI, Arial; font-size:" + FontSize.ToString() + "pt\">" + _html+"</span>");
				}
			}
		}

		public int FontSize;
		public string ColorName;

		private void HtmlLabel_Load(object sender, EventArgs e)
		{
			if (this.DesignModeAtAll())
				return;

			_browser = new GeckoWebBrowser();

			_browser.Parent = this;
			_browser.Dock = DockStyle.Fill;
			Controls.Add(_browser);
			_browser.NoDefaultContextMenu = true;
			_browser.Margin = new Padding(0);

			HTML = _html;//in the likely case that there's html waiting to be shown
			_browser.DomClick += new EventHandler<GeckoDomEventArgs>(OnBrowser_DomClick);

		}

		private void OnBrowser_DomClick(object sender, GeckoDomEventArgs e)
		{
			if (this.DesignModeAtAll())
				return;

		  var ge = e as GeckoDomEventArgs;
			if (ge.Target == null)
				return;
			if (ge.Target.TagName=="A")
			{
				var url = ge.Target.GetAttribute("href");
				System.Diagnostics.Process.Start(url);
				e.Handled = true; //don't let the browser navigate itself
			}
		}
	}
}
