using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Windows.Forms;
using System.Xml;
using Gecko;
using Gecko.DOM;
using Gecko.Events;
using Palaso.IO;
using Palaso.Reporting;
using BloomTemp;
using Bloom.Workspace;

namespace Bloom
{
	public partial class Browser : UserControl
	{
		protected GeckoWebBrowser _browser;
		bool _browserIsReadyToNavigate;
		private string _url;
		private XmlDocument _rootDom; // root DOM we navigate the browser to; typically a shell with other doms in iframes
		private XmlDocument _pageEditDom; // DOM, dypically in an iframe of _rootDom, which we are editing.
		private TempFile _tempHtmlFile;
		private PasteCommand _pasteCommand;
		private CopyCommand _copyCommand;
		private  UndoCommand _undoCommand;
		private  CutCommand _cutCommand;
		private bool _disposed;
		public event EventHandler OnBrowserClick;
		public static event EventHandler XulRunnerShutdown;

		private static JavaScriptErrorHandler _jsErrorHandler;
		private static JavaScriptCallHook _jsCallHook;

		private static int XulRunnerVersion
		{
			get
			{
				var geckofx = Assembly.GetAssembly(typeof(GeckoWebBrowser));
				if (geckofx == null)
					return 0;

				var versionAttribute = geckofx.GetCustomAttributes(typeof(AssemblyFileVersionAttribute), true)
					.FirstOrDefault() as AssemblyFileVersionAttribute;
				return versionAttribute == null ? 0 : new Version(versionAttribute.Version).Major;
			}
		}

		// TODO: refactor to use same initialization code as Palaso
		public static void SetUpXulRunner()
		{
			if (Xpcom.IsInitialized)
				return;

			string xulRunnerPath = Environment.GetEnvironmentVariable("XULRUNNER");
			if (!Directory.Exists(xulRunnerPath))
			{
				xulRunnerPath = Path.Combine(FileLocator.DirectoryOfApplicationOrSolution, "xulrunner");
				if (!Directory.Exists(xulRunnerPath))
				{
					//if this is a programmer, go look in the lib directory
					xulRunnerPath = Path.Combine(FileLocator.DirectoryOfApplicationOrSolution,
						Path.Combine("lib", "xulrunner"));

					//on my build machine, I really like to have the dir labelled with the version.
					//but it's a hassle to update all the other parts (installer, build machine) with this number,
					//so we only use it if we don't find the unnumbered alternative.
					if (!Directory.Exists(xulRunnerPath))
					{
						xulRunnerPath = Path.Combine(FileLocator.DirectoryOfApplicationOrSolution,
							Path.Combine("lib", "xulrunner" + XulRunnerVersion));
					}

					if (!Directory.Exists(xulRunnerPath))
					{
						throw new ConfigurationException(
							"Can't find the directory where xulrunner (version {0}) is installed",
							XulRunnerVersion);
					}
				}
			}

			Xpcom.Initialize(xulRunnerPath);

			var errorsToHide = new List<string>
			{
				"['Shockwave Flash'] is undefined", // can happen when mootools (used by calendar) is loaded
				"mootools", // can happen when mootools (used by calendar) is loaded
				"PlacesCategoriesStarter.js", // happens if you let bloom sit there long enough
				"PlacesDBUtils", // happens if you let bloom sit there long enough
				"privatebrowsing", // no idea why it shows this error sometimes
				"xulrunner", // can happen when mootools (used by calendar) is loaded
				"calledByCSharp", // this can happen while switching pages quickly, when the page unloads after the script starts executing.
				"resource://", // these errors/warnings are coming from internal firefox files
				"chrome://",   // these errors/warnings are coming from internal firefox files
				"jar:",        // these errors/warnings are coming from internal firefox files

				//This one started appearing, only on the ImageOnTop pages, when I introduced jquery.resize.js
				//and then added the ResetRememberedSize() function to it. So it's my fault somehow, but I haven't tracked it down yet.
				//it will continue to show in firebug, so i won't forget about it
				"jquery.js at line 622",

				// Warnings began popping up when we started using http rather than file urls for script tags.
				// 21 JUL 2014, PH: This is a confirmed bug in firefox (https://bugzilla.mozilla.org/show_bug.cgi?id=1020846)
				//   and is supposed to be fixed in firefox 33.
				"is being assigned a //# sourceMappingURL, but already has one"
			};

			// Do this so we can get the call stack of a JavaScript error
			_jsErrorHandler = new JavaScriptErrorHandler(errorsToHide);
			_jsCallHook = new JavaScriptCallHook(_jsErrorHandler);
			_jsCallHook.JavaScriptError += _jsCallHook_JavaScriptError;
			using (var jsd = Xpcom.GetService2<jsdIDebuggerService>(Contracts.DebuggerService))
			{
				jsd.Instance.SetErrorHookAttribute(_jsErrorHandler);
				jsd.Instance.SetDebugHookAttribute(_jsCallHook);
				using (var runtime = Xpcom.GetService2<nsIJSRuntimeService>(Contracts.RuntimeService))
				{
					jsd.Instance.ActivateDebugger(runtime.Instance.GetRuntimeAttribute());
				}
			}

			// BL-535: 404 error if system proxy settings not configured to bypass proxy for localhost
			// See: https://developer.mozilla.org/en-US/docs/Mozilla/Preferences/Mozilla_networking_preferences
			GeckoPreferences.User["network.proxy.http"] = string.Empty;
			GeckoPreferences.User["network.proxy.http_port"] = 80;
			GeckoPreferences.User["network.proxy.type"] = 1; // 0 = direct (uses system settings on Windows), 1 = manual configuration

			Application.ApplicationExit += OnApplicationExit;
		}

