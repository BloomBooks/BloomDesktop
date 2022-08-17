using System;
using System.Windows.Forms;
using Bloom.Book;
using Bloom.Api;
using Microsoft.Web.WebView2.Core;

namespace Bloom
{
	public partial class WebView2Browser :  Browser
	{

		public WebView2Browser()
		{
			InitializeComponent();

			_webview.CoreWebView2InitializationCompleted += (object sender, CoreWebView2InitializationCompletedEventArgs args) =>
			{
				_webview.CoreWebView2.NavigationCompleted += (object sender2, CoreWebView2NavigationCompletedEventArgs args2) =>
					{
						RaiseDocumentCompleted(sender2, args2);
					};
			};
		}
	
		public ControlKeyEvent ControlKeyEvent { get; set; }
		public int VerticalScrollDistance { get; set; }

		// needed by geckofx but not webview2
		public override void EnsureHandleCreated()
		{		
		}
		public override void CopySelection()
		{
			// I think it's fine that this is async but we aren't waiting, as long as this
			// is only used for user actions and not by code that would immediately try to
			// do something.
			_webview.ExecuteScriptAsync("document.execCommand(\"Copy\")");

		}
		public override void SelectAll()
		{
			// I think it's fine that this is async but we aren't waiting, as long as this
			// is only used for user actions and not by code that would immediately try to
			// do something.
			_webview.ExecuteScriptAsync("document.execCommand(\"SelectAll\")");
		}
		public override void AddScriptContent(string content)
		{
			throw new NotImplementedException();
		}

		public override void ActivateFocussed() 
		{
			//TODO
		}

		public override void AddScriptSource(string filename)
		{
			throw new NotImplementedException();
		}

		public override void Copy()
		{
			throw new NotImplementedException();
		}

		public override void Navigate(HtmlDom htmlDom, HtmlDom htmlEditDom, bool setAsCurrentPageForDebugging, BloomServer.SimulatedPageFileSource source)
		{
			var html = XmlHtmlConverter.ConvertDomToHtml5(htmlDom.RawDom);
			_webview.NavigateToString(html);
		}

		public async override void Navigate(string url, bool cleanupFileAfterNavigating)
		{
			await _webview.EnsureCoreWebView2Async();
			_webview.CoreWebView2.Navigate(url);
		}

		public override bool NavigateAndWaitTillDone(HtmlDom htmlDom, int timeLimit, string source, Func<bool> cancelCheck, bool throwOnTimeout)
		{
			var html = XmlHtmlConverter.ConvertDomToHtml5(htmlDom.RawDom);
			_webview.NavigateToString(html);
			return true;
		}

		public override void NavigateRawHtml(string html)
		{
			_webview.NavigateToString(html);
		}

		public override void NavigateToTempFileThenRemoveIt(string path, string urlQueryParams)
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

		public override void OnGetTroubleShootingInformation(object sender, EventArgs e)
		{
			throw new NotImplementedException();
		}

		public override void OnOpenPageInSystemBrowser(object sender, EventArgs e)
		{
			throw new NotImplementedException();
		}

		public override void ReadEditableAreasNow(string bodyHtml, string userCssContent)
		{
			
		}

		public override string RunJavaScript(string script)
		{
			return null;
		}

		public override void SaveHTML(string path)
		{
			throw new NotImplementedException();
		}

		public override void SetEditDom(HtmlDom editDom)
		{
			throw new NotImplementedException();
		}

		public override void SetEditingCommands(CutCommand cutCommand, CopyCommand copyCommand, PasteCommand pasteCommand, UndoCommand undoCommand)
		{
			
		}

		public override void ShowHtml(string html)
		{
			throw new NotImplementedException();
		}

		public override void UpdateEditButtons()
		{
			
		}
	}
}
