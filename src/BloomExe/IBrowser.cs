using Bloom.Api;
using Bloom.Book;
using System;
using System.Windows.Forms;

namespace Bloom
{
	// this is just a throw-away thing to centralize switching during
	// my coding. 
	public class BrowserMaker
	{
		public static Browser MakeBrowser()
		{
			//return new WebView2Browser();
			return new GeckoFxBrowser();
		}
	}

	public abstract class Browser : UserControl
	{
		//Func<GeckoContextMenuEventArgs, bool> ContextMenuProvider { get; set; }
		ControlKeyEvent ControlKeyEvent { get; set; }
		int VerticalScrollDistance { get; set; }
		//GeckoWebBrowser WebBrowser { get; }

		public abstract void EnsureHandleCreated();

		public event EventHandler BrowserReady;
		public event EventHandler OnBrowserClick;
		public event EventHandler DocumentCompleted;


		/// <summary>
		/// This Function will be passed a GeckoContextMenuEventArgs to which appropriate menu items
		/// can be added. If it returns true these are in place of our standard extensions; if false, the
		/// standard ones will follow whatever it adds.
		/// </summary>
		/// TODO: obviously this is GeckoFx-specific. It is here because it is non-trivial to introduce
		/// a generic mechanism that will work with WebView2, so we are leaving that for another time.
		public Func<Gecko.GeckoContextMenuEventArgs, bool> ContextMenuProvider { get; set; }

		public abstract void CopySelection();
		public abstract void SelectAll();


		protected void RaiseBrowserReady()
		{
			EventHandler handler = BrowserReady;
			if (handler != null) handler(this, null);
		}

		protected void RaiseDocumentCompleted(object sender, EventArgs e)
		{
			DocumentCompleted?.Invoke(sender, e);
		}
		protected void RaiseBrowserClick(object sender, EventArgs e)
		{
			OnBrowserClick?.Invoke(sender, e);
		}
		public abstract void ActivateFocussed(); // review what should this be called?

		public abstract void AddScriptContent(string content);
		public abstract void AddScriptSource(string filename);
		public abstract void Copy();
		//public abstract void HandleLinkClick(GeckoAnchorElement anchor, DomEventArgs eventArgs, string workingDirectoryForFileLinks);
		public abstract void Navigate(HtmlDom htmlDom, HtmlDom htmlEditDom = null, bool setAsCurrentPageForDebugging = false, BloomServer.SimulatedPageFileSource source = BloomServer.SimulatedPageFileSource.Nav);
		public abstract void Navigate(string url, bool cleanupFileAfterNavigating);
		public abstract bool NavigateAndWaitTillDone(HtmlDom htmlDom, int timeLimit, string source = "nav", Func<bool> cancelCheck = null, bool throwOnTimeout = true);
		public abstract void NavigateRawHtml(string html);
		public abstract void NavigateToTempFileThenRemoveIt(string path, string urlQueryParams = "");
		public abstract void OnGetTroubleShootingInformation(object sender, EventArgs e);
		public abstract void OnOpenPageInSystemBrowser(object sender, EventArgs e);
		public abstract void ReadEditableAreasNow(string bodyHtml, string userCssContent);
		public abstract string RunJavaScript(string script);
		public abstract void SaveHTML(string path);
		public abstract void SetEditDom(HtmlDom editDom);
		public abstract void SetEditingCommands(CutCommand cutCommand, CopyCommand copyCommand, PasteCommand pasteCommand, UndoCommand undoCommand);
		public abstract void ShowHtml(string html);
		public abstract void UpdateEditButtons();
	}
}