		private static void _jsCallHook_JavaScriptError(object sender, JavaScriptErrorArgs e)
		{
			// pop-up the error messages if a debugger is attached or an environment variable is set
			var popUpErrors = Debugger.IsAttached || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DEBUG_BLOOM"));

			// log the error message
			var errorMsg = e.Message + Environment.NewLine + "Call stack:" + Environment.NewLine
						+ e.CallStack + Environment.NewLine;

			Logger.WriteMinorEvent(errorMsg);
			Console.Out.WriteLine(errorMsg);

			if (popUpErrors)
				ErrorReport.NotifyUserOfProblem(errorMsg);
		}

		private static void OnApplicationExit(object sender, EventArgs e)
		{
			// We come here iff we initialized Xpcom. In that case we want to call shutdown,
			// otherwise the app might not exit properly.
			if (XulRunnerShutdown != null)
				XulRunnerShutdown(null, EventArgs.Empty);

			if (Xpcom.IsInitialized)
				Xpcom.Shutdown();
			Application.ApplicationExit -= OnApplicationExit;
		}

		public Browser()
		{
			InitializeComponent();
		}

		/// <summary>
		/// Should be set by every caller of the constructor before attempting navigation. The only reason we don't make it a constructor argument
		/// is so that Browser can be used in designer.
		/// </summary>
		public NavigationIsolator Isolator { get; set; }

		public void SetEditingCommands( CutCommand cutCommand, CopyCommand copyCommand, PasteCommand pasteCommand, UndoCommand undoCommand)
		{
			_cutCommand = cutCommand;
			_copyCommand = copyCommand;
			_pasteCommand = pasteCommand;
			_undoCommand = undoCommand;

			_cutCommand.Implementer = () => _browser.CutSelection();
			_copyCommand.Implementer = () => _browser.CopySelection();
			_pasteCommand.Implementer = PasteFilteredText;
			_undoCommand.Implementer = () =>
			{
				// Note: this is only used for the Undo button in the toolbar;
				// ctrl-z is handled in JavaScript directly.
				var result = RunJavaScript("calledByCSharp ? 'y' : 'f'");
				if (result == "y")
				{
					if (RunJavaScript("calledByCSharp.handleUndo()") == "fail")
						_browser.Undo(); // not using special Undo.
				}
				else
				{
					_browser.Undo();
				}
			};
			//none of these worked
/*            _browser.DomKeyPress+=new GeckoDomKeyEventHandler((sender, args) => UpdateEditButtons());
			_browser.DomClick += new GeckoDomEventHandler((sender, args) => UpdateEditButtons());
			_browser.DomFocus += new GeckoDomEventHandler((sender, args) => UpdateEditButtons());
  */      }

		public void SaveHTML(string path)
		{
			if (InvokeRequired)
			{
				Invoke(new Action<string>(SaveHTML), path);
				return;
			}
			_browser.SaveDocument(path, "text/html");
		}

		private void UpdateEditButtons()
		{
			if (_copyCommand == null)
				return;

			if (InvokeRequired)
			{
				Invoke(new Action(UpdateEditButtons));
				return;
			}

			try
			{
				_cutCommand.Enabled = _browser != null && _browser.CanCutSelection;
				_copyCommand.Enabled = _browser != null && _browser.CanCopySelection;
				_pasteCommand.Enabled = _browser != null && _browser.CanPaste;
				if (_pasteCommand.Enabled)
				{
					//prevent pasting images (BL-93)
					_pasteCommand.Enabled = BloomClipboard.ContainsText();
				}
				_undoCommand.Enabled = CanUndo;

			}
			catch (Exception)
			{
				_pasteCommand.Enabled = false;
				Logger.WriteMinorEvent("UpdateEditButtons(): Swallowed exception.");
				//REf jira.palaso.org/issues/browse/BL-197
				//I saw this happen when Bloom was in the background, with just normal stuff on the clipboard.
				//so it's probably just not ok to check if you're not front-most.
			}
		}

