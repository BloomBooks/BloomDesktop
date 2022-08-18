using Bloom.Api;
using Bloom.Book;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Net;
using System.Text;
using System.Windows.Forms;
using Gecko;
using L10NSharp;
using SIL.Reporting;
using SIL.Windows.Forms.Miscellaneous;

namespace Bloom
{
	// This is just a temporary thing to centralize switching during progressive coding and testing. 
	public class BrowserMaker
	{
		public static Browser MakeBrowser()
		{
		// Using this #if is the simplest way to allow the WebView2Browser work to proceed without
		// breaking the Linux build.  Other build restrictions are in the .csproj file.
#if !__MonoCS__
			if (ExperimentalFeatures.IsFeatureEnabled(ExperimentalFeatures.kWebView2))
				return new WebView2Browser();
#endif
			return new GeckoFxBrowser();
		}
	}

	public abstract class Browser : UserControl
	{
		internal Point ContextMenuLocation;
		//Func<GeckoContextMenuEventArgs, bool> ContextMenuProvider { get; set; }

		/// <summary>
		/// Allow creator to hook up this event handler if the browser needs to handle Ctrl-N.
		/// Not every browser instance needs this.
		/// Todo: maybe not yet implemented in WebView2?
		/// </summary>
		public ControlKeyEvent ControlKeyEvent { get; set; }
		int VerticalScrollDistance { get; set; }
		//GeckoWebBrowser WebBrowser { get; }

		public abstract void EnsureHandleCreated();

		public event EventHandler BrowserReady;
		public event EventHandler OnBrowserClick;
		public event EventHandler DocumentCompleted;

		public abstract string Url { get; }

		/// <summary>
		/// This function is in the process of migrating from GeckoFx to Webview2.
		/// It originally took a GeckoContextMenuEventArgs, but most users only wanted
		/// the ContextMenu, to which they could add items. A couple also used the TargetNode.
		/// There is no WebView2 equivalent of targetNode, so those menus will have to migrate
		/// to Javascript. However, as long as those panes have not been migrated, we'll keep
		/// passing the targetNode. It is the first argument if the browser is really a
		/// GeckoFxBrowser; otherwise, null (and eventually will go away).
		/// If it returns true these are in place of our standard extensions; if false, the
		/// standard ones will follow whatever it adds.
		/// </summary>
		public Func<object, IMenuItemAdder, bool> ContextMenuProvider { get; set; }

		protected bool WantNativeMenu
		{
			get
			{
#if __MonoCS__
			if (!_controlPressed && ContextMenuProvider == null)
				return true;
#else
				if ((Control.ModifierKeys & Keys.Control) != Keys.Control && ContextMenuProvider == null)
					return true;
#endif
				return false;
			}
		}

		protected bool WantDebugMenuItems
		{
			get
			{
#if DEBUG
				var addDebuggingMenuItems = true;
#else
			var debugBloom = Environment.GetEnvironmentVariable("DEBUGBLOOM")?.ToLowerInvariant();
			var _addDebuggingMenuItems =
 !String.IsNullOrEmpty(debugBloom) && debugBloom != "false" && debugBloom != "no" && debugBloom != "off";
#endif
				return addDebuggingMenuItems || ApplicationUpdateSupport.IsDevOrAlpha;
			}

		}

		public abstract void CopySelection();
		public abstract void SelectAll();

		public abstract void SelectBrowser();

		protected void RaiseBrowserReady()
		{
			EventHandler handler = BrowserReady;
			if (handler != null) handler(this, null);
		}

		protected virtual void RaiseDocumentCompleted(object sender, EventArgs e)
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
		public abstract void Navigate(HtmlDom htmlDom, HtmlDom htmlEditDom = null, bool setAsCurrentPageForDebugging = false, BloomServer.SimulatedPageFileSource source = BloomServer.SimulatedPageFileSource.Nav);
		public abstract void Navigate(string url, bool cleanupFileAfterNavigating);
		public abstract bool NavigateAndWaitTillDone(HtmlDom htmlDom, int timeLimit, string source = "nav", Func<bool> cancelCheck = null, bool throwOnTimeout = true);
		public abstract void NavigateRawHtml(string html);
		public abstract void NavigateToTempFileThenRemoveIt(string path, string urlQueryParams = "");

