using Bloom.Api;
using Bloom.Book;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Net;
using System.Text;
using System.Windows.Forms;
using System.Xml;
using Gecko;
using L10NSharp;
using SIL.IO;
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

	/// <summary>
	/// This class is a bit of an oddity. The original Browser class was a wrapper for GeckoFxWebBrowser.
	/// It added all kinds of code related to how Bloom uses a browser. Then we needed to be able to use
	/// a different browser class (currently WebView2). So we made the class abstract and all the methods
	/// that depend on exactly which browser we're working with abstract. Then different subclasses can
	/// implement them appropriately. What's left apart from the abstract methods is various stuff
	/// related to how Bloom uses browser components.
	/// </summary>
	public abstract class Browser : UserControl
	{
		internal Point ContextMenuLocation;
		private XmlDocument _pageEditDom; // DOM, dypically in an iframe of _rootDom, which we are editing.
		private XmlDocument _rootDom; // root DOM we navigate the browser to; typically a shell with other doms in iframes
		// A temporary object needed just as long as it is the content of this browser.
		// Currently may be a TempFile (a real filesystem file) or a SimulatedPageFile (just a dictionary entry).
		// It gets disposed when the Browser goes away.
		private IDisposable _dependentContent;
		protected string _replacedUrl;

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

		// To allow Typescript code to implement right-click, we'll do our special developer menu
		// only if the control key is down. Though, if ContextMenuProvider is non-null, we'll assume
		// C# is supposed to handle the context menu here.
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
			var addDebuggingMenuItems =
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

		// NB: make sure you assigned HtmlDom.BaseForRelativePaths if the temporary document might
		// contain references to files in the directory of the original HTML file it is derived from,
		// 'cause that provides the information needed
		// to fake out the browser about where the 'file' is so internal references work.();
		public void Navigate(HtmlDom htmlDom, HtmlDom htmlEditDom = null, bool setAsCurrentPageForDebugging = false,
			BloomServer.SimulatedPageFileSource source = BloomServer.SimulatedPageFileSource.Nav)
		{
			if (InvokeRequired)
			{
				Invoke(new Action<HtmlDom, HtmlDom, bool, BloomServer.SimulatedPageFileSource>(Navigate), htmlDom, htmlEditDom, setAsCurrentPageForDebugging, source);
				return;
			}

			EnsureBrowserReadyToNavigate();

			XmlDocument dom = htmlDom.RawDom;
			XmlDocument editDom = htmlEditDom == null ? null : htmlEditDom.RawDom;

			_rootDom = dom;//.CloneNode(true); //clone because we want to modify it a bit
			_pageEditDom = editDom ?? dom;

			XmlHtmlConverter.MakeXmlishTagsSafeForInterpretationAsHtml(dom);
			var fakeTempFile = BloomServer.MakeSimulatedPageFileInBookFolder(htmlDom, setAsCurrentPageForDebugging: setAsCurrentPageForDebugging, source: source);
			SetNewDependent(fakeTempFile);
			UpdateDisplay(fakeTempFile.Key);
		}

		private void SetNewDependent(IDisposable dependent)
		{
			// Save information needed to prevent http://issues.bloomlibrary.org/youtrack/issue/BL-4268.
			var simulated = _dependentContent as SimulatedPageFile;
			_replacedUrl = (simulated != null) ? simulated.Key : null;

			if (_dependentContent != null)
			{
				try
				{
					_dependentContent.Dispose();
				}
				catch (Exception)
				{
					//not worth talking to the user about it. Just abandon it in the Temp directory.
#if DEBUG
					throw;
#endif
				}

			}
			_dependentContent = dependent;
		}


		// Navigate the browser to Url
		protected abstract void UpdateDisplay(string newUrl);

		protected abstract void EnsureBrowserReadyToNavigate();

		public void Navigate(string url, bool cleanupFileAfterNavigating)
		{
			// BL-513: Navigating to "about:blank" is causing the Pages panel to not be updated for a new book on Linux.
			if (url == "about:blank")
			{
				// Creating a temp file every time we need this seems excessive, and it turns out to
				// be fragile as well.  See https://issues.bloomlibrary.org/youtrack/issue/BL-5598.
				url = FileLocationUtilities.GetFileDistributedWithApplication("BloomBlankPage.htm");
				cleanupFileAfterNavigating = false;
			}

			if (InvokeRequired)
			{
				Invoke(new Action<string, bool>(Navigate), url, cleanupFileAfterNavigating);
				return;
			}

			//TODO: fix up this hack. We found that deleting the pdf while we're still showing it is a bad idea.
			if (cleanupFileAfterNavigating && !url.EndsWith(".pdf"))
			{
				SetNewDependent(TempFile.TrackExisting(url));
			}
			UpdateDisplay(url);
		}
		public abstract bool NavigateAndWaitTillDone(HtmlDom htmlDom, int timeLimit, string source = "nav", Func<bool> cancelCheck = null, bool throwOnTimeout = true);

		public void NavigateRawHtml(string html)
		{
			if (InvokeRequired)
			{
				Invoke(new Action<string>(NavigateRawHtml), html);
				return;
			}

			var tf = TempFile.WithExtension("htm"); // For some reason Gecko won't recognize a utf-8 file as html unless it has the right extension
			RobustFile.WriteAllText(tf.Path, html, Encoding.UTF8);
			SetNewDependent(tf);
			UpdateDisplay(tf.Path);
		}

		protected override void Dispose(bool disposing)
		{
			base.Dispose(disposing);
			if (_dependentContent != null)
			{
				_dependentContent.Dispose();
				_dependentContent = null;
			}
		}

		/// <summary>
		/// The normal Navigate() has a similar capability, but I'm not clear where it works
		/// or how it can work, since it expects an actual url, and once you have an actual
		/// url, how do you get back to the file path That you need to delete the temp file?
		/// So in any case, this version takes a path and handles making a url out of it as
		/// well as deleting it when this browser component is disposed of.
		/// </summary>
		/// <param name="path">The path to the temp file. Should be a valid Filesystem path.</param>
		/// <param name="urlQueryParams">The query component of a URL (that is, the part after the "?" char in a URL).
		/// This string should be a valid, appropriately encoded string ready to insert into a URL
		/// You may include or omit the "?" at the beginning, either way is fine.
		/// </param>
		public void NavigateToTempFileThenRemoveIt(string path, string urlQueryParams = "")
		{
			if (InvokeRequired)
			{
				Invoke(new Action<string, string>(NavigateToTempFileThenRemoveIt), path, urlQueryParams);
				return;
			}

			// Convert from path to URL
			if (!String.IsNullOrEmpty(urlQueryParams))
			{
				if (!urlQueryParams.StartsWith("?"))
					urlQueryParams = '?' + urlQueryParams;
			}

			SetNewDependent(TempFile.TrackExisting(path));
			UpdateDisplay(path.ToLocalhost() + urlQueryParams);
		}

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

		public void ReadEditableAreasNow(string bodyHtml, string userCssContent)
		{
			if (Url != "about:blank")
			{
				LoadPageDomFromBrowser(bodyHtml, userCssContent);
			}
		}


		/// <summary>
		/// What's going on here: the browser is just editing/displaying a copy of one page of the document.
		/// So we need to copy any changes back to the real DOM.
		/// We're now obtaining the new content another way, so this code doesn't have any reason
		/// to be in this class...but we're aiming for a minimal change, maximal safety fix for 4.9
		/// </summary>
		private void LoadPageDomFromBrowser(string bodyHtml, string userCssContent)
		{
			Debug.Assert(!InvokeRequired);
			if (_pageEditDom == null)
				return;

			try
			{
				// unlikely, but if we somehow couldn't get the new content, better keep the old.
				// This MIGHT be able to happen in some cases of very fast page clicking, where
				// the page isn't fully enough loaded to expose the functions we use to get the
				// content. In that case, the user can't have made changes, so not saving is fine.
				if (string.IsNullOrEmpty(bodyHtml))
					return;

				var content = bodyHtml;
				XmlDocument dom;

				//todo: deal with exception that can come out of this
				dom = XmlHtmlConverter.GetXmlDomFromHtml(content, false);
				var bodyDom = dom.SelectSingleNode("//body");

				if (_pageEditDom == null)
					return;

				var destinationDomPage = _pageEditDom.SelectSingleNode("//body//div[contains(@class,'bloom-page')]");
				if (destinationDomPage == null)
					return;
				var expectedPageId = destinationDomPage.Attributes["id"].Value;

				var browserDomPage = bodyDom.SelectSingleNode("//body//div[contains(@class,'bloom-page')]");
				if (browserDomPage == null)
					return;//why? but I've seen it happen

				var thisPageId = browserDomPage.Attributes["id"].Value;
				if (expectedPageId != thisPageId)
				{
					SIL.Reporting.ErrorReport.NotifyUserOfProblem(LocalizationManager.GetString("Browser.ProblemSaving",
						"There was a problem while saving. Please return to the previous page and make sure it looks correct."));
					return;
				}
				_pageEditDom.GetElementsByTagName("body")[0].InnerXml = bodyDom.InnerXml;

				SaveCustomizedCssRules(userCssContent);

				//enhance: we have jscript for this: cleanup()... but running jscript in this method was leading the browser to show blank screen
				//				foreach (XmlElement j in _editDom.SafeSelectNodes("//div[contains(@class, 'ui-tooltip')]"))
				//				{
				//					j.ParentNode.RemoveChild(j);
				//				}
				//				foreach (XmlAttribute j in _editDom.SafeSelectNodes("//@ariasecondary-describedby | //@aria-describedby"))
				//				{
				//					j.OwnerElement.RemoveAttributeNode(j);
				//				}

			}
			catch (Exception e)
			{
				Bloom.Utils.MiscUtils.SuppressUnusedExceptionVarWarning(e);
				Debug.Fail("Debug Mode Only: Error while trying to read changes to CSSRules. In Release, this just gets swallowed. Will now re-throw the exception.");
#if DEBUG
				throw;
#endif
			}

			try
			{
				XmlHtmlConverter.ThrowIfHtmlHasErrors(_pageEditDom.OuterXml);
			}
			catch (Exception e)
			{
				//var exceptionWithHtmlContents = new Exception(content);
				ErrorReport.NotifyUserOfProblem(e,
					"Sorry, Bloom choked on something on this page (validating page).{1}{1}+{0}",
					e.Message, Environment.NewLine);
			}

		}

		private void SaveCustomizedCssRules(string userCssContent)
		{
			try
			{
				// Yes, this wipes out everything else in the head. At this point, the only things
				// we need in _pageEditDom are the user defined style sheet and the bloom-page element in the body.
				_pageEditDom.GetElementsByTagName("head")[0].InnerXml = HtmlDom.CreateUserModifiedStyles(userCssContent);
			}
			catch (GeckoJavaScriptException jsex)
			{
				/* We are attempting to catch and ignore all JavaScript errors encountered here,
				 * specifically addEventListener errors and JSError (BL-279, BL-355, et al.).
				 */
				Logger.WriteEvent("GeckoJavaScriptException (" + jsex.Message + "). We're swallowing it but listing it here in the log.");
				Debug.Fail("GeckoJavaScriptException(" + jsex.Message + "). In Release version, this would not show.");
			}
		}

		public abstract string RunJavaScript(string script);
		public abstract void SaveHTML(string path);

		public void SetEditDom(HtmlDom editDom)
		{
			_pageEditDom = editDom.RawDom;
		}
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