		private bool CanUndo
		{
			get
			{
				if (_browser == null)
					return false;
				var result = RunJavaScript("calledByCSharp ? 'y' : 'f'");
				if (result == "y")
				{
					result = RunJavaScript("calledByCSharp.canUndo()");
					if (result == "fail")
						return _browser.CanUndo; // not using special Undo.
					return result == "yes";
				}
				return _browser.CanUndo;
			}
		}

		void OnValidating(object sender, CancelEventArgs e)
		{
			Debug.Assert(!InvokeRequired);
			LoadPageDomFromBrowser();
			//_afterValidatingTimer.Enabled = true;//LoadPageDomFromBrowser();
		}

		/// <summary>
		/// Clean up any resources being used.
		/// </summary>
		/// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
		protected override void Dispose(bool disposing)
		{
			if (_jsCallHook != null)
			{
				_jsCallHook.JavaScriptError -= _jsCallHook_JavaScriptError;
				_jsCallHook = null;
			}
			_jsErrorHandler = null;

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
			_disposed = true;
		}

		public GeckoWebBrowser WebBrowser { get { return _browser; } }

		protected override void OnLoad(EventArgs e)
		{
			Debug.Assert(!InvokeRequired);
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
			_browser.NoDefaultContextMenu = true;
			_browser.ShowContextMenu += OnShowContextMenu;

			_browser.Navigating += _browser_Navigating;
			//NB: registering for domclicks seems to stop normal hyperlinking (which we don't
			//necessarily need).  When I comment this out, I get an error if the href had, for example,
			//"bloom" for the protocol.  We could probably install that as a protocol, rather than
			//using the click to just get a target and go from there, if we wanted.
			_browser.DomClick += OnBrowser_DomClick;

			_browser.DomKeyPress += OnDomKeyPress;
			_browserIsReadyToNavigate = true;

			UpdateDisplay();
			_browser.Validating += new CancelEventHandler(OnValidating);
			_browser.Navigated += CleanupAfterNavigation;//there's also a "document completed"
			_browser.DocumentCompleted += new EventHandler<GeckoDocumentCompletedEventArgs>(_browser_DocumentCompleted);

			_updateCommandsTimer.Enabled = true;//hack

			GeckoPreferences.User["mousewheel.withcontrolkey.action"] = 3;
			GeckoPreferences.User["browser.zoom.full"] = true;

			//in firefox 14, at least, there was a bug such that if you have more than one lang on the page, all are check with English
			//until we get past that, it's just annoying

			GeckoPreferences.User["layout.spellcheckDefault"] = 0;

			RaiseGeckoReady();
	   }

		private void _browser_DocumentCompleted(object sender, EventArgs e)
		{
			//no: crashes (at least in Sept 2012) AutoZoom();
		}

		/// <summary>
		/// Prevent a CTRL+V pasting when we have the Paste button disabled, e.g. when pictures are on the clipboard
		/// </summary>
		/// <param name="sender"></param>
		/// <param name="e"></param>
		void OnDomKeyPress(object sender, DomKeyEventArgs e)
		{
			Debug.Assert(!InvokeRequired);
			const uint DOM_VK_INSERT = 0x2D;
			if ((e.CtrlKey && e.KeyChar == 'v') || (e.ShiftKey && e.KeyCode == DOM_VK_INSERT)) //someone was using shift-insert to do the paste
			{
				if (_pasteCommand==null /*happend in calendar config*/ || !_pasteCommand.Enabled)
				{
					Debug.WriteLine("Paste not enabled, so ignoring.");
					e.PreventDefault();
				}
				else if(_browser.CanPaste && BloomClipboard.ContainsText())
				{
					e.PreventDefault(); //we'll take it from here, thank you very much
					PasteFilteredText();
				}
			}
		}

		private void PasteFilteredText()
		{
			Debug.Assert(!InvokeRequired);
			//Remove everything from the clipboard except the unicode text (e.g. remove messy html from ms word)
			var originalText = BloomClipboard.GetText(TextDataFormat.UnicodeText);
			//setting clears everything else:
			BloomClipboard.SetText(originalText, TextDataFormat.UnicodeText);
			_browser.Paste();
		}

