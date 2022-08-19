using System;
using System.Collections.Generic;
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

			// I don't think anything we're doing here will take long enough for us to need to await it.
			InitWebView();

			_webview.CoreWebView2InitializationCompleted += (object sender, CoreWebView2InitializationCompletedEventArgs args) =>
			{
				_webview.CoreWebView2.NavigationCompleted += (object sender2, CoreWebView2NavigationCompletedEventArgs args2) =>
					{
						RaiseDocumentCompleted(sender2, args2);
					};
				_webview.CoreWebView2.ContextMenuRequested += ContextMenuRequested;
			};
		}

		private void ContextMenuRequested(object sender, CoreWebView2ContextMenuRequestedEventArgs e)
		{
			// 		Name	"inspectElement"	string
			//"reload"
			if (WantNativeMenu)
				return;
			var wantDebug = WantDebugMenuItems;
			// Remove built-in items (except a couple of useful ones, if we're in a debugging context)
			var menuList = e.MenuItems;

			for (int index = 0; index < menuList.Count; )
			{
				if (wantDebug && (menuList[index].Name == "inspectElement"))
				{
					index++;
					continue;
				}
				menuList.RemoveAt(index);
			}
			AdjustContextMenu(null, new WebViewItemAdder(_webview, menuList));
		}

		public override void OnRefresh(object sender, EventArgs e)
		{
			// Todo
		}

		private async void InitWebView()
		{
			// based on https://stackoverflow.com/questions/63404822/how-to-disable-cors-in-wpf-webview2
			// this should disable CORS, but it doesn't seem to work, at least for fixing communication from
			// an iframe in one domain to a parent in another. Keeping in case I need to try further.
			// However, the reason I thought I needed to disable it was a problem that sourced the root
			// HTML document in edit mode from the wrong domain; we may not need this at all.
			//var op = new CoreWebView2EnvironmentOptions("--allow-insecure-localhost --disable-web-security");
			//var env = await CoreWebView2Environment.CreateAsync(null, null, op);
			//await _webview.EnsureCoreWebView2Async(env);
		}

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

		public override void SelectBrowser()
		{
			// Enhance: investigate reasons why we do this. Possibly it is not necessary after we
			// settle on WebView2; at least one client was just using it to work around a
			// peculiar behavior of GeckoFx.
			_webview.Select();
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
			XmlHtmlConverter.MakeXmlishTagsSafeForInterpretationAsHtml(htmlDom.RawDom);
			var fakeTempFile = BloomServer.MakeSimulatedPageFileInBookFolder(htmlDom, setAsCurrentPageForDebugging: setAsCurrentPageForDebugging, source: source);
			_webview.CoreWebView2.Navigate(fakeTempFile.Key);
			// Do not be tempted to just Navigate to a string that is the HTML of the htmlDom. That will have a different origin
			// than the embedded iframes, which will cause CORS errors when they try to talk to each other.
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

		public override string Url => _webview.Source.ToString();

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

		// Review: base class currently explicitly opens FireFox. Should we instead open Chrome,
		// or whatever the default browser is, or...?
		//public override void OnOpenPageInSystemBrowser(object sender, EventArgs e)
		//{
		//	throw new NotImplementedException();
		//}

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

	class WebViewItemAdder : IMenuItemAdder
	{
		private readonly IList<CoreWebView2ContextMenuItem> _menuList;
		private Microsoft.Web.WebView2.WinForms.WebView2 _webview;
		public WebViewItemAdder(Microsoft.Web.WebView2.WinForms.WebView2 webview, IList<CoreWebView2ContextMenuItem> menuList)
		{
			_webview = webview;
			_menuList = menuList;
		}
		public void Add(string caption, EventHandler handler, bool enabled = true)
		{
			CoreWebView2ContextMenuItem newItem =
				_webview.CoreWebView2.Environment.CreateContextMenuItem(
					caption, null, CoreWebView2ContextMenuItemKind.Command);
			newItem.CustomItemSelected += (sender,args) => handler(sender, new EventArgs());
			newItem.IsEnabled = enabled;
			_menuList.Insert(_menuList.Count, newItem);
		}
	}
}
