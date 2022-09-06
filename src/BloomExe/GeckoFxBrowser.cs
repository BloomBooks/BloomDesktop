using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Windows.Forms;
using Bloom.Book;
using Bloom.Api;
using Bloom.Edit;
using Gecko;
using Gecko.Events;
using SIL.Reporting;
using SIL.Windows.Forms.Miscellaneous;

namespace Bloom
{
	public partial class GeckoFxBrowser : Browser
	{
		protected GeckoWebBrowser _browser;
		bool _browserIsReadyToNavigate;
		protected string _url;

		private bool _offScreen = false;

		private PasteCommand _pasteCommand;
		private CopyCommand _copyCommand;
		private UndoCommand _undoCommand;
		private CutCommand _cutCommand;
		private bool _disposed;

		// We need some way to pass the cursor location in the Browser context to the EditingView command handlers
		// for Add/Delete TextOverPicture textboxes. These will store the cursor location when the context menu is
		// generated.

		public static string DefaultBrowserLangs;

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
			GeckoPreferences.User["network.proxy.type"] =
				1; // 0 = direct (uses system settings on Windows), 1 = manual configuration
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
			GeckoPreferences.User["image.mem.max_decoded_image_kb"] = 40960; // 40MB (default = 256000 == 250MB)
			// maximum amount of memory used by javascript
			GeckoPreferences.User["javascript.options.mem.max"] = 40960; // 40MB (default = -1 == automatic)
			// memory usage at which javascript starts garbage collecting
			GeckoPreferences.User["javascript.options.mem.high_water_mark"] = 20; // 20MB (default = 128 == 128MB)
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
				GeckoPreferences.User["image.mem.surfacecache.max_size_kb"] = 102400; // 100MB
			else
				GeckoPreferences.User["image.mem.surfacecache.max_size_kb"] = 40960; // 40MB
			GeckoPreferences.User["image.mem.surfacecache.min_expiration_ms"] = 500; // 500ms (default = 60000 == 60sec)

			// maximum amount of memory for the browser cache (probably redundant with browser.cache.memory.enable above, but doesn't hurt)
			GeckoPreferences.User["browser.cache.memory.capacity"] = 0; // 0 disables feature

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

			// Save the default system language tags for later use.
			DefaultBrowserLangs = GeckoPreferences.User["intl.accept_languages"].ToString();
		}

		public override bool IsReadyToNavigate => WebBrowser != null;

		public static void SetBrowserLanguage(string langId)
		{
			var defaultLangs = DefaultBrowserLangs.Split(',');
			if (defaultLangs.Contains(langId))
			{
				var newLangs = new StringBuilder();
				newLangs.Append(langId);
				foreach (var lang in defaultLangs)
				{
					if (lang != langId)
						newLangs = newLangs.AppendFormat(",{0}", lang);
				}

				GeckoPreferences.User["intl.accept_languages"] = newLangs.ToString();
			}
			else
			{
				GeckoPreferences.User["intl.accept_languages"] = langId + "," + DefaultBrowserLangs;
			}
		}

		public GeckoFxBrowser(bool offScreen = false)
		{
#if __MonoCS__
			_offScreen = offScreen;
			if (_offScreen)
				_browser = new OffScreenGeckoWebBrowser();	// can't wait for loading
#endif
			InitializeComponent();
			_isolator = NavigationIsolator.GetOrCreateTheOneNavigationIsolator();
		}

		// previously clients had to access the Handle to make the control ready to talk to,
		// this just isolates that a bit
		public override void EnsureHandleCreated()
		{
			var x = this.Handle; // gets the WebBrowser created
		}

		/// <summary>
		/// Singleton set by the constructor after designer setup, but before attempting navigation.
		/// </summary>
		private NavigationIsolator _isolator;

		public override void SetEditingCommands(CutCommand cutCommand, CopyCommand copyCommand,
			PasteCommand pasteCommand, UndoCommand undoCommand)
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
						RunJavaScript("editTabBundle.handleUndo()");
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

		public override void SaveHTML(string path)
		{
			if (InvokeRequired)
			{
				Invoke(new Action<string>(SaveHTML), path);
				return;
			}

			_browser.SaveDocument(path, "text/html");
		}

