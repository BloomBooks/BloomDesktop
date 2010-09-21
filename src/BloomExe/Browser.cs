using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using System.Xml;
using Palaso.Xml;
using Skybound.Gecko;

namespace Bloom
{
	public partial class Browser : UserControl
	{
		protected GeckoWebBrowser _browser;
		bool _browserIsReadyToNavigate;
		private string _url;
		private XmlDocument _pageDom;
		private TempFile _tempHtmlFile;

		public Browser()
		{
			InitializeComponent();

		}

		void OnValidating(object sender, CancelEventArgs e)
		{
			UpdateDomWithNewEditsCopiedOver();
		}

		/// <summary>
		/// Clean up any resources being used.
		/// </summary>
		/// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
		protected override void Dispose(bool disposing)
		{
			if (_tempHtmlFile != null)
			{
				_tempHtmlFile.Dispose();
				_tempHtmlFile = null;
			}
			if (disposing && (components != null))
			{
				components.Dispose();
			}
			base.Dispose(disposing);
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
			_browser.Navigated += CleanupAfterNavigation;//there's also a "document completed"
		}

		private void CleanupAfterNavigation(object sender, GeckoNavigatedEventArgs e)
		{
			if(_tempHtmlFile!=null)
			{
				_tempHtmlFile.Dispose();
				_tempHtmlFile = null;
			}
		}

		public void Navigate(string url, bool cleanupFileAfterNavigating)
		{
			_url=url;
			if(cleanupFileAfterNavigating)
			{
				SetNewTempFile(TempFile.TrackExisting(url));
			}
			UpdateDisplay();
		}

		//NB: make sure the <base> is set correctly, 'cause you don't know where this method will
		//save the file before navigating to it.
		public void Navigate(XmlDocument dom)
		{
			_pageDom = dom;
			AddJavaScriptForEditing(_pageDom);
			MakeSafeForBrowserWhichDoesntUnderstandXmlSingleElements(dom);
			SetNewTempFile(TempFile.CreateHtm(dom));
			_url = _tempHtmlFile.Path;
			UpdateDisplay();
		}

		private void SetNewTempFile(TempFile tempFile)
		{
			if(_tempHtmlFile!=null)
			{
				_tempHtmlFile.Dispose();
			}
			_tempHtmlFile = tempFile;
		}

		private void MakeSafeForBrowserWhichDoesntUnderstandXmlSingleElements(XmlDocument dom)
		{
			foreach (XmlElement node in dom.SafeSelectNodes("//textarea"))
			{
				if (string.IsNullOrEmpty(node.InnerText))
				{
					node.InnerText = " ";
				}
			}
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
		/// So we need to copy any changes back to the real DOM.
		/// </summary>
		private void UpdateDomWithNewEditsCopiedOver()
		{
			foreach (XmlElement node in _pageDom.SafeSelectNodes("//input"))
			{
				var id = node.GetAttribute("id");
				node.SetAttribute("value", _browser.Document.GetElementById(id).GetAttribute("value"));
			}

			foreach (XmlElement node in _pageDom.SafeSelectNodes("//textarea"))
			{
				var id = node.GetAttribute("id");
				if (string.IsNullOrEmpty(id))
				{
					throw new ApplicationException("Could not find the id '"+id+"' in the textarea");
				}
				else
				{
					node.InnerText = _browser.Document.GetElementById(id).InnerHtml;
				}
			}
		}

		/// <summary>
		/// When editting using a browser (at least, gecko), we can't actually
		/// just grab the new value of, say, a textarea.  Gecko will always return the
		/// original value to us, even after being editted.
		/// But from *within* the browser, javascript can get at the new values.
		/// So here, we inject some javascript which
		/// copies the editted values back into the dom.
		/// </summary>
		private void AddJavaScriptForEditing(XmlDocument dom)
		{
			//ref: http://dev-answers.blogspot.com/2007/08/firefox-does-not-reflect-input-form.html
			foreach (XmlElement node in dom.SafeSelectNodes("//input"))
			{
				node.SetAttribute("onblur", "", "this.setAttribute('Value',this.value);");
			}
			foreach (XmlElement node in dom.SafeSelectNodes("//textarea"))
			{
				node.SetAttribute("onblur", "","this.innerHTML = this.value;");
			}
		}
	}
}
