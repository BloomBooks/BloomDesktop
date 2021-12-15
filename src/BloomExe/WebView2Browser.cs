using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Text;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Windows.Forms;
using System.Xml;
using Bloom.Book;
using Bloom.Api;
using Bloom.Edit;
using SIL.IO;
using SIL.Reporting;
using SIL.Windows.Forms.Miscellaneous;
using L10NSharp;
using SimulatedPageFileSource = Bloom.Api.BloomServer.SimulatedPageFileSource;

namespace Bloom
{
	public partial class WebView2Browser : UserControl, IBrowser
	{

		public WebView2Browser()
		{
			InitializeComponent();
		}
		//Func<GeckoContextMenuEventArgs, bool> ContextMenuProvider { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
		public ControlKeyEvent ControlKeyEvent { get; set; }
		public int VerticalScrollDistance { get; set; }

		//GeckoWebBrowser WebBrowser => throw new NotImplementedException();

		public event EventHandler BrowserReady;

		public event EventHandler OnBrowserClick;

		public void AddScriptContent(string content)
		{
			throw new NotImplementedException();
		}

		public void AddScriptSource(string filename)
		{
			throw new NotImplementedException();
		}

		public void Copy()
		{
			throw new NotImplementedException();
		}

		//public void HandleLinkClick(GeckoAnchorElement anchor, DomEventArgs eventArgs, string workingDirectoryForFileLinks)
		//{
		//	throw new NotImplementedException();
		//}

		public void Navigate(HtmlDom htmlDom, HtmlDom htmlEditDom, bool setAsCurrentPageForDebugging, BloomServer.SimulatedPageFileSource source)
		{
			var html = XmlHtmlConverter.ConvertDomToHtml5(htmlDom.RawDom);
			_browser.NavigateToString(html);
		}

		public async void Navigate(string url, bool cleanupFileAfterNavigating)
		{
			await _browser.EnsureCoreWebView2Async();
			_browser.CoreWebView2.Navigate(url);
		}

		public bool NavigateAndWaitTillDone(HtmlDom htmlDom, int timeLimit, string source, Func<bool> cancelCheck, bool throwOnTimeout)
		{
			var html = XmlHtmlConverter.ConvertDomToHtml5(htmlDom.RawDom);
			_browser.NavigateToString(html);
			return true;
		}

		public void NavigateRawHtml(string html)
		{
			_browser.NavigateToString(html);
		}

		public void NavigateToTempFileThenRemoveIt(string path, string urlQueryParams)
		{
			// Convert from path to URL
			if (!String.IsNullOrEmpty(urlQueryParams))
			{
				if (!urlQueryParams.StartsWith("?"))
					urlQueryParams = '?' + urlQueryParams;
			}
			var url = path.ToLocalhost() + urlQueryParams;
			this.Navigate(url, false);
		}

		public void OnGetTroubleShootingInformation(object sender, EventArgs e)
		{
			throw new NotImplementedException();
		}

		public void OnOpenPageInSystemBrowser(object sender, EventArgs e)
		{
			throw new NotImplementedException();
		}

		public void RaiseBrowserReady()
		{
			
		}

		public void ReadEditableAreasNow(string bodyHtml, string userCssContent)
		{
			
		}

		public string RunJavaScript(string script)
		{
			return null;
		}

		public void SaveHTML(string path)
		{
			throw new NotImplementedException();
		}

		public void SetEditDom(HtmlDom editDom)
		{
			throw new NotImplementedException();
		}

		public void SetEditingCommands(CutCommand cutCommand, CopyCommand copyCommand, PasteCommand pasteCommand, UndoCommand undoCommand)
		{
			
		}

		public void ShowHtml(string html)
		{
			throw new NotImplementedException();
		}

		public void UpdateEditButtons()
		{
			
		}
	}
}