		public void OnOpenPageInSystemBrowser(object sender, EventArgs e)
		{
			Debug.Assert(!InvokeRequired);
			bool isWindows = SIL.PlatformUtilities.Platform.IsWindows;
			string genericError = "Something went wrong trying to open this page in ";
			try
			{
				// An earlier version of this method made a new temp file in hopes that it would go on working
				// in the browser even after Bloom closed. This has gotten steadily less feasible as we depend
				// more on the http server. With the <base> element now removed, an independent page file will
				// have even more missing links. I don't think it's worth making a separate temp file any more.
				if (isWindows)
					Process.Start("Firefox.exe", '"' + Url + '"');
				else
					SIL.Program.Process.SafeStart("xdg-open", Uri.EscapeUriString(Url));
			}
			catch (Win32Exception)
			{
				if (isWindows)
				{
					MessageBox.Show(genericError + "Firefox. Do you have Firefox in your PATH variable?");
				}
				else
				{
					// See comment in OnGetTroubleShootingInformation() about why BeginInvoke is needed.
					// Also, in Linux, xdg-open calls the System Browser, which isn't necessarily Firefox.
					// It isn't necessarily in Windows, either, but there we're specifying Firefox.
					BeginInvoke((Action)delegate ()
					{
						MessageBox.Show(genericError + "the System Browser.");
					});

				}
			}
		}
		public abstract void ReadEditableAreasNow(string bodyHtml, string userCssContent);
		public abstract string RunJavaScript(string script);
		public abstract void SaveHTML(string path);
		public abstract void SetEditDom(HtmlDom editDom);
		public abstract void SetEditingCommands(CutCommand cutCommand, CopyCommand copyCommand, PasteCommand pasteCommand, UndoCommand undoCommand);
		public abstract void ShowHtml(string html);
		public abstract void UpdateEditButtons();

		protected void AdjustContextMenu(GeckoNode targetNode, IMenuItemAdder adder)
		{
			if (WantNativeMenu)
				return;
			bool addedFFMenuItem = false;
			Debug.Assert(!InvokeRequired);

			ContextMenuLocation = PointToClient(Cursor.Position);
			if (ContextMenuProvider != null)
			{
				var replacesStdMenu = ContextMenuProvider(targetNode, adder);
				if (WantDebugMenuItems || ((ModifierKeys & Keys.Control) == Keys.Control))
				{
					AddOpenPageInFFItem(adder);
					addedFFMenuItem = true;
				}

				if (replacesStdMenu)
					return;
			}

			if (!addedFFMenuItem)
				AddOpenPageInFFItem(adder);
			// Allow debugging entries on any alpha builds as well as any debug builds.
			if (WantDebugMenuItems)
				AddOtherMenuItemsForDebugging(adder);

			adder.Add(
				LocalizationManager.GetString("Browser.CopyTroubleshootingInfo", "Copy Troubleshooting Information"),
				(EventHandler)OnGetTroubleShootingInformation);
		}

		private void AddOpenPageInFFItem(IMenuItemAdder adder)
		{
			adder.Add(
				LocalizationManager.GetString("Browser.OpenPageInFirefox", "Open Page in Firefox (which must be in the PATH environment variable)"),
				OnOpenPageInSystemBrowser);
		}

		private void AddOtherMenuItemsForDebugging(IMenuItemAdder adder)
		{
			adder.Add((string)"Open about:memory window", (EventHandler)OnOpenAboutMemory);
			adder.Add((string)"Open about:config window", (EventHandler)OnOpenAboutConfig);
			adder.Add((string)"Open about:cache window", (EventHandler)OnOpenAboutCache);
			adder.Add((string)"Refresh", (EventHandler)OnRefresh);
		}

		public abstract void OnRefresh(object sender, EventArgs e);