		/// <summary>
		/// This action will be passed a GeckoContextMenuEventArgs to which appropriate menu items
		/// can be added. For now these are in place of our standard extensions; that is, if this
		/// is non-null the standard ones won't be present.
		/// </summary>
		public Action<GeckoContextMenuEventArgs> ContextMenuProvider { get; set; }

		void OnShowContextMenu(object sender, GeckoContextMenuEventArgs e)
		{
			Debug.Assert(!InvokeRequired);
			if (ContextMenuProvider != null)
			{
				ContextMenuProvider(e);
				return;
			}
			var m = e.ContextMenu.MenuItems.Add("Edit Stylesheets in Stylizer", new EventHandler(OnOpenPageInStylizer));
			m.Enabled = !string.IsNullOrEmpty(GetPathToStylizer());

			e.ContextMenu.MenuItems.Add("Open Page in Firefox (which must be in the PATH environment variable)", new EventHandler(OnOpenPageInSystemBrowser));

			e.ContextMenu.MenuItems.Add("Copy Troubleshooting Information", new EventHandler(OnGetTroubleShootingInformation));
		}

		public void OnGetTroubleShootingInformation(object sender, EventArgs e)
		{
			Debug.Assert(!InvokeRequired);
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
				builder.AppendLine(client.DownloadString(_url));
			}
			BloomClipboard.SetText(builder.ToString());

			// NOTE: it seems strange to call BeginInvoke to display the MessageBox. However, this
			// is necessary on Linux: this method gets called from the context menu which on Linux
			// is displayed by GTK (which has its own message loop). Calling MessageBox.Show
			// directly kind of works but has all kinds of side-effects like the message box not
			// properly updating and geckofx not properly working anymore. Displaying the message
			// box asynchronously lets us get out of the GTK message loop and displays it
			// properly on the SWF message loop. Technically this is only necessary on Linux, but
			// it doesn't hurt on Windows.
			BeginInvoke((Action) delegate() {
				MessageBox.Show("Debugging information has been placed on your clipboard. You can paste it into an email.");
			});
		}

		public void OnOpenPageInSystemBrowser(object sender, EventArgs e)
		{
			Debug.Assert(!InvokeRequired);
			var  temp = Palaso.IO.TempFile.WithExtension(".htm");
			var src = _url.FromLocalhost();
			File.Copy(src, temp.Path,true); //we make a copy because once Bloom leaves this page, it will delete it, which can be an annoying thing to have happen your editor

			if (Palaso.PlatformUtilities.Platform.IsWindows)
				Process.Start("Firefox.exe", '"' + temp.Path.ToLocalhost() + '"');
			else
				Process.Start("xdg-open", temp.Path.ToLocalhost()); ;
		}

		public void OnOpenPageInStylizer(object sender, EventArgs e)
		{
			Debug.Assert(!InvokeRequired);
			string path = Path.GetTempFileName().Replace(".tmp",".html");
			File.Copy(_url, path,true); //we make a copy because once Bloom leaves this page, it will delete it, which can be an annoying thing to have happen your editor
			Process.Start(GetPathToStylizer(), path);
		}

		public static string GetPathToStylizer()
		{
			return FileLocator.LocateInProgramFiles("Stylizer.exe", false, new string[] { "Skybound Stylizer 5" });
		}

		void OnBrowser_DomClick(object sender, DomEventArgs e)
		{
			Debug.Assert(!InvokeRequired);
		  //this helps with a weird condition: make a new page, click in the text box, go over to another program, click in the box again.
			//it loses its focus.
			_browser.WebBrowserFocus.Activate();//trying to help the disappearing cursor problem

			EventHandler handler = OnBrowserClick;
			if (handler != null)
				handler(this, e);
		}

		void _browser_Navigating(object sender, GeckoNavigatingEventArgs e)
		{
			Debug.Assert(!InvokeRequired);
			string url = e.Uri.OriginalString.ToLower();

			if ((!url.StartsWith(Bloom.web.ServerBase.PathEndingInSlash)) && (url.StartsWith("http")))
			{
				e.Cancel = true;
				Process.Start(e.Uri.OriginalString); //open in the system browser instead
				Debug.WriteLine("Navigating " + e.Uri);
			}
		}

		private void CleanupAfterNavigation(object sender, GeckoNavigatedEventArgs e)
		{
			Debug.Assert(!InvokeRequired);
			//_setInitialZoomTimer.Enabled = true;

			Application.Idle += new EventHandler(Application_Idle);

		   //NO. We want to leave it around for debugging purposes. It will be deleted when the next page comes along, or when this class is disposed of
//    		if(_tempHtmlFile!=null)
//    		{
//				_tempHtmlFile.Dispose();
//    			_tempHtmlFile = null;
//    		}
			//didn't seem to do anything:  _browser.WebBrowserFocus.SetFocusAtFirstElement();
		}

