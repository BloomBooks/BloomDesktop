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
using Gecko;
using Gecko.DOM;
using Gecko.Events;
using SIL.IO;
using SIL.Reporting;
using SIL.Windows.Forms.Miscellaneous;
using L10NSharp;
using SimulatedPageFileSource = Bloom.Api.BloomServer.SimulatedPageFileSource;

namespace Bloom
{
	public partial class Browser : UserControl
	{
		protected GeckoWebBrowser _browser;
		bool _browserIsReadyToNavigate;
		private string _url;
		private string _replacedUrl;
		private XmlDocument _rootDom; // root DOM we navigate the browser to; typically a shell with other doms in iframes
		private XmlDocument _pageEditDom; // DOM, dypically in an iframe of _rootDom, which we are editing.
		// A temporary object needed just as long as it is the content of this browser.
		// Currently may be a TempFile (a real filesystem file) or a SimulatedPageFile (just a dictionary entry).
		// It gets disposed when the Browser goes away.
		private IDisposable _dependentContent;
		private PasteCommand _pasteCommand;
		private CopyCommand _copyCommand;
		private UndoCommand _undoCommand;
		private CutCommand _cutCommand;
		private bool _disposed;
		public event EventHandler OnBrowserClick;
		public static event EventHandler XulRunnerShutdown;
		// We need some way to pass the cursor location in the Browser context to the EditingView command handlers
		// for Add/Delete TextOverPicture textboxes. These will store the cursor location when the context menu is
		// generated.
		internal Point ContextMenuLocation;

		// TODO: refactor to use same initialization code as Palaso
		public static void SetUpXulRunner()
		{
			if (Xpcom.IsInitialized)
				return;
			string xulRunnerPath = Environment.GetEnvironmentVariable("XULRUNNER");
			if (String.IsNullOrEmpty(xulRunnerPath) || !Directory.Exists(xulRunnerPath))
			{
				var asm = Assembly.GetExecutingAssembly();
				var file = asm.CodeBase.Replace("file://", String.Empty);
				if (SIL.PlatformUtilities.Platform.IsWindows)
					file = file.TrimStart('/');
				var folder = Path.GetDirectoryName(file);
				xulRunnerPath = Path.Combine(folder, "Firefox");
			}
#if !__MonoCS__
			// This function seems to be newer than our Linux version of GeckoFx (as of Feb 2017, GeckFx45 rev 23 on Linux).
			// It somehow prevents a spurious complaint by the debugger that an exception inside XpCom.initialize() is not handled
			// (although it is).
			Xpcom.EnableProfileMonitoring = false;
#endif
			Xpcom.Initialize(xulRunnerPath);

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
			// Do NOT set this to zero. Somehow that disables following hyperlinks within a document (e.g., the ReadMe
			// for the template starter, BL-5321).
			GeckoPreferences.User["browser.sessionhistory.max_entries"] = 1;
			GeckoPreferences.User["browser.sessionhistory.max_total_viewers"] = 0;
			GeckoPreferences.User["browser.cache.memory.enable"] = false;

			// Some more settings that can help to reduce memory consumption.
			// (Tested in switching pages in the Edit tool.  These definitely reduce consumption in that test.)
			// See http://www.instantfundas.com/2013/03/how-to-keep-firefox-from-using-too-much.html
			// and http://kb.mozillazine.org/Memory_Leak.
			// maximum amount of memory used to cache decoded images
			GeckoPreferences.User["image.mem.max_decoded_image_kb"] = 40960;        // 40MB (default = 256000 == 250MB)
			// maximum amount of memory used by javascript
			GeckoPreferences.User["javascript.options.mem.max"] = 40960;            // 40MB (default = -1 == automatic)
			// memory usage at which javascript starts garbage collecting
			GeckoPreferences.User["javascript.options.mem.high_water_mark"] = 20;   // 20MB (default = 128 == 128MB)
			// SurfaceCache is an imagelib-global service that allows caching of temporary
			// surfaces. Surfaces normally expire from the cache automatically if they go
			// too long without being accessed.
			// 40MB is not enough for pdfjs to work reliably with some (large?) jpeg images with some test data.
			// (See https://silbloom.myjetbrains.com/youtrack/issue/BL-6247.)  That value was chosen arbitrarily
			// a couple of years ago, possibly to match image.mem.max_decoded_image_kb and javascript.options.mem.max
			// above.  It seemed to work okay until we stumbled across occasional books that refused to display their
			// jpeg files.  70MB was enough in my testing of a couple of those books, but let's go with 100MB since
			// other books may well need more.  (Mozilla seems to have settled on 1GB for the default surfacecache
			// size, but that doesn't appear to be needed in the Bloom context.)  Most Linux systems are 64-bit and
			// run a 64-bit version of of Bloom, while Bloom on Windows is still a 32-bit program regardless of the
			// system.  Since Windows Bloom uses Adobe Acrobat code to display PDF files, it doesn't need the larger
			// size for surfacecache, and that memory may be needed elsewhere.
			if (SIL.PlatformUtilities.Platform.IsLinux)
				GeckoPreferences.User["image.mem.surfacecache.max_size_kb"] = 102400;	// 100MB
			else
				GeckoPreferences.User["image.mem.surfacecache.max_size_kb"] = 40960;	// 40MB
			GeckoPreferences.User["image.mem.surfacecache.min_expiration_ms"] = 500;    // 500ms (default = 60000 == 60sec)

			// maximum amount of memory for the browser cache (probably redundant with browser.cache.memory.enable above, but doesn't hurt)
			GeckoPreferences.User["browser.cache.memory.capacity"] = 0;             // 0 disables feature

			// do these do anything?
			//GeckoPreferences.User["javascript.options.mem.gc_frequency"] = 5;	// seconds?
			//GeckoPreferences.User["dom.caches.enabled"] = false;
			//GeckoPreferences.User["browser.sessionstore.max_tabs_undo"] = 0;	// (default = 10)
			//GeckoPreferences.User["network.http.use-cache"] = false;

			// These settings prevent a problem where the gecko instance running the add page dialog
			// would request several images at once, but we were not able to generate the image
			// because we could not make additional requests of the localhost server, since some limit
			// had been reached. I'm not sure all of them are needed, but since in this program we
			// only talk to our own local server, there is no reason to limit any requests to the server,
			// so increasing all the ones that look at all relevant seems like a good idea.
			GeckoPreferences.User["network.http.max-persistent-connections-per-server"] = 200;
			GeckoPreferences.User["network.http.pipelining.maxrequests"] = 200;
			GeckoPreferences.User["network.http.pipelining.max-optimistic-requests"] = 200;

			// Graphite support was turned off by default in Gecko45. Back on in 49, but we don't have that yet.
			// We always want it, so may as well keep this permanently.
			GeckoPreferences.User["gfx.font_rendering.graphite.enabled"] = true;

			// This suppresses the normal zoom-whole-window behavior that Gecko normally does when using the mouse while
			// while holding crtl. Code in bloomEditing.js provides a more controlled zoom of just the body.
			GeckoPreferences.User["mousewheel.with_control.action"] = 0;

			// These two allow the sign language toolbox to capture a camera without asking the user's permission...
			// which we have no way to do, so it otherwise just fails.
			GeckoPreferences.User["media.navigator.enabled"] = true;
			GeckoPreferences.User["media.navigator.permission.disabled"] = true;

			// (In Geckofx60) Video is being rendered with a different thread to the main page.
			// However for some paint operations, the main thread temporary changes the ImageFactory on the container
			// (shared by both threads) to a BasicImageFactory, which is incompatible with the video decoding.
			// So if BasicImageFactory is set while a video image is being decoded, the decoding fails, resulting in
			// an unhelpful "Out of Memory" error.  If HW composing is on, then the main thread doesn't switch to the
			// BasicImageFactory, as composing is cheap (since FF is now using LAYERS_OPENGL on Linux instead of
			// LAYERS_BASIC).  [analysis courtesy of Tom Hindle]
			// This setting is needed only on Linux as far as we can tell.
			if (SIL.PlatformUtilities.Platform.IsLinux)
				GeckoPreferences.User["layers.acceleration.force-enabled"] = true;
		}