		public override void UpdateEditButtons()
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
		/// We configure something in Javascript to keep track of this, since GeckoFx-45's CanXSelection properties aren't working,
		/// and a workaround involving making a GeckoWindow object and querying its selection led to memory leaks (BL-9757).
		/// </summary>
		/// <returns></returns>
		private bool IsThereACurrentTextSelection()
		{
			return EditingModel.IsTextSelected;
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
		// to make sure that consistently we call editTabBundle.handleUndo to implement undo if editTabBundle.canUndo()
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
				var result =
					RunJavaScript(
						"(typeof editTabBundle === 'undefined' || typeof editTabBundle.canUndo === 'undefined') ? 'f' : 'y'");
				if (result == "y")
				{
					result = RunJavaScript("editTabBundle.canUndo()");
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
					// no need to disconnect event handlers that are connect to this instance's methods.
					_browser.Dispose();
					_browser = null;
				}

				if (components != null)
				{
					components.Dispose();
				}

				Application.Idle -= Application_Idle; // just in case...  Multiple disconnects hurt nothing.
			}

			// Do this AFTER the _browser. We've seen cases where, if we dispose _dependentContent first (in base class), some threading issue causes
			// the browser to request the simulated page (which is often the _dependentContent) AFTER it's disposed, leading to an error.
			base.Dispose(disposing);
			_disposed = true;
		}

		public GeckoWebBrowser WebBrowser
		{
			get { return _browser; }
		}

		protected override void OnLoad(EventArgs e)
		{
			Debug.Assert(!InvokeRequired);
			base.OnLoad(e);

			if (DesignMode)
			{
				this.BackColor = Color.DarkGray;
				return;
			}

			if (!_offScreen)
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
			_browser.DocumentCompleted += _browser_DocumentCompleted;
			_browser.ReadyStateChange += BrowserOnReadyStateChange;

			_browser.ConsoleMessage += OnConsoleMessage;

			// This makes any zooming zoom everything, not just enlarge text.
			// May be obsolete, since I don't think we are using the sort of zooming it controls.
			// Instead we implement zoom ourselves in a more controlled way using transform: scale
			GeckoPreferences.User["browser.zoom.full"] = true;

			// in firefox 14, at least, there was a bug such that if you have more than one lang on
			// the page, all are check with English
			// until we get past that, it's just annoying
			GeckoPreferences.User["layout.spellcheckDefault"] = 0;

			_browser.FrameEventsPropagateToMainWindow =
				true; // we want clicks in iframes to propagate all the way up to C#

			RaiseBrowserReady();
		}

		private void BrowserOnReadyStateChange(object sender, DomEventArgs e)
		{
			// In GeckoFx, ready state change seems to be a more reliable way of detecting document complete.
			// If it's reached that state, raise the event.
			if (_browser.Document.ReadyState != "complete")
				return;
			RaiseDocumentCompleted(sender, e);
		}

		private void _browser_DocumentCompleted(object sender, GeckoDocumentCompletedEventArgs e)
		{
			RaiseDocumentCompleted(sender, e);
		}

		protected override void RaiseDocumentCompleted(object sender, EventArgs e)
		{
			// If it isn't really complete, don't tell the client it is!
			if (_browser.Document.ReadyState != "complete")
				return;
			base.RaiseDocumentCompleted(this, e);
		}

		// We'd like to suppress them just in one browser. But it seems to be unpredictable which
		// browser instance(s) get the messages when something goes wrong in one of them.
		public static Boolean SuppressJavaScriptErrors { get; set; }

		public override void ActivateFocussed() // review what should this be called?
		{
			_browser.WebBrowserFocus.Activate();
		}

		private void OnConsoleMessage(object sender, ConsoleMessageEventArgs e)
		{
			if (e.Message.StartsWith("[JavaScript Warning"))
				return;
			if (e.Message.StartsWith("[JavaScript Error"))
			{
				// BL-4737 Don't report websocket errors, but do send them to the Debug console.
				if (!SuppressJavaScriptErrors &&
				    !e.Message.Contains("has terminated unexpectedly. Some data may have been transferred."))
				{
					ReportJavaScriptError(new GeckoJavaScriptException(e.Message));
				}
			}

			Debug.WriteLine(e.Message);
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
			if ((e.CtrlKey && e.KeyChar == 'v') ||
			    (e.ShiftKey && e.KeyCode == DOM_VK_INSERT)) //someone was using shift-insert to do the paste
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
#if __MonoCS__
			_lastKeypressWasCtrlM = e.CtrlKey && e.KeyChar == 'm';	// m for menu...
#endif
		}

