using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Data;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.Xml;
using Skybound.Gecko;

namespace Bloom
{
	public partial class Browser : UserControl
	{
		protected GeckoWebBrowser _browser;
		bool _browserIsReadyToNavigate = false;
		private string _url;
		private XmlDocument _domBeingEditted;

		public Browser()
		{
			InitializeComponent();

		}

		void OnValidating(object sender, CancelEventArgs e)
		{
			UpdateDomWithNewEditsCopiedOver();
		}

		protected override void OnLoad(EventArgs e)
		{
			base.OnLoad(e);

			if(DesignMode)
			{
				this.BackColor=Color.DarkGray;
				return;
			}

			_browser = new GeckoWebBrowser();
			_browser.Parent = this;
			_browser.Dock = DockStyle.Fill;
			Controls.Add(_browser);

			_browserIsReadyToNavigate = true;
			UpdateDisplay();
			_browser.Validating += new CancelEventHandler(OnValidating);

		}

		public void Navigate(string url)
		{
			_url=url;
			UpdateDisplay();
		}

		//NB: make sure the <base> is set correctly, 'cause you don't know where this method will
		//save the file before navigating to it.
		public void Navigate(XmlDocument dom)
		{
			_domBeingEditted = dom;
			_url = TempFile.CreateHtm(dom).Path;
			UpdateDisplay();
		}
		private void UpdateDisplay()
		{
			if (!_browserIsReadyToNavigate)
				return;

			if (_url!=null)
			{
				_browser.Navigate(_url);
			}
		}

		/// <summary>
		/// What's going on here: the browser is just /editting displaying a copy of one page of the document.
		/// So we need to copy any changes back to the real DOM.  As a complicating factor, we can't actually
		/// just grab the new value of, say, a textarea.  It will always return the original value to us, even
		/// after being editted. So previously (in AddJavaScriptForEditing), we injected some javascript which
		/// copies the editted values to an attribute we can get at, "newValue".  Now, we can read those values,
		/// and push them into the original DOM.
		/// </summary>
		private void UpdateDomWithNewEditsCopiedOver()
		{
			XmlNamespaceManager namespaceManager = new XmlNamespaceManager(_domBeingEditted.NameTable);
			namespaceManager.AddNamespace("x", "http://www.w3.org/1999/xhtml");
			foreach (XmlElement node in _domBeingEditted.SafeSelectNodes("//x:input", namespaceManager))
			{
				var id = node.GetAttribute("id");
				node.SetAttribute("value", _browser.Document.GetElementById(id).GetAttribute("newValue"));
			}
			foreach (XmlElement node in _domBeingEditted.SafeSelectNodes("//x:textarea", namespaceManager))
			{
				var id = node.GetAttribute("id");
				if (string.IsNullOrEmpty(id))
				{
					// Debug.Fail();
				}
				else
				{
					var value = _browser.Document.GetElementById(id).GetAttribute("newValue");
					if (!string.IsNullOrEmpty(value))
					{
						node.InnerText = value;
					}
				}
			}
		}
	}
}