		public Browser()
		{
			InitializeComponent();
			_isolator = NavigationIsolator.GetOrCreateTheOneNavigationIsolator();
		}

		/// <summary>
		/// Allow creator to hook up this event handler if the browser needs to handle Ctrl-N.
		/// Not every browser instance needs this.
		/// </summary>
		public ControlKeyEvent ControlKeyEvent { get; set; }

		/// <summary>
		/// Singleton set by the constructor after designer setup, but before attempting navigation.
		/// </summary>
		private NavigationIsolator _isolator;

		public void SetEditingCommands(CutCommand cutCommand, CopyCommand copyCommand, PasteCommand pasteCommand, UndoCommand undoCommand)
		{
			_cutCommand = cutCommand;
			_copyCommand = copyCommand;
			_pasteCommand = pasteCommand;
			_undoCommand = undoCommand;

			_cutCommand.Implementer = () => _browser.CutSelection();
			_copyCommand.Implementer = () => _browser.CopySelection();
			_pasteCommand.Implementer = () => Paste();
			_undoCommand.Implementer = () =>
			{
				// Note: this is only used for the Undo button in the toolbar;
				// ctrl-z is handled in JavaScript directly.
				switch (CanUndoWithJavaScript)
				{
					case JavaScriptUndoState.Disabled: break; // this should not even have been called
					case JavaScriptUndoState.DependsOnBrowser:
						_browser.Undo();
						break;
					case JavaScriptUndoState.Enabled:
						RunJavaScript("FrameExports.handleUndo()");
						break;
				}
			};
		}