		private void OnOpenAboutMemory(object sender, EventArgs e)
		{
			var form = new AboutMemory();
			form.Text = "Bloom Browser Memory Diagnostics (\"about:memory\")";
			form.FirstLinkMessage = "See https://developer.mozilla.org/en-US/docs/Mozilla/Performance/about:memory for a basic explanation.";
			form.FirstLinkUrl = "https://developer.mozilla.org/en-US/docs/Mozilla/Performance/about:memory";
			form.SecondLinkMessage = "See https://developer.mozilla.org/en-US/docs/Mozilla/Performance/GC_and_CC_logs for more details.";
			form.SecondLinkUrl = "https://developer.mozilla.org/en-US/docs/Mozilla/Performance/GC_and_CC_logs";
			form.Navigate("about:memory");
			form.Show();	// NOT Modal!
		}

		// This is currently still Gecko-specific, not sure whether there will be an equivalent for WebView2.
		private void OnOpenAboutConfig(object sender, EventArgs e)
		{
			var form = new AboutMemory();
			form.Text = "Bloom Browser Internal Configuration Settings (\"about:config\")";
			form.FirstLinkMessage = "See http://kb.mozillazine.org/About:config_entries for a basic explanation.";
			form.FirstLinkUrl = "http://kb.mozillazine.org/About:config_entries";
			form.SecondLinkMessage = null;
			form.SecondLinkUrl = null;
			form.Navigate("about:config");
			form.Show();    // NOT Modal!
		}
		// This is currently still Gecko-specific, not sure whether there will be an equivalent for WebView2.
		private void OnOpenAboutCache(object sender, EventArgs e)
		{
			var form = new AboutMemory();
			form.Text = "Bloom Browser Internal Cache Status (\"about:cache?storage=&context=\")";
			form.FirstLinkMessage = "See http://kb.mozillazine.org/Browser.cache.memory.capacity for a basic explanation.";
			form.FirstLinkUrl = "http://kb.mozillazine.org/Browser.cache.memory.capacity";
			form.SecondLinkMessage = null;
			form.SecondLinkUrl = null;
			form.Navigate("about:cache?storage=&context=");
			form.Show();    // NOT Modal!
		}

		// This is currently still Gecko-specific, not sure whether there will be an equivalent for WebView2.
		public virtual void OnGetTroubleShootingInformation(object sender, EventArgs e)
		{
			Debug.Assert(!InvokeRequired);

			try
			{
				//we can imagine doing a lot more than this... the main thing I wanted was access to the <link> paths for stylesheets,
				//as those can be the cause of errors if Bloom is using the wrong version of some stylesheet, and it might not do that
				//on a developer/ support-person computer.
				var builder = new StringBuilder();

				foreach (string label in ErrorReport.Properties.Keys)
				{
					builder.AppendLine(label + ": " + ErrorReport.Properties[label] + Environment.NewLine);
				}

				builder.AppendLine();

				using (var client = new WebClient())
				{
					builder.AppendLine(client.DownloadString(Url));
				}
				PortableClipboard.SetText(builder.ToString());

				// NOTE: it seems strange to call BeginInvoke to display the MessageBox. However, this
				// is necessary on Linux: this method gets called from the context menu which on Linux
				// is displayed by GTK (which has its own message loop). Calling MessageBox.Show
				// directly kind of works but has all kinds of side-effects like the message box not
				// properly updating and geckofx not properly working anymore. Displaying the message
				// box asynchronously lets us get out of the GTK message loop and displays it
				// properly on the SWF message loop. Technically this is only necessary on Linux, but
				// it doesn't hurt on Windows.
				BeginInvoke((Action) delegate()
				{
					MessageBox.Show("Debugging information has been placed on your clipboard. You can paste it into an email.");
				});
			}
			catch (Exception ex)
			{
				NonFatalProblem.ReportSentryOnly(ex);
			}
		}
	}

	public interface IMenuItemAdder
	{
		void Add(string caption, EventHandler handler, bool enabled = true);
	}
}