#if __MonoCS__
		private bool _lastKeypressWasCtrlM;
		/// <summary>
		/// The overridden method uses code that only works on Windows to determine whether the CTRL-key
		/// is down during a right click.  This is not exactly equivalent but the best we can do on Linux:
		/// pressing Ctrl-M enables native menus until the next keypress.  The different behavior affects
		/// only developers.  Note that OnDomKeyDown and OnDomKeyUp don't seem to work reliably when I
		/// tested them.
		/// </summary>
		override protected bool WantNativeMenu
		{
			get
			{
				return !_lastKeypressWasCtrlM && ContextMenuProvider == null;
			}
		}
#endif

		private void Paste()
		{
			// Saved as an example of how to do a special paste. But since we introduced modules,
			// if we want this we have to get the ts code into the editTabBundle system.
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

		public override string Url => _url;
		public override Bitmap GetPreview()
		{
			var creator = new ImageCreator(_browser);
			byte[] imageBytes = creator.CanvasGetPngImage((uint)_browser.Width, (uint)_browser.Height);
			using (var stream = new MemoryStream(imageBytes))
			{
				return new Bitmap(stream);
			}
		}

		public override void CopySelection()
		{
			_browser.CopySelection();
		}

		public override void SelectAll()
		{
			_browser.SelectAll();
		}

		public override void SelectBrowser()
		{
			_browser.Select();
		}

		void OnShowContextMenu(object sender, GeckoContextMenuEventArgs e)
		{
			AdjustContextMenu(e.TargetNode, new GeckoMenuItemAdder(e.ContextMenu));
		}

		public override void OnRefresh(object sender, EventArgs e)
		{
			_browser.Reload();
		}

		public override void SaveDocument(string path)
		{
			_browser.SaveDocument(path);
		}

		void OnBrowser_DomClick(object sender, DomEventArgs e)
		{
			Debug.Assert(!InvokeRequired);
			//this helps with a weird condition: make a new page, click in the text box, go over to another program, click in the box again.
			//it loses its focus.
			_browser.WebBrowserFocus.Activate(); //trying to help the disappearing cursor problem

			// We deliberately don't pass on the DomEventArgs, since a current goal is
			// to make sure no click handler depends on this Gecko-specific information.
			RaiseBrowserClick(sender, new EventArgs());
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

			Application.Idle += Application_Idle;

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

			Application.Idle -= Application_Idle;
		}

		public static void ClearCache()
		{
			try
			{
				// Review: is this supposed to say "netwerk" or "network"?
				// Haven't found a clear answer; https://hg.mozilla.org/releases/mozilla-release/rev/496aaf774697f817a689ee0d59f2f866fdb16801
				// seems to indicate that both may be supported.
				var instance =
					Xpcom.CreateInstance<nsICacheStorageService>("@mozilla.org/netwerk/cache-storage-service;1");
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


		private bool _hasNavigated;

		protected override void UpdateDisplay(string newUrl)
		{
			_url = newUrl;
			UpdateDisplay();
		}

		protected override void EnsureBrowserReadyToNavigate()
		{
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
				while ((_browser == null || _browser.IsBusy) && startupTimer.ElapsedMilliseconds < 1000)
				{
					Application.DoEvents(); // NOTE: this has bad consequences all down the line. See BL-6122.
					Application.RaiseIdle(
						new EventArgs()); // needed on Linux to avoid deadlock starving browser navigation
				}

				startupTimer.Stop();
				if (_browser == null)
				{
					Console.WriteLine("New browser still null after a second");
				}
				else if (_browser.IsBusy)
				{
					// I don't think I've seen this actually happen.
					Debug.WriteLine("New browser still busy after a second");
				}
			}
		}

		public override bool NavigateAndWaitTillDone(HtmlDom htmlDom, int timeLimit, BloomServer.SimulatedPageFileSource source,
			Func<bool> cancelCheck = null, bool throwOnTimeout = true)
		{
			// Should be called on UI thread. Since it is quite typical for this method to create the
			// window handle and browser, it can't do its own Invoke, which depends on already having a handle.
			// OTOH, Unit tests are often not run on the UI thread (and would therefore just pop up annoying asserts).
			Debug.Assert(Program.RunningOnUiThread || Program.RunningUnitTests || Program.RunningInConsoleMode,
				"Should be running on UI Thread or Unit Tests or Console mode");
			var dummy = Handle; // gets WebBrowser created, if not already done.
			var done = false;
			var navTimer = new Stopwatch();
			navTimer.Start();
			_hasNavigated = false;
			_browser.DocumentCompleted += (sender, args) => done = true;
			// just in case something goes wrong, avoid the timeout if it fails rather than completing.
			_browser.NavigationError += (sender, e) => done = true;
			// var oldUrl = _browser.Url; // goes with commented out code below
			Navigate(htmlDom, source: source);
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

		private void UpdateDisplay()
		{
			Debug.Assert(!InvokeRequired);
			if (!_browserIsReadyToNavigate)
				return;

			if (_url != null)
			{
				_browser.Visible = true;
				_isolator.Navigate(_browser, _url);
			}
		}

		public override string RunJavaScript(string script)
		{
			Debug.Assert(!InvokeRequired);
			return RunJavaScriptOn(_browser, script);
		}

		public static string RunJavaScriptOn(GeckoWebBrowser geckoWebBrowser, string script)
		{
			// Review JohnT: does this require integration with the NavigationIsolator?
			if (geckoWebBrowser != null &&
			    geckoWebBrowser.Window != null) // BL-2313 two Alt-F4s in a row while changing a folder name can do this
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

			// This one occurs somewhere in the depths of Google's login code, possibly a consequence of
			// running unexpectedly in an embedded browser. Doesn't seem to be anything we can do about it.
			if (ex.Message.Contains("Component returned failure code: 0x80004002") &&
			    ex.Message.Contains("@https://accounts.google.com/ServiceLogin"))
			{
				Logger.WriteError(ex);
				return;
			}

			var longMsg = ex.Message;
			if (script != null)
				longMsg = string.Format("Script=\"{0}\"{1}Exception message = {2}", script, Environment.NewLine,
					ex.Message);
			NonFatalProblem.Report(ModalIf.None, PassiveIf.Alpha,
				"A JavaScript error occurred and was missed by our onerror handler", longMsg, ex);
		}

		/* snippets
		 *
		 * //           _browser.WebBrowser.Navigate("javascript:void(document.getElementById('output').innerHTML = 'test')");
//            _browser.WebBrowser.Navigate("javascript:void(alert($.fn.jquery))");
//            _browser.WebBrowser.Navigate("javascript:void(alert($(':input').serialize()))");
			//_browser.WebBrowser.Navigate("javascript:void(document.getElementById('output').innerHTML = form2js('form','.',false,null))");
			//_browser.WebBrowser.Navigate("javascript:void(alert($(\"form\").serialize()))");

			*/
		//public override event EventHandler BrowserReady;



		public override void ShowHtml(string html)
		{
			Debug.Assert(!InvokeRequired);
			_browser.LoadHtml(html);
		}

		private void Browser_Resize(object sender, EventArgs e)
		{
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
	}

	class GeckoMenuItemAdder : IMenuItemAdder
	{
		private readonly ContextMenu _menu;
		public GeckoMenuItemAdder(ContextMenu menu)
		{
			_menu = menu;
		}
		public void Add(string caption, EventHandler handler, bool enabled = true)
		{
			var newItem = new MenuItem(caption, handler);
			newItem.Enabled = enabled;
			_menu.MenuItems.Add(newItem);
		}
	}
}