		//private string GetDomSelectionText()
		//{
		//	// It took me a whole afternoon to figure out the following 5 lines of javascript!
		//	// This only gets the text -- no HTML markup is included.  It appears that mozilla
		//	// uses the GTK clipboard internally on Linux which is what PortableClipboard uses,
		//	// so we don't need this function.  But in case we ever do need to get the DOM
		//	// selection text in C# code, I'm leaving this method here, commented out.
		//	var selectionText = RunJavaScript(
		//		"var root = window.parent || window;" +
		//		"var frame = root.document.getElementById('page');" +
		//		"var frameWindow = frame.contentWindow;" +
		//		"var frameDocument = frameWindow.document;" +
		//		"frameDocument.getSelection().toString();"
		//	);
		//	return selectionText;
		//}

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
				// BL-3658 GeckoFx-45 has a bug in the CanXSelection Properties, they always return 'true'
				// Tom Hindle suggested a workaround that seems to work.
				var isTextSelection = IsThereACurrentTextSelection();
				_cutCommand.Enabled = _browser != null && isTextSelection;
				_copyCommand.Enabled = _browser != null && isTextSelection;
				_pasteCommand.Enabled = _browser != null && _browser.CanPaste;
				if (_pasteCommand.Enabled)
				{
					//prevent pasting images (BL-93)
					_pasteCommand.Enabled = PortableClipboard.ContainsText();
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

		/// <summary>
		/// Workaround suggested by Tom Hindle, since GeckoFx-45's CanXSelection properties aren't working.
		/// </summary>
		/// <returns></returns>
		private bool IsThereACurrentTextSelection()
		{
			using (var win = new GeckoWindow(_browser.WebBrowserFocus.GetFocusedWindowAttribute()))
			{
				var sel = win.Selection;
				if (sel.IsCollapsed || sel.FocusNode is GeckoImageElement)
					return false;
			}
			return true;
		}

		enum JavaScriptUndoState
		{
			Disabled,
			Enabled,
			DependsOnBrowser
		}

		// Answer what we can determine about Undo from our JavaScript canUndo method.
		// Some Undo tasks are best handled in JavaScript; others, the best we can do is to use the browser's
		// built-in CanUndo and Undo. This method is used by both CanUndo and the actual Undo code (in SetEditingCommands)
		// to make sure that consistently we call FrameExports.handleUndo to implement undo if FrameExports.canUndo()
		// returns "yes"; if it returns "fail" we let the browser both determine whether Undo is possible and
		// implement Undo if so.
		// (Currently these are the only two things canUndo returns. However, it seemed marginally worth keeping the
		// previous logic that it could also return something else indicating that Undo is definitely not possible.)
		private JavaScriptUndoState CanUndoWithJavaScript
		{
			get
			{
				if (_browser == null)
					return JavaScriptUndoState.Disabled;
				var result = RunJavaScript("(typeof FrameExports === 'undefined' || typeof FrameExports.canUndo === 'undefined') ? 'f' : 'y'");
				if (result == "y")
				{
					result = RunJavaScript("FrameExports.canUndo()");
					if (result == "fail")
						return JavaScriptUndoState.DependsOnBrowser; // not using special Undo.
					return result == "yes" ? JavaScriptUndoState.Enabled : JavaScriptUndoState.Disabled;
				}
				return JavaScriptUndoState.DependsOnBrowser;
			}
		}

		private bool CanUndo
		{
			get
			{
				switch (CanUndoWithJavaScript)
				{
					case JavaScriptUndoState.Enabled:
						return true;
					case JavaScriptUndoState.Disabled:
						return false;
					case JavaScriptUndoState.DependsOnBrowser:
						return _browser.CanUndo;
					default:
						throw new ApplicationException("Illegal JavaScriptUndoState");
				}
			}
		}

		/// <summary>
		/// Clean up any resources being used.
		/// </summary>
		/// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
		protected override void Dispose(bool disposing)
		{
			if (_disposed)
				return;
			if (disposing)
			{
				if (_browser != null)
				{
					_browser.Dispose();
					_browser = null;
				}
				// Dispose of this AFTER the _browser. We've seen cases where, if we dispose _dependentContent first, some threading issue causes
				// the browser to request the simulated page (which is often the _dependentContent) AFTER it's disposed, leading to an error.
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

			if (DesignMode)
			{
				this.BackColor = Color.DarkGray;
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
			_browser.Navigated += CleanupAfterNavigation; //there's also a "document completed"
			_browser.DocumentCompleted += new EventHandler<GeckoDocumentCompletedEventArgs>(_browser_DocumentCompleted);

			_browser.ConsoleMessage += OnConsoleMessage;

			// This makes any zooming zoom everything, not just enlarge text.
			// May be obsolete, since I don't think we are using the sort of zooming it controls.
			// Instead we implement zoom ourselves in a more controlled way using transform: scale
			GeckoPreferences.User["browser.zoom.full"] = true;

			// in firefox 14, at least, there was a bug such that if you have more than one lang on
			// the page, all are check with English
			// until we get past that, it's just annoying
			GeckoPreferences.User["layout.spellcheckDefault"] = 0;

			_browser.FrameEventsPropagateToMainWindow = true; // we want clicks in iframes to propagate all the way up to C#

			AddMessageEventListener("jsNotification", ReceiveJsNotification);

			RaiseGeckoReady();
		}

		static Dictionary<string, List<Action>> _jsNotificationRequests = new Dictionary<string, List<Action>>();

		/// <summary>
		/// Allows some C# to receive a notification when Javascript (on any page) raises the jsNotification event,
		/// typically by calling fireCSharpEditEvent('jsNotification', id), where id corresponds to the string
		/// passed to this method.
		/// fireCSharpEditEvent is usually implemented as
		/// top.document.dispatchEvent(new MessageEvent(eventName, {"bubbles": true, "cancelable": true, "data": eventData });
		/// When that event is raised all the Actions queued for it are invoked once (and then forgotten).
		/// Thus, the expectation is that the caller is wanting to know about one single instance of the
		/// event occurring. There is therefore no need to clean up as with an event handler.
		/// </summary>
		/// <param name="id"></param>
		/// <param name="action"></param>
		public static void RequestJsNotification(string id, Action action)
		{
			List<Action> list;
			if (!_jsNotificationRequests.TryGetValue(id, out list))
			{
				list = new List<Action>();
				_jsNotificationRequests[id] = list;
			}
			list.Add(action);
		}

		/// <summary>
		/// This event is raised
		/// </summary>
		public event EventHandler PaintedPageNotificationFromJavaScript;

		private void ReceiveJsNotification(string id)
		{
			if (id == "editPagePainted")
				PaintedPageNotificationFromJavaScript?.Invoke(this, new EventArgs());
			List<Action> list;
			if (!_jsNotificationRequests.TryGetValue(id, out list))
				return;
			foreach (var action in list)
				action();
			_jsNotificationRequests.Remove(id);
		}

		// We'd like to suppress them just in one browser. But it seems to be unpredictable which
		// browser instance(s) get the messages when something goes wrong in one of them.
		public static Boolean SuppressJavaScriptErrors { get; set; }

		private void OnConsoleMessage(object sender, ConsoleMessageEventArgs e)
		{
			if(e.Message.StartsWith("[JavaScript Warning"))
				return;
			if (e.Message.StartsWith("[JavaScript Error"))
			{
				// BL-4737 Don't report websocket errors, but do send them to the Debug console.
				if (!SuppressJavaScriptErrors && !e.Message.Contains("has terminated unexpectedly. Some data may have been transferred."))
				{
					ReportJavaScriptError(new GeckoJavaScriptException(e.Message));
				}
			}
			Debug.WriteLine(e.Message);
		}

		private void _browser_DocumentCompleted(object sender, EventArgs e)
		{
		}

		/// <summary>
		/// Prevent a CTRL+V pasting when we have the Paste button disabled, e.g. when pictures are on the clipboard.
		/// Also handle CTRL+N creating a new page on Linux/Mono.
		/// </summary>
		void OnDomKeyPress(object sender, DomKeyEventArgs e)
		{
			Debug.Assert(!InvokeRequired);
			const uint DOM_VK_INSERT = 0x2D;

			//enhance: it's possible that, with the introduction of ckeditor, we don't need to pay any attention
			//to ctrl+v. I'm doing a hotfix to a beta here so I don't want to change more than necessary.
			if ((e.CtrlKey && e.KeyChar == 'v') || (e.ShiftKey && e.KeyCode == DOM_VK_INSERT)) //someone was using shift-insert to do the paste
			{
				// pasteCommand may well be null in minor instances of browser, such as configuring Wall Calendar.
				// allow the default Paste to do its best there (BL-5322)
				if (_pasteCommand != null)
				{
					if (!_pasteCommand.Enabled)
					{
						Debug.WriteLine("Paste not enabled, so ignoring.");
						e.PreventDefault();
					}
				}
				//otherwise, ckeditor will handle the paste
			}
			// On Windows, Form.ProcessCmdKey (intercepted in Shell) seems to get ctrl messages even when the browser
			// has focus.  But on Mono, it doesn't.  So we just do the same thing as that Shell.ProcessCmdKey function
			// does, which is to raise this event.
			if (SIL.PlatformUtilities.Platform.IsMono && ControlKeyEvent != null && e.CtrlKey && e.KeyChar == 'n')
			{
				Keys keyData = Keys.Control | Keys.N;
				ControlKeyEvent.Raise(keyData);
			}
		}

		private void Paste()
		{
			// Saved as an example of how to do a special paste. But since we introduced modules,
			// if we want this we have to get the ts code into the FrameExports system.
			//if (Control.ModifierKeys == Keys.Control)
			//{
			//	var text = PortableClipboard.GetText(TextDataFormat.UnicodeText);
			//	text = System.Web.HttpUtility.JavaScriptStringEncode(text);
			//	RunJavaScript("BloomField.CalledByCSharp_SpecialPaste('" + text + "')");
			//}
			//else
			//{
				//just let ckeditor do the MSWord filtering
				_browser.Paste();
			//}
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
			ContextMenuLocation = PointToClient(Cursor.Position);
			if (ContextMenuProvider != null)
			{
				var replacesStdMenu = ContextMenuProvider(e);
#if DEBUG
				FFMenuItem = AddOpenPageInFFItem(e);
#endif

				if (replacesStdMenu)
					return; // only the provider's items
			}
			var m = e.ContextMenu.MenuItems.Add("Edit Stylesheets in Stylizer", OnOpenPageInStylizer);
			m.Enabled = !String.IsNullOrEmpty(GetPathToStylizer());

			if(FFMenuItem == null)
				AddOpenPageInFFItem(e);
#if DEBUG
			var _addDebuggingMenuItems = true;
#else
			var debugBloom = Environment.GetEnvironmentVariable("DEBUGBLOOM");
			var _addDebuggingMenuItems = !String.IsNullOrEmpty(debugBloom) && debugBloom.ToLowerInvariant() != "false" && debugBloom.ToLowerInvariant() != "no";
#endif
			if (_addDebuggingMenuItems)
				AddOtherMenuItemsForDebugging(e);

			e.ContextMenu.MenuItems.Add(LocalizationManager.GetString("Browser.CopyTroubleshootingInfo", "Copy Troubleshooting Information"), OnGetTroubleShootingInformation);
		}

		private MenuItem AddOpenPageInFFItem(GeckoContextMenuEventArgs e)
		{
			return e.ContextMenu.MenuItems.Add(
				LocalizationManager.GetString("Browser.OpenPageInFirefox", "Open Page in Firefox (which must be in the PATH environment variable)"),
				OnOpenPageInSystemBrowser);
		}

		private void AddOtherMenuItemsForDebugging(GeckoContextMenuEventArgs e)
		{
			e.ContextMenu.MenuItems.Add("Open about:memory window", OnOpenAboutMemory);
			e.ContextMenu.MenuItems.Add("Open about:config window", OnOpenAboutConfig);
			e.ContextMenu.MenuItems.Add("Open about:cache window", OnOpenAboutCache);
			e.ContextMenu.MenuItems.Add("Refresh", OnRefresh);
		}

		private void OnRefresh(object sender, EventArgs e)
		{
			_browser.Reload();
		}

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
			PortableClipboard.SetText(builder.ToString());

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
			bool isWindows = SIL.PlatformUtilities.Platform.IsWindows;
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
					SIL.Program.Process.SafeStart("xdg-open", Uri.EscapeUriString(_url));
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
			RobustFile.Copy(_url, path,true); //we make a copy because once Bloom leaves this page, it will delete it, which can be an annoying thing to have happen your editor
			Process.Start(GetPathToStylizer(), path);
		}

		public static string GetPathToStylizer()
		{
			return FileLocationUtilities.LocateInProgramFiles("Stylizer.exe", false, new string[] { "Skybound Stylizer 5" });
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
			string url = e.Uri.OriginalString.ToLowerInvariant();

			if ((!url.StartsWith(BloomServer.ServerUrlWithBloomPrefixEndingInSlash)) && (url.StartsWith("http")))
			{
				e.Cancel = true;
				SIL.Program.Process.SafeStart(e.Uri.OriginalString); //open in the system browser instead
				Debug.WriteLine("Navigating " + e.Uri);
			}
			// Check for a simulated file that has been replaced before being displayed.
			// See http://issues.bloomlibrary.org/youtrack/issue/BL-4268.
			if (_replacedUrl != null && _replacedUrl.ToLowerInvariant() == url)
			{
				e.Cancel = true;
				Debug.WriteLine("Navigating to expired " + e.Uri.OriginalString + " cancelled");
			}
		}

		private void CleanupAfterNavigation(object sender, GeckoNavigatedEventArgs e)
		{
			Debug.Assert(!InvokeRequired);

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
		}

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

			_url = url; //TODO: fix up this hack. We found that deleting the pdf while we're still showing it is a bad idea.
			if(cleanupFileAfterNavigating && !_url.EndsWith(".pdf"))
			{
				SetNewDependent(TempFile.TrackExisting(url));
			}
			UpdateDisplay();
		}

		public static void ClearCache()
		{
            try
            {
				// Review: is this supposed to say "netwerk" or "network"?
				// Haven't found a clear answer; https://hg.mozilla.org/releases/mozilla-release/rev/496aaf774697f817a689ee0d59f2f866fdb16801
				// seems to indicate that both may be supported.
				var instance = Xpcom.CreateInstance<nsICacheStorageService>("@mozilla.org/netwerk/cache-storage-service;1");
                instance.Clear();
            }
            catch (InvalidCastException e)
            {
                // For some reason, Release builds (only) sometimes run into this when uploading.
                // Don't let it stop us just to clear a cache.
				// Todo Gecko60: see if we can get rid of these catch clauses.
                Logger.WriteError(e);
            }
            catch (NullReferenceException e)
            {
				// Similarly, the Harvester has run into this one, and ignoring it doesn't seem to have been a problem.
				Logger.WriteError(e);
			}

        }

		public void SetEditDom(HtmlDom editDom)
		{
			_pageEditDom = editDom.RawDom;
		}

		private bool _hasNavigated;

		// NB: make sure you assigned HtmlDom.BaseForRelativePaths if the temporary document might
		// contain references to files in the directory of the original HTML file it is derived from,
		// 'cause that provides the information needed
		// to fake out the browser about where the 'file' is so internal references work.
		public void Navigate(HtmlDom htmlDom, HtmlDom htmlEditDom = null, bool setAsCurrentPageForDebugging = false,
			SimulatedPageFileSource source = SimulatedPageFileSource.Nav)
		{
			if (InvokeRequired)
			{
				Invoke(new Action<HtmlDom, HtmlDom, bool, SimulatedPageFileSource>(Navigate), htmlDom, htmlEditDom, setAsCurrentPageForDebugging, source);
				return;
			}

			// Make sure the browser is ready for business. Sometimes a newly created one is briefly busy.
			if (!_hasNavigated)
			{
				_hasNavigated = true;
				// gets WebBrowser created, if not already done.
				var dummy = Handle;

				// Without this block, I've seen a situation where the newly created WebBrowser is not ready
				// just long enough so that when we actually ask it to navigate, it doesn't do anything.
				// Then it will never finish, which was a causing timeouts in code like NavigateAndWaitTillDone().
				// As of 18 July 2018, that can STILL happen, so I'm not entirely sure this helps.
				// But it seemed to reduce the frequency somewhat, so I'm keeping it for now.
				var startupTimer = new Stopwatch();
				startupTimer.Start();
				while (_browser.IsBusy && startupTimer.ElapsedMilliseconds < 1000)
				{
					Application.DoEvents(); // NOTE: this has bad consequences all down the line. See BL-6122.
					Application.RaiseIdle(new EventArgs()); // needed on Linux to avoid deadlock starving browser navigation
				}
				startupTimer.Stop();
				if (_browser.IsBusy)
				{
					// I don't think I've seen this actually happen.
					Debug.WriteLine("New browser still busy after a second");
				}
			}

			XmlDocument dom = htmlDom.RawDom;
			XmlDocument editDom = htmlEditDom == null ? null : htmlEditDom.RawDom;

			_rootDom = dom;//.CloneNode(true); //clone because we want to modify it a bit
			_pageEditDom = editDom ?? dom;

			XmlHtmlConverter.MakeXmlishTagsSafeForInterpretationAsHtml(dom);
			var fakeTempFile = BloomServer.MakeSimulatedPageFileInBookFolder(htmlDom, setAsCurrentPageForDebugging: setAsCurrentPageForDebugging, source:source);
			SetNewDependent(fakeTempFile);
			_url = fakeTempFile.Key;
			UpdateDisplay();
		}

		public bool NavigateAndWaitTillDone(HtmlDom htmlDom, int timeLimit, string source = "nav", Func<bool> cancelCheck = null, bool throwOnTimeout = true)
		{
			// Should be called on UI thread. Since it is quite typical for this method to create the
			// window handle and browser, it can't do its own Invoke, which depends on already having a handle.
			// OTOH, Unit tests are often not run on the UI thread (and would therefore just pop up annoying asserts).
			Debug.Assert(Program.RunningOnUiThread || Program.RunningUnitTests || Program.RunningInConsoleMode, "Should be running on UI Thread or Unit Tests or Console mode");
			var dummy = Handle; // gets WebBrowser created, if not already done.
			var done = false;
			var navTimer = new Stopwatch();
			navTimer.Start();
			_hasNavigated = false;
			_browser.DocumentCompleted += (sender, args) => done = true;
			// just in case something goes wrong, avoid the timeout if it fails rather than completing.
			_browser.NavigationError += (sender, e) => done = true;
			// var oldUrl = _browser.Url; // goes with commented out code below
			Navigate(htmlDom, source: SimulatedPageFileSource.Epub);
			// If done is set (by NavigationError?) prematurely, we still need to wait while IsBusy
			// is true to give the loaded document time to become available for the checks later.
			// See https://issues.bloomlibrary.org/youtrack/issue/BL-8741.
			while ((!done || _browser.IsBusy) && navTimer.ElapsedMilliseconds < timeLimit)
			{
				Application.DoEvents(); // NOTE: this has bad consequences all down the line. See BL-6122.
				Application.RaiseIdle(new EventArgs()); // needed on Linux to avoid deadlock starving browser navigation
				if (cancelCheck != null && cancelCheck())
				{
					navTimer.Stop();
					return false;
				}
				// Keeping this code as a reminder: it seems to be a reliable way of telling when
				// the nothing happens when told to navigate problem is rearing its ugly head.
				// But I'm not sure enough to throw what might be a premature exception.
				//if (navTimer.ElapsedMilliseconds > 1000 && _browser.Url == oldUrl)
				//{
				//	throw new ApplicationException("Browser isn't even trying to navigate");
				//}
			}
			navTimer.Stop();

			if (!done)
			{
				if (throwOnTimeout)
					throw new ApplicationException("Browser unexpectedly took too long to load a page");
				else return false;
			}

			return true;
		}

		public void NavigateRawHtml(string html)
		{
			if (InvokeRequired)
			{
				Invoke(new Action<string>(NavigateRawHtml), html);
				return;
			}

			var tf = TempFile.WithExtension("htm"); // For some reason Gecko won't recognize a utf-8 file as html unless it has the right extension
			RobustFile.WriteAllText(tf.Path,html, Encoding.UTF8);
			SetNewDependent(tf);
			_url = tf.Path;
			UpdateDisplay();
		}

		private void SetNewDependent(IDisposable dependent)
		{
			// Save information needed to prevent http://issues.bloomlibrary.org/youtrack/issue/BL-4268.
			var simulated = _dependentContent as SimulatedPageFile;
			_replacedUrl = (simulated != null) ? simulated.Key : null;

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
				_isolator.Navigate(_browser, _url);
			}
		}

