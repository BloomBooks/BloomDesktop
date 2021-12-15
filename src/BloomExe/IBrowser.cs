using Bloom.Api;
using Bloom.Book;
using Gecko;
using Gecko.DOM;
using System;

namespace Bloom
{
	public interface IBrowser 
	{
		//Func<GeckoContextMenuEventArgs, bool> ContextMenuProvider { get; set; }
		ControlKeyEvent ControlKeyEvent { get; set; }
		int VerticalScrollDistance { get; set; }
		//GeckoWebBrowser WebBrowser { get; }

		event EventHandler BrowserReady;
		event EventHandler OnBrowserClick;

		void AddScriptContent(string content);
		void AddScriptSource(string filename);
		void Copy();
		//void HandleLinkClick(GeckoAnchorElement anchor, DomEventArgs eventArgs, string workingDirectoryForFileLinks);
		void Navigate(HtmlDom htmlDom, HtmlDom htmlEditDom = null, bool setAsCurrentPageForDebugging = false, BloomServer.SimulatedPageFileSource source = BloomServer.SimulatedPageFileSource.Nav);
		void Navigate(string url, bool cleanupFileAfterNavigating);
		bool NavigateAndWaitTillDone(HtmlDom htmlDom, int timeLimit, string source = "nav", Func<bool> cancelCheck = null, bool throwOnTimeout = true);
		void NavigateRawHtml(string html);
		void NavigateToTempFileThenRemoveIt(string path, string urlQueryParams = "");
		void OnGetTroubleShootingInformation(object sender, EventArgs e);
		void OnOpenPageInSystemBrowser(object sender, EventArgs e);
		void RaiseBrowserReady();
		void ReadEditableAreasNow(string bodyHtml, string userCssContent);
		string RunJavaScript(string script);
		void SaveHTML(string path);
		void SetEditDom(HtmlDom editDom);
		void SetEditingCommands(CutCommand cutCommand, CopyCommand copyCommand, PasteCommand pasteCommand, UndoCommand undoCommand);
		void ShowHtml(string html);
		void UpdateEditButtons();
	}
}
