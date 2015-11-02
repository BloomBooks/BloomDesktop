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
using System.Text.RegularExpressions;
using System.Windows.Forms;
using System.Xml;
using Bloom.Book;
using Bloom.web;
using Gecko;
using Gecko.DOM;
using Gecko.Events;
using Palaso.IO;
using Palaso.Reporting;
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
		// A temporary object needed just as long as it is the content of this browser.
		// Currently may be a TempFile (a real filesystem file) or a SimulatedPageFile (just a dictionary entry).
		// It gets disposed when the Browser goes away.
		private IDisposable _dependentContent;
		private PasteCommand _pasteCommand;
		private CopyCommand _copyCommand;
		private  UndoCommand _undoCommand;
		private  CutCommand _cutCommand;
		private bool _disposed;
		public event EventHandler OnBrowserClick;
		public static event EventHandler XulRunnerShutdown;

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

			// BL-535: 404 error if system proxy settings not configured to bypass proxy for localhost
			// See: https://developer.mozilla.org/en-US/docs/Mozilla/Preferences/Mozilla_networking_preferences
			GeckoPreferences.User["network.proxy.http"] = string.Empty;
			GeckoPreferences.User["network.proxy.http_port"] = 80;
			GeckoPreferences.User["network.proxy.type"] = 1; // 0 = direct (uses system settings on Windows), 1 = manual configuration
			// Try some settings to reduce memory consumption by the mozilla browser engine.
			// Testing on Linux showed eventual substantial savings after several cycles of viewing
			// all the pages and then going to the publish tab and producing PDF files for several
			// books with embedded jpeg files.  (physical memory 1,153,864K, managed heap 37,789K
			// instead of physical memory 1,952,380K, managed heap 37,723K for stepping through the
			// same operations on the same books in the same order.  I don't know why managed heap
			// changed although it didn't change much.)
			// See http://kb.mozillazine.org/About:config_entries, http://www.davidtan.org/tips-reduce-firefox-memory-cache-usage
			// and http://forums.macrumors.com/showthread.php?t=1838393.
			GeckoPreferences.User["memory.free_dirty_pages"] = true;
			GeckoPreferences.User["browser.sessionhistory.max_entries"] = 1;
			GeckoPreferences.User["browser.sessionhistory.max_total_viewers"] = 0;
			GeckoPreferences.User["browser.cache.memory.enable"] = false;
			// These settings prevent a problem where the gecko instance running the add page dialog
			// would request several images at once, but we were not able to generate the image
			// because we could not make additional requests of the localhost server, since some limit
			// had been reached. I'm not sure all of them are needed, but since in this program we
			// only talk to our own local server, there is no reason to limit any requests to the server,
			// so increasing all the ones that look at all relevant seems like a good idea.
			GeckoPreferences.User["network.http.max-persistent-connections-per-server"] = 200;
			GeckoPreferences.User["network.http.pipelining.maxrequests"] = 200;
			GeckoPreferences.User["network.http.pipelining.max-optimistic-requests"] = 200;

			Application.ApplicationExit += OnApplicationExit;
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
		/// Allow creator to hook up this event handler if the browser needs to handle Ctrl-N.
		/// Not every browser instance needs this.
		/// </summary>
		public ControlKeyEvent ControlKeyEvent { get; set; }

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
			_pasteCommand.Implementer = () => PasteFilteredText(false);
			_undoCommand.Implementer = () =>
			{
				// Note: this is only used for the Undo button in the toolbar;
				// ctrl-z is handled in JavaScript directly.
				var result = RunJavaScript("(typeof calledByCSharp === 'undefined') ? 'undefined' : 'ok'");
				if (result == "ok")
				{
					RunJavaScript("calledByCSharp.handleUndo()");
				}
				else
				{
					_browser.Undo();
				}
			};
		}

		public void SaveHTML(string path)
		{
			if (InvokeRequired)
			{
				Invoke(new Action<string>(SaveHTML), path);
				return;
			}
			_browser.SaveDocument(path, "text/html");
		}

		public void UpdateEditButtons()
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
				var result = RunJavaScript("(typeof calledByCSharp === 'undefined') ? 'f' : 'y'");
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
			if (disposing)
			{
				if (_dependentContent != null)
				{
					_dependentContent.Dispose();
					_dependentContent = null;
				}
				if (components != null)
				{
					components.Dispose();
				}
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

			GeckoPreferences.User["mousewheel.withcontrolkey.action"] = 3;
			GeckoPreferences.User["browser.zoom.full"] = true;

			// in firefox 14, at least, there was a bug such that if you have more than one lang on
			// the page, all are check with English
			// until we get past that, it's just annoying
			GeckoPreferences.User["layout.spellcheckDefault"] = 0;

			RaiseGeckoReady();
	   }

		private void _browser_DocumentCompleted(object sender, EventArgs e)
		{
			//no: crashes (at least in Sept 2012) AutoZoom();
		}

		/// <summary>
		/// Prevent a CTRL+V pasting when we have the Paste button disabled, e.g. when pictures are on the clipboard.
		/// Also handle CTRL+N creating a new page on Linux/Mono.
		/// </summary>
		void OnDomKeyPress(object sender, DomKeyEventArgs e)
		{
			Debug.Assert(!InvokeRequired);
			const uint DOM_VK_INSERT = 0x2D;
			if ((e.CtrlKey && e.KeyChar == 'v') || (e.ShiftKey && e.KeyCode == DOM_VK_INSERT)) //someone was using shift-insert to do the paste
			{
				if (_pasteCommand==null /*happened in calendar config*/ || !_pasteCommand.Enabled)
				{
					Debug.WriteLine("Paste not enabled, so ignoring.");
					e.PreventDefault();
				}
				else if(_browser.CanPaste && BloomClipboard.ContainsText())
				{
					e.PreventDefault(); //we'll take it from here, thank you very much
					PasteFilteredText(false);
				}
			}
			// On Windows, Form.ProcessCmdKey (intercepted in Shell) seems to get ctrl messages even when the browser
			// has focus.  But on Mono, it doesn't.  So we just do the same thing as that Shell.ProcessCmdKey function
			// does, which is to raise this event.
			if (Palaso.PlatformUtilities.Platform.IsMono && ControlKeyEvent != null && e.CtrlKey && e.KeyChar == 'n')
			{
				Keys keyData = Keys.Control | Keys.N;
				ControlKeyEvent.Raise(keyData);
			}
		}

		private void PasteFilteredText(bool removeSingleLineBreaks)
		{
			//this prone to dying in System.Windows.Forms.Clipboard.SetText. E.g. bl-2787
			try
			{

				Debug.Assert(!InvokeRequired);

				//Remove everything from the clipboard except the unicode text (e.g. remove messy html from ms word)
				var text = BloomClipboard.GetText(TextDataFormat.UnicodeText);

				if (!string.IsNullOrEmpty(text))
				{
					if (removeSingleLineBreaks)
					{
						text = text.Replace("\r\n", " ").Replace("\n", " ").Replace("\r", " ");
					}
					//setting clears other formats that might be on the clipboard, such as html
					BloomClipboard.SetText(text, TextDataFormat.UnicodeText);
					_browser.Paste();
				}

			}
			catch (Exception error)
			{
				Logger.WriteEvent("***Failed to paste in Browser.PasteFilteredText()");
#if DEBUG
				throw error;
#endif
				Palaso.Reporting.ErrorReport.NotifyUserOfProblem(error, "There was a problem pasting from the clipboard.");
			}
		}

		/// <summary>
		/// This Function will be passed a GeckoContextMenuEventArgs to which appropriate menu items
		/// can be added. If it returns true these are in place of our standard extensions; if false, the
		/// standard ones will follow whatever it adds.
		/// </summary>
		public Func<GeckoContextMenuEventArgs, bool> ContextMenuProvider { get; set; }

		void OnShowContextMenu(object sender, GeckoContextMenuEventArgs e)
		{
			MenuItem FFMenuItem = null;
			Debug.Assert(!InvokeRequired);
			if (ContextMenuProvider != null)
			{
				var replacesStdMenu = ContextMenuProvider(e);
				FFMenuItem = e.ContextMenu.MenuItems.Add("Open Page in Firefox (which must be in the PATH environment variable)",
					new EventHandler(OnOpenPageInSystemBrowser));

				if (replacesStdMenu)
					return; // only the provider's items
			}
			var m = e.ContextMenu.MenuItems.Add("Edit Stylesheets in Stylizer", new EventHandler(OnOpenPageInStylizer));
			m.Enabled = !string.IsNullOrEmpty(GetPathToStylizer());

			if(FFMenuItem == null)
				e.ContextMenu.MenuItems.Add("Open Page in Firefox (which must be in the PATH environment variable)",
					new EventHandler(OnOpenPageInSystemBrowser));
#if DEBUG
			e.ContextMenu.MenuItems.Add("Open about:memory window", OnOpenAboutMemory);
#endif

			e.ContextMenu.MenuItems.Add("Copy Troubleshooting Information", new EventHandler(OnGetTroubleShootingInformation));
		}

		private void OnOpenAboutMemory(object sender, EventArgs e)
		{
			var form = new AboutMemory(Isolator);
			form.Text = "Bloom Browser Memory Diagnostics (\"about:memory\")";
			form.FirstLinkMessage = "See https://developer.mozilla.org/en-US/docs/Mozilla/Performance/about:memory for a basic explanation.";
			form.FirstLinkUrl = "https://developer.mozilla.org/en-US/docs/Mozilla/Performance/about:memory";
			form.SecondLinkMessage = "See https://developer.mozilla.org/en-US/docs/Mozilla/Performance/GC_and_CC_logs for more details.";
			form.SecondLinkUrl = "https://developer.mozilla.org/en-US/docs/Mozilla/Performance/GC_and_CC_logs";
			form.Navigate("about:memory");
			form.Show();	// NOT Modal!
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
			bool isWindows = Palaso.PlatformUtilities.Platform.IsWindows;
			string genericError = "Something went wrong trying to open this page in ";
			try
			{
				// An earlier version of this method made a new temp file in hopes that it would go on working
				// in the browser even after Bloom closed. This has gotten steadily less feasible as we depend
				// more on the http server. With the <base> element now removed, an independent page file will
				// have even more missing links. I don't think it's worth making a separate temp file any more.
				if (isWindows)
					Process.Start("Firefox.exe", '"' + _url + '"');
				else
					Process.Start("xdg-open", Uri.EscapeUriString(_url));
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
					BeginInvoke((Action)delegate()
					{
						MessageBox.Show(genericError + "the System Browser.");
					});

				}
			}
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
			var mouseEvent = e as Gecko.DomMouseEventArgs;
			var specialPasteClick = ModifierKeys.HasFlag(Keys.Control) || (mouseEvent!=null && mouseEvent.Button== GeckoMouseButton.Middle);
			if(_browser.CanPaste && BloomClipboard.ContainsText() && specialPasteClick)
			{
				e.PreventDefault();
				PasteFilteredText(true);
				return;
			}

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
			string url = e.Uri.OriginalString.ToLowerInvariant();

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
				SetNewDependent(TempFile.TrackExisting(url));
			}
			UpdateDisplay();
		}

		[DefaultValue(true)]
		public bool ScaleToFullWidthOfPage { get; set; }

		// NB: make sure you assigned HtmlDom.BaseForRelativePaths if the temporary document might
		// contain references to files in the directory of the original HTML file it is derived from,
		// 'cause that provides the information needed
		// to fake out the browser about where the 'file' is so internal references work.
		public void Navigate(HtmlDom htmlDom, HtmlDom htmlEditDom = null)
		{
			if (InvokeRequired)
			{
				Invoke(new Action<HtmlDom, HtmlDom>(Navigate), htmlDom, htmlEditDom);
				return;
			}

			XmlDocument dom = htmlDom.RawDom;
			XmlDocument editDom = htmlEditDom == null ? null : htmlEditDom.RawDom;

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
			var fakeTempFile = EnhancedImageServer.MakeSimulatedPageFileInBookFolder(htmlDom);
			SetNewDependent(fakeTempFile);
			_url = fakeTempFile.Key;
			UpdateDisplay();
		}

		public void NavigateRawHtml(string html)
		{
			if (InvokeRequired)
			{
				Invoke(new Action<string>(NavigateRawHtml), html);
				return;
			}

			var tf = TempFile.WithExtension("htm"); // For some reason Gecko won't recognize a utf-8 file as html unless it has the right extension
			File.WriteAllText(tf.Path,html, Encoding.UTF8);
			SetNewDependent(tf);
			_url = tf.Path;
			UpdateDisplay();
		}


		private static string GetZoomCSS(float scale)
		{
			//return "";
			return string.Format("-moz-transform: scale({0}); -moz-transform-origin: 0 0", scale.ToString(CultureInfo.InvariantCulture));
		}

		private void SetNewDependent(IDisposable dependent)
		{
			if(_dependentContent!=null)
			{
				try
				{
					_dependentContent.Dispose();
				}
				catch(Exception)
				{
						//not worth talking to the user about it. Just abandon it in the Temp directory.
#if DEBUG
					throw;
#endif
				}

			}
			_dependentContent = dependent;
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
			if (_browser.Window != null) // BL-2313 two Alt-F4s in a row while changing a folder name can do this
			{
				using (var context = new AutoJSContext(_browser.Window.JSContext))
				{
					string result;
					context.EvaluateScript(script, (nsISupports)_browser.Document.DomObject, out result);
					return result;
				}
			}
			return null;
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
			if (anchor.Href.ToLowerInvariant().StartsWith("http")) //will cover https also
			{
				Process.Start(anchor.Href);
				eventArgs.Handled = true;
				return;
			}
			if (anchor.Href.ToLowerInvariant().StartsWith("file"))
			//links to files are handled externally if we can tell they aren't html/javascript related
			{
				// TODO: at this point spaces in the file name will cause the link to fail.
				// That seems to be a problem in the DomEventArgs.Target.CastToGeckoElement() method.
				var href = anchor.Href;

				var path = href.Replace("file:///", "");

				if (new List<string>(new[] { ".pdf", ".odt", ".doc", ".docx", ".txt" }).Contains(Path.GetExtension(path).ToLowerInvariant()))
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
			else if (anchor.Href.ToLowerInvariant().StartsWith("mailto"))
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