		void Application_Idle(object sender, EventArgs e)
		{
			if (_disposed)
				return;

			if (InvokeRequired)
			{
				Invoke(new Action<object, EventArgs>(Application_Idle), sender, e);
				return;
			}
			Application.Idle -= new EventHandler(Application_Idle);

			ZoomToFullWidth();

			//this is the only safe way I've found to do a programatic zoom: trigger a resize event at idle time!
			//NB: if we instead directly call AutoZoom() here, we get a accessviolation pretty easily

			//But even though on my machine this doesn't crash, switching between books makes the resizing
			//stop working, so that even manually reziing the window won't get us a new zoom
/*			var original = Size.Height;
			Size = new Size(Size.Width, Size.Height + 1);
			Size = new Size(Size.Width, original);
	*/	}

		public void Navigate(string url, bool cleanupFileAfterNavigating)
		{
			// BL-513: Navigating to "about:blank" is causing the Pages panel to not be updated for a new book on Linux.
			if (url == "about:blank")
			{
				//This doc, is visible for a bit when we open the edit tab. So it is showing a dark grey to be less visible
				NavigateRawHtml("<!DOCTYPE html><html><head></head><body style='background-color: #363333'></body></html>");
				return;
			}

			if (InvokeRequired)
			{
				Invoke(new Action<string, bool>(Navigate), url, cleanupFileAfterNavigating);
				return;
			}

			_url = url; //TODO: fix up this hack. We found that deleting the pdf while we're still showing it is a bad idea.
			if(cleanupFileAfterNavigating && !_url.EndsWith(".pdf"))
			{
				SetNewTempFile(TempFile.TrackExisting(url));
			}
			UpdateDisplay();
		}

		[DefaultValue(true)]
		public bool ScaleToFullWidthOfPage { get; set; }

		//NB: make sure the <base> is set correctly, 'cause you don't know where this method will
		//save the file before navigating to it.
		public void Navigate(XmlDocument dom, XmlDocument editDom = null)
		{
			if (InvokeRequired)
			{
				Invoke(new Action<XmlDocument, XmlDocument>(Navigate), dom, editDom);
				return;
			}

			_rootDom = dom;//.CloneNode(true); //clone because we want to modify it a bit
			_pageEditDom = editDom ?? dom;

			/*	This doesn't work for the 1st book shown, or when you change book sizes.
			 * But it's still worth doing, becuase without it, we have this annoying re-zoom every time we look at different page.
			*/
			XmlElement body = (XmlElement) _rootDom.GetElementsByTagName("body")[0];
			if (ScaleToFullWidthOfPage)
			{
				var scale = GetScaleToShowWholeWidthOfPage();
				if (scale > 0f)
				{
					body.SetAttribute("style", GetZoomCSS(scale));
				}
			}
			XmlHtmlConverter.MakeXmlishTagsSafeForInterpretationAsHtml(dom);
			SetNewTempFile(TempFileUtils.CreateHtm5FromXml(dom));
			_url = _tempHtmlFile.Path.ToLocalhost();
			UpdateDisplay();
		}

		public void NavigateRawHtml(string html)
		{
			if (InvokeRequired)
			{
				Invoke(new Action<string>(NavigateRawHtml), html);
				return;
			}

			var tf = TempFile.CreateAndGetPathButDontMakeTheFile();
			File.WriteAllText(tf.Path,html);
			SetNewTempFile(tf);
			_url = _tempHtmlFile.Path;
			UpdateDisplay();
		}


		private static string GetZoomCSS(float scale)
		{
			//return "";
			return string.Format("-moz-transform: scale({0}); -moz-transform-origin: 0 0", scale.ToString(CultureInfo.InvariantCulture));
		}

		private void SetNewTempFile(TempFile tempFile)
		{
			if(_tempHtmlFile!=null)
			{
				try
				{
					_tempHtmlFile.Dispose();
				}
				catch(Exception)
				{
						//not worth talking to the user about it. Just abandon it in the Temp directory.
#if DEBUG
					throw;
#endif
				}

			}
			_tempHtmlFile = tempFile;
		}



		private void UpdateDisplay()
		{
			Debug.Assert(!InvokeRequired);
			if (!_browserIsReadyToNavigate)
				return;

			if (_url!=null)
			{
				_browser.Visible = true;
				Isolator.Navigate(_browser, _url);
			}
		}