		/// <summary>
		/// What's going on here: the browser is just editing/displaying a copy of one page of the document.
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
					var frameElement = _browser.Window?.Document?.GetElementById("page") as GeckoIFrameElement;
					if (frameElement == null)
						return;
					// contentDocument = frameElement.ContentDocument; unreliable in Gecko45
					contentDocument = (GeckoDocument) frameElement.ContentWindow.Document; // TomH says this will always succeed
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
				if (body.Length ==0)	//review: a previous comment said this could happen in OnValidating, but that is gone...may be obsolete
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
				var expectedPageId = destinationDomPage.Attributes["id"].Value;

				var browserDomPage = bodyDom.SelectSingleNode("//body//div[contains(@class,'bloom-page')]");
				if (browserDomPage == null)
					return;//why? but I've seen it happen

				var thisPageId = browserDomPage.Attributes["id"].Value;
				if(expectedPageId != thisPageId)
				{
					SIL.Reporting.ErrorReport.NotifyUserOfProblem(LocalizationManager.GetString("Browser.ProblemSaving",
						"There was a problem while saving. Please return to the previous page and make sure it looks correct."));
					return;
				}
				_pageEditDom.GetElementsByTagName("body")[0].InnerXml = bodyDom.InnerXml;

				var userModifiedStyleSheet = contentDocument.StyleSheets.FirstOrDefault(s =>
					{
						// We used to have a workaround here for a bug in geckofx-29, but since newer geckos work fine
						// I'd (gjm) like to go with what's clearer now.
						//var titleNode = s.OwnerNode.EvaluateXPath("@title").GetNodes().FirstOrDefault();
						var titleNode = s.OwnerNode.EvaluateXPath("@title").GetSingleNodeValue();
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

		public const string  CdataPrefix = "/*<![CDATA[*/";
		public const string CdataSuffix = "/*]]>*/";

		private void SaveCustomizedCssRules(GeckoStyleSheet userModifiedStyleSheet)
		{
			try
			{
				/* why are we bothering to walk through the rules instead of just copying the html of the style tag? Because that doesn't
				 * actually get updated when the javascript edits the stylesheets of the page. Well, the <style> tag gets created, but
				 * rules don't show up inside of it. So
				 * this won't work: _editDom.GetElementsByTagName("head")[0].InnerText = userModifiedStyleSheet.OwnerNode.OuterHtml;
				 */
				var outerStyleElementString = new StringBuilder();
				outerStyleElementString.AppendLine("<style title='userModifiedStyles' type='text/css'>");
				var innerCssStylesString = new StringBuilder();
				foreach (var cssRule in userModifiedStyleSheet.CssRules)
				{
					innerCssStylesString.AppendLine(cssRule.CssText);
				}
				outerStyleElementString.Append(WrapUserStyleInCdata(innerCssStylesString.ToString()));
				outerStyleElementString.AppendLine("</style>");
				//Debug.WriteLine("*User Modified Stylesheet in browser:" + styles);
				_pageEditDom.GetElementsByTagName("head")[0].InnerXml = outerStyleElementString.ToString();
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

		/// <summary>
		/// Wraps the inner css styles for userModifiedStyles in commented CDATA so we can handle invalid
		/// xhtml characters like >.
		/// </summary>
		public static string WrapUserStyleInCdata(string innerCssStyles)
		{
			if (innerCssStyles.StartsWith(CdataPrefix))
			{
				// For some reason, we are already wrapped in CDATA.
				// Could happen in HtmlDom.MergeUserStylesOnInsertion().
				return innerCssStyles;
			}
			// Now, our styles string may contain invalid xhtml characters like >
			// We shouldn't have &gt; in XHTML because the content of <style> is supposed to be CSS, and &gt; is an HTML escape.
			// And in XElement we can't just have > like we can in HTML (<style> is PCDATA, not CDATA).
			// So, we want to mark the main body of the rules as <![CDATA[ ...]]>, within which we CAN have >.
			// But, once again, that's HTML markup that's not valid CSS. To fix it we wrap each of the markers
			// in CSS comments, so the wrappers end up as /*<![CDATA[*/.../*]]>*/.
			var cdataString = new StringBuilder();
			cdataString.AppendLine(CdataPrefix);
			cdataString.Append(innerCssStyles); // Not using AppendLine, since innerCssStyles is likely several lines
			cdataString.AppendLine(CdataSuffix);
			return cdataString.ToString();
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
			if (!RobustFile.Exists(Path.Combine(Path.GetDirectoryName(_url), filename)))
				throw new FileNotFoundException(filename);

			GeckoDocument doc = WebBrowser.Document;
			var head = doc.GetElementsByTagName("head").First();
			GeckoScriptElement script = doc.CreateElement("script") as GeckoScriptElement;
			// Geckofx60 doesn't implement the GeckoScriptElement .Type and .Src properties
			script.SetAttribute("type", "text/javascript");
			script.SetAttribute("src", filename);
			head.AppendChild(script);
		}

		public void AddScriptContent(string content)
		{
			Debug.Assert(!InvokeRequired);
			GeckoDocument doc = WebBrowser.Document;
			var head = doc.GetElementsByTagName("head").First();
			GeckoScriptElement script = doc.CreateElement("script") as GeckoScriptElement;
			// Geckofx60 doesn't implement the GeckoScriptElement .Type and .Text properties
			script.SetAttribute("type", "text/javascript");
			script.TextContent = content;
			head.AppendChild(script);
		}

		public string RunJavaScript(string script)
		{
			Debug.Assert(!InvokeRequired);
			return RunJavaScriptOn(_browser, script);
		}

		public static string RunJavaScriptOn(GeckoWebBrowser geckoWebBrowser, string script)
		{
			// Review JohnT: does this require integration with the NavigationIsolator?
			if (geckoWebBrowser != null && geckoWebBrowser.Window != null) // BL-2313 two Alt-F4s in a row while changing a folder name can do this
			{
				try
				{
					using (var context = new AutoJSContext(geckoWebBrowser.Window))
					{
						var jsValue = context.EvaluateScript(script, (nsISupports)geckoWebBrowser.Window.DomWindow,
							(nsISupports)geckoWebBrowser.Document.DomObject);
						if (!jsValue.IsString)
							return null;
						// This bit of magic was borrowed from GeckoFx's AutoJsContext.ConvertValueToString (which changed in Geckofx60).
						// Unfortunately the more convenient version of EvaluateScript which returns a string also eats exceptions
						// (though it does return a boolean...we want the stack trace, though.)
						return SpiderMonkey.JsValToString(context.ContextPointer, jsValue);
					}
				}
				catch (GeckoJavaScriptException ex)
				{
					ReportJavaScriptError(ex, script);
				}
			}
			return null;
		}

		private static void ReportJavaScriptError(GeckoJavaScriptException ex, string script = null)
		{
			// For now unimportant JS errors are still quite common, sadly. Per BL-4301, we don't want
			// more than a toast, even for developers. But they should now be reported through CommonApi.HandleJavascriptError.
			// Any that still come here we want to know about.
			// But, mysteriously, we're still getting more than we can deal with, so going back to toast-only for now.
			// This one is particularly common while playing videos, and seems harmless, and being from the depths of
			// Gecko, not something we can do anything about. (We've observed these coming at least 27 times per second,
			// so decided not to clutter the log with them.)
			if (ex.Message.Contains("file: \"chrome://global/content/bindings/videocontrols.xml\""))
				return;
			// This error is one we can't do anything about that doesn't seem to hurt anything.  It frequently happens when
			// Bloom is just sitting idle with the user occupied elsewhere. (BL-7076)
			if (ex.Message.Contains("Async statement execution returned with '1', 'no such table: moz_favicons'"))
				return;
			// This one apparently can't be stopped in Javascript  (it comes from WebSocketManager.getOrCreateWebSocket).
			// It's something to do with preventing malicious code from probing for open sockets.
			// We get it quite often when testers are clicking around much too fast and pages become obsolete
			// before they finish loading.
			if (ex.Message.Contains("JavaScript Error: \"The connection was refused when attempting to contact"))
			{
				Logger.WriteError(ex);
				return;
			}

			var longMsg = ex.Message;
			if (script != null)
				longMsg = string.Format("Script=\"{0}\"{1}Exception message = {2}", script, Environment.NewLine, ex.Message);
			NonFatalProblem.Report(ModalIf.None, PassiveIf.Alpha, "A JavaScript error occurred and was missed by our onerror handler", longMsg, ex);
		}

		HashSet<string> _knownEvents = new HashSet<string>();

		/// <summary>
		/// Only the first call per browser per event name takes effect.
		/// (Unless RemoveMessageEventListener is called explicitly for the event name.)
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

		/// <summary>
		/// Remove a previously installed event handler.
		/// </summary>
		/// <param name="eventName"></param>
		public void RemoveMessageEventListener(string eventName)
		{
			if (_browser != null)
			{
				_browser.RemoveMessageEventListener(eventName);
				_knownEvents.Remove(eventName);
			}
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
				SIL.Program.Process.SafeStart(anchor.Href);
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
			var foundAndika = false;
			using (var installedFontCollection = new InstalledFontCollection())
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
					foundAndika |= family.Name == "Andika New Basic";

					yield return family.Name;
				}
			}
			if(!foundAndika) // see BL-3674. We want to offer Andika even if the Andika installer isn't finished yet.
			{	// it's possible that the user actually uninstalled Andika, but that's ok. Until they change to another font,
				// they'll get a message that this font is not actually installed when they try to edit a book.
				Logger.WriteMinorEvent("Andika not installed (BL-3674)");
				yield return "Andika New Basic";
			}
		}
	}
}
