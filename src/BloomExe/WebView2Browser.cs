using System;
using System.Windows.Forms;
using Bloom.Book;
using Bloom.Api;
using Microsoft.Web.WebView2.Core;

namespace Bloom
{
	public partial class WebView2Browser : UserControl, IBrowser
	{

		public WebView2Browser()
		{
			InitializeComponent();

			_webview.CoreWebView2InitializationCompleted += (object sender, CoreWebView2InitializationCompletedEventArgs args) =>
			{
				_webview.CoreWebView2.NavigationCompleted += (object sender2, CoreWebView2NavigationCompletedEventArgs args2) =>
					{
						if(DocumentCompleted!=null)
							DocumentCompleted(sender2, args2);
					};
			};
		}
		//Func<GeckoContextMenuEventArgs, bool> ContextMenuProvider { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
		public ControlKeyEvent ControlKeyEvent { get; set; }
		public int VerticalScrollDistance { get; set; }

		//GeckoWebBrowser WebBrowser => throw new NotImplementedException();

		public event EventHandler BrowserReady;

		public event EventHandler OnBrowserClick;
		public event EventHandler DocumentCompleted;

		// needed by geckofx but not webview2
		public void EnsureHandleCreated()
		{		
		}

		public void AddScriptContent(string content)
		{
			throw new NotImplementedException();
		}

		public void ActivateFocussed() 
		{
			//TODO
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
			_webview.NavigateToString(html);
		}

		public async void Navigate(string url, bool cleanupFileAfterNavigating)
		{
			await _webview.EnsureCoreWebView2Async();
			_webview.CoreWebView2.Navigate(url);
		}

		public bool NavigateAndWaitTillDone(HtmlDom htmlDom, int timeLimit, string source, Func<bool> cancelCheck, bool throwOnTimeout)
		{
			var html = XmlHtmlConverter.ConvertDomToHtml5(htmlDom.RawDom);
			_webview.NavigateToString(html);
			return true;
		}

		public void NavigateRawHtml(string html)
		{
			_webview.NavigateToString(html);
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