		private void _afterValidatingTimer_Tick(object sender, EventArgs e)
		{
			_afterValidatingTimer.Enabled = false;
			//LoadPageDomFromBrowser();
			//AutoZoom();
		}
		/// <summary>
		/// What's going on here: the browser is just /editting displaying a copy of one page of the document.
		/// So we need to copy any changes back to the real DOM.
		/// </summary>
		private void LoadPageDomFromBrowser()
		{
			Debug.Assert(!InvokeRequired);
			if (_pageEditDom == null)
				return;

			GeckoDocument contentDocument = null;
			try
			{
				if (_pageEditDom == _rootDom)
					contentDocument = _browser.Document;
				else
				{
					// Assume _editDom corresponds to a frame called 'page' in the root. This may eventually need to be more configurable.
					if (_browser.Window == null || _browser.Window.Document == null)
						return;
					var frameElement = _browser.Window.Document.GetElementById("page") as GeckoIFrameElement;
					if (frameElement == null)
						return;
					contentDocument = frameElement.ContentDocument;
				}
				if (contentDocument == null)
					return; // can this happen?
				// As of august 2012 textareas only occur in the Calendar
				if (_pageEditDom.SelectNodes("//textarea").Count > 0)
				{
					//This approach was to force an onblur so that we can get at the actual user-edited value.
					//This caused problems, with Bloom itself (the Shell) not knowing that it is active.
					//_browser.WebBrowserFocus.Deactivate();
					//_browser.WebBrowserFocus.Activate();

					// Now, we just do the blur directly.
					var activeElement = contentDocument.ActiveElement;
					if (activeElement != null)
						activeElement.Blur();
				}

				var body = contentDocument.GetElementsByTagName("body");
				if (body.Length ==0)	//review: this does happen... onValidating comes along, but there is no body. Assuming it is a timing issue.
					return;

				var content = body[0].InnerHtml;
				XmlDocument dom;

				//todo: deal with exception that can come out of this
				dom = XmlHtmlConverter.GetXmlDomFromHtml(content, false);
				var bodyDom = dom.SelectSingleNode("//body");

				if (_pageEditDom == null)
					return;

				var destinationDomPage = _pageEditDom.SelectSingleNode("//body//div[contains(@class,'bloom-page')]");
				if (destinationDomPage == null)
					return;
				var expectedPageId = destinationDomPage["id"];

				var browserPageId = bodyDom.SelectSingleNode("//body//div[contains(@class,'bloom-page')]");
				if (browserPageId == null)
					return;//why? but I've seen it happen

				var thisPageId = browserPageId["id"];
				if(expectedPageId != thisPageId)
				{
					Palaso.Reporting.ErrorReport.NotifyUserOfProblem("Bloom encountered an error saving that page (unexpected page id)");
					return;
				}
				_pageEditDom.GetElementsByTagName("body")[0].InnerXml = bodyDom.InnerXml;

				var userModifiedStyleSheet = contentDocument.StyleSheets.FirstOrDefault(s =>
					{
						// workaround for bug #40 (https://bitbucket.org/geckofx/geckofx-29.0/issue/40/xpath-error-hresult-0x805b0034)
						// var titleNode = s.OwnerNode.EvaluateXPath("@title").GetSingleNodeValue();
						var titleNode = s.OwnerNode.EvaluateXPath("@title").GetNodes().FirstOrDefault();
						if (titleNode == null)
							return false;
						return titleNode.NodeValue == "userModifiedStyles";
					});

				if (userModifiedStyleSheet != null)
				{
					SaveCustomizedCssRules(userModifiedStyleSheet);
				}

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
			catch(Exception e)
			{
				Debug.Fail("Debug Mode Only: Error while trying to read changes to CSSRules. In Release, this just gets swallowed. Will now re-throw the exception.");
#if DEBUG
				throw;
#endif
			}
			finally
			{
				if (contentDocument != null)
					contentDocument.Dispose();
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

		private void SaveCustomizedCssRules(GeckoStyleSheet userModifiedStyleSheet)
		{
			try
			{
				/* why are we bothering to walk through the rules instead of just copying the html of the style tag? Because that doesn't
				 * actually get updated when the javascript edits the stylesheets of the page. Well, the <style> tag gets created, but
				 * rules don't show up inside of it. So
				 * this won't work: _editDom.GetElementsByTagName("head")[0].InnerText = userModifiedStyleSheet.OwnerNode.OuterHtml;
				 */
				var styles = new StringBuilder();
				styles.AppendLine("<style title='userModifiedStyles' type='text/css'>");
				foreach (var cssRule in userModifiedStyleSheet.CssRules)
				{
					styles.AppendLine(cssRule.CssText);
				}
				styles.AppendLine("</style>");
				Debug.WriteLine("*User Modified Stylesheet in browser:" + styles);
				_pageEditDom.GetElementsByTagName("head")[0].InnerXml = styles.ToString();
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

		private void OnUpdateDisplayTick(object sender, EventArgs e)
		{
			UpdateEditButtons();
		}

		/// <summary>
		/// This is needed if we want to save before getting a natural Validating event.
		/// </summary>
		public void ReadEditableAreasNow()
		{
			if (_url != "about:blank")
			{
		//		RunJavaScript("Cleanup()");
					//nb: it's important not to move this into LoadPageDomFromBrowser(), which is also called during validation, becuase it isn't allowed then
				LoadPageDomFromBrowser();
			}
		}

		public void Copy()
		{
			Debug.Assert(!InvokeRequired);
			_browser.CopySelection();
		}

		/// <summary>
		/// add a jscript source file
		/// </summary>
		/// <param name="filename"></param>
		public void AddScriptSource(string filename)
		{
			Debug.Assert(!InvokeRequired);
			if (!File.Exists(Path.Combine(Path.GetDirectoryName(_url), filename)))
				throw new FileNotFoundException(filename);

			GeckoDocument doc = WebBrowser.Document;
			var head = doc.GetElementsByTagName("head").First();
			GeckoScriptElement script = doc.CreateElement("script") as GeckoScriptElement;
			script.Type = "text/javascript";
			script.Src = filename;
			head.AppendChild(script);
		}

		public void AddScriptContent(string content)
		{
			Debug.Assert(!InvokeRequired);
			GeckoDocument doc = WebBrowser.Document;
			var head = doc.GetElementsByTagName("head").First();
			GeckoScriptElement script = doc.CreateElement("script") as GeckoScriptElement;
			script.Type = "text/javascript";
			script.Text = content;
			head.AppendChild(script);
		}

		public string RunJavaScript(string script)
		{
			Debug.Assert(!InvokeRequired);
			// Review JohnT: does this require integration with the NavigationIsolator?
			using (AutoJSContext context = new AutoJSContext(_browser.Window.JSContext))
			{
				string result;
				context.EvaluateScript(script, (nsISupports)_browser.Document.DomObject, out result);
				return result;
		   }
		}

		HashSet<string> _knownEvents = new HashSet<string>();

		/// <summary>
		/// Only the first call per browser per event name takes effect.
		/// </summary>
		/// <param name="eventName"></param>
		/// <param name="action"></param>
		public void AddMessageEventListener(string eventName, Action<string> action)
		{
			Debug.Assert(!InvokeRequired);
			if (_knownEvents.Contains(eventName))
				return; // This browser already knows what to do about this; hopefully we don't have a conflict.
			_browser.AddMessageEventListener(eventName, action);
			_knownEvents.Add(eventName);
		}


		/* snippets
		 *
		 * //           _browser.WebBrowser.Navigate("javascript:void(document.getElementById('output').innerHTML = 'test')");
//            _browser.WebBrowser.Navigate("javascript:void(alert($.fn.jquery))");
//            _browser.WebBrowser.Navigate("javascript:void(alert($(':input').serialize()))");
			//_browser.WebBrowser.Navigate("javascript:void(document.getElementById('output').innerHTML = form2js('form','.',false,null))");
			//_browser.WebBrowser.Navigate("javascript:void(alert($(\"form\").serialize()))");

			*/
		public event EventHandler GeckoReady;

		public void RaiseGeckoReady()
		{
			EventHandler handler = GeckoReady;
			if (handler != null) handler(this, null);
		}

		public void ShowHtml(string html)
		{
			Debug.Assert(!InvokeRequired);
			_browser.LoadHtml(html);
		}

		private void Browser_Resize(object sender, EventArgs e)
		{
			ZoomToFullWidth();
		}

		private float GetScaleToShowWholeWidthOfPage()
		{
			if (_browser != null)
			{
				if (_browser.InvokeRequired)
				{
					return (float)_browser.Invoke((MethodInvoker)(() => GetScaleToShowWholeWidthOfPage()));
				}

				var div = _browser.Document.ActiveElement;
				if (div != null)
				{
					div = (GeckoHtmlElement)(div.EvaluateXPath("//div[contains(@class, 'bloom-page')]").GetNodes().FirstOrDefault());
					if (div != null)
					{
						if (div.ScrollWidth > _browser.Width)
						{
							var widthWeNeed = div.ScrollWidth + 100 + 100/*for qtips*/;
							return ((float)_browser.Width) / widthWeNeed;

						}
						else
						{
							return 1.0f;
						}
					}
				}
			}
			return 0f;
		}

		private void ZoomToFullWidth()
		{
			if (!ScaleToFullWidthOfPage)
				return;
			var scale = GetScaleToShowWholeWidthOfPage();
			if(scale>0f)
			{
				SetZoom(scale);
			}
		}

		private void SetZoom(float scale)
		{
			Debug.Assert(!InvokeRequired);
/*			//Dangerous. See https://bitbucket.org/geckofx/geckofx-11.0/issue/12/setfullzoom-doesnt-work
			//and if I get it to work (by only calling it from onresize, it stops working after you navigate

			var geckoMarkupDocumentViewer = _browser.GetMarkupDocumentViewer();
			if (geckoMarkupDocumentViewer != null)
			{
				geckoMarkupDocumentViewer.SetFullZoomAttribute(scale);
			}
*/
			// So we append it to the css instead, making sure it's within the 'mainPageScope', if there is one
			var cssString = GetZoomCSS(scale);
			var pageScope = _browser.Document.GetElementById("mainPageScope");
			// Gecko's CssText setter is smart enough not to duplicate styles!
			if (pageScope != null)
				(pageScope as GeckoHtmlElement).Style.CssText += cssString;
			else
				_browser.Document.Body.Style.CssText += cssString;
			_browser.Window.ScrollTo(0, 0);
		}

		/// <summary>
		/// When you receive a OnBrowserClick and have determined that nothing was clicked on that the c# needs to pay attention to,
		/// pass it on to this method. It will either let the browser handle it normally, or redirect it to the operating system
		/// so that it can open the file or external website itself.
		/// </summary>
		public void HandleLinkClick(GeckoAnchorElement anchor, DomEventArgs eventArgs, string workingDirectoryForFileLinks)
		{
			Debug.Assert(!InvokeRequired);
			if (anchor.Href.ToLower().StartsWith("http")) //will cover https also
			{
				Process.Start(anchor.Href);
				eventArgs.Handled = true;
				return;
			}
			if (anchor.Href.ToLower().StartsWith("file"))
			//links to files are handled externally if we can tell they aren't html/javascript related
			{
				// TODO: at this point spaces in the file name will cause the link to fail.
				// That seems to be a problem in the DomEventArgs.Target.CastToGeckoElement() method.
				var href = anchor.Href;

				var path = href.Replace("file:///", "");

				if (new List<string>(new[] { ".pdf", ".odt",".doc", ".docx", ".txt" }).Contains(Path.GetExtension(path).ToLower()))
				{
					eventArgs.Handled = true;
					Process.Start(new ProcessStartInfo()
					{
						FileName = path,
						WorkingDirectory = workingDirectoryForFileLinks
					});
					return;
				}
				eventArgs.Handled = false; //let gecko handle it
				return;
			}
			else if (anchor.Href.ToLower().StartsWith("mailto"))
			{
				eventArgs.Handled = true;
				Process.Start(anchor.Href); //let the system open the email program
				Debug.WriteLine("Opening email program " + anchor.Href);
			}
			else
			{
				ErrorReport.NotifyUserOfProblem("Bloom did not understand this link: " + anchor.Href);
				eventArgs.Handled = true;
			}
		}

		/*
		 * Sets or retrieves the distance between the top of the object and the topmost portion of the content currently visible in the window (scrollTop)
		 */
		public int VerticalScrollDistance
		{
			get
			{
				try
				{
					if (_browser != null)
						return _browser.Document.DocumentElement.ScrollTop;
				}
				catch
				{
				}
				return 0;
			}
			set
			{
				try
				{
					if (_browser != null) // avoids a lot of initialization exceptions
						_browser.Document.DocumentElement.ScrollTop = value;
				}
				catch
				{
				}
			}
		}


		/// <summary>
		/// See https://jira.sil.org/browse/BL-802  and https://bugzilla.mozilla.org/show_bug.cgi?id=1108866
		/// Until that gets fixed, we're better off not listing those fonts that are just going to cause confusion
		/// </summary>
		/// <returns></returns>
		public static IEnumerable<string> NamesOfFontsThatBrowserCanRender()
		{
			using(var installedFontCollection = new InstalledFontCollection())
			{
				var modifierTerms = new string[] { "condensed", "semilight", "black", "bold", "medium", "semibold", "light", "narrow" };

				foreach(var family in installedFontCollection.Families)
				{
					var name = family.Name.ToLowerInvariant();
					if(modifierTerms.Any(modifierTerm => name.Contains(" " + modifierTerm)))
					{
						continue;
						// sorry, we just can't display that font, it will come out as some browser default font (at least on Windows, and at least up to Firefox 36)
					}
					yield return family.Name;
				}
			}
		}
	}
}
