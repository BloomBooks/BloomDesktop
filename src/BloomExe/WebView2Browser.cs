using Bloom.Api;
using Bloom.Book;
using Bloom.Edit;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using Microsoft.Win32;
using Newtonsoft.Json;
using SIL.Extensions;
using SIL.IO;
using SIL.Reporting;
using SIL.Windows.Forms.Miscellaneous;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Bloom
{
	public partial class WebView2Browser : Browser
	{
		public static string AlternativeWebView2Path;
		private bool _readyToNavigate;
		private PasteCommand _pasteCommand;
		private CopyCommand _copyCommand;
		private UndoCommand _undoCommand;
		private CutCommand _cutCommand;
		public WebView2Browser()
		{
			InitializeComponent();

			// I don't think anything we're doing here will take long enough for us to need to await it.
			InitWebView();

			_webview.CoreWebView2InitializationCompleted += (object sender, CoreWebView2InitializationCompletedEventArgs args) =>
			{
				try
				{
					_webview.CoreWebView2.NavigationCompleted += (object sender2, CoreWebView2NavigationCompletedEventArgs args2) =>
						{
							RaiseDocumentCompleted(sender2, args2);
						};
				}
				catch (Exception ex)
				{
					// enhance: how to show using the winforms error dialog?
					MessageBox.Show("Bloom was unable to initialize the WebView2 browser. Please see https://docs.bloomlibrary.org/wv2trouble", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
					// hard exit
					Environment.Exit(1);
				}
				_webview.CoreWebView2.FrameNavigationCompleted += (o, eventArgs) =>
				{
					RaiseDocumentCompleted(o, eventArgs);
				};
				// We thought we might need something like this to tell WebView2 to open pages in the system browser
				// rather than a new WebView2 window. But ExternalLinkController.HandleLink() does what we want if we
				// hook things up correctly on the typescript side (see hookupLinkHandler in linkHandler.ts).
				//_webview.CoreWebView2.NewWindowRequested += (object sender3, CoreWebView2NewWindowRequestedEventArgs eventArgs) =>
				//{
				//	if (eventArgs.Uri.StartsWith("https://"))
				//	{
				//		eventArgs.Handled = true;
				//		ProcessExtra.SafeStartInFront(eventArgs.Uri);
				//	}
				//};
				_webview.CoreWebView2.ContextMenuRequested += ContextMenuRequested;

				// This is only really needed for the print tab. But it is harmless elsewhere.
				// It removes some unwanted controls from the toolbar that WebView2 inserts when
				// previewing a PDF file.
				_webview.CoreWebView2.Settings.HiddenPdfToolbarItems = CoreWebView2PdfToolbarItems.Print // we prefer our big print button, and it may show a dialog first
																	   | CoreWebView2PdfToolbarItems.Rotate // shouldn't be needed, just clutter
																	   | CoreWebView2PdfToolbarItems.Save // would always be disabled, there's no known place to save
																	   | CoreWebView2PdfToolbarItems.SaveAs // We want our Save code, which checks things like not saving in the book folder
																	   | CoreWebView2PdfToolbarItems.FullScreen // doesn't work right and is hard to recover from
																	   | CoreWebView2PdfToolbarItems.MoreSettings; // none of its functions seem useful

				_webview.CoreWebView2.Settings.IsStatusBarEnabled = false;
				_webview.CoreWebView2.Settings.IsWebMessageEnabled = true;
				// Disable swipe navigation, which is a problem on trackpads (and touch screens). See BL-12405.
				_webview.CoreWebView2.Settings.IsSwipeNavigationEnabled = false;

				// Based on https://github.com/MicrosoftEdge/WebView2Feedback/issues/308,
				// this attempts to prevent Bloom asking permission to read the clipboard
				// the first time the user does a paste. I can't test it, because I don't know
				// how to revoke that permission.
				_webview.CoreWebView2.PermissionRequested += (o, e) =>
				{
					if (e.PermissionKind == CoreWebView2PermissionKind.ClipboardRead)
						e.State = CoreWebView2PermissionState.Allow;
				};
				_readyToNavigate = true;
			};
		}

		private void ContextMenuRequested(object sender, CoreWebView2ContextMenuRequestedEventArgs e)
		{
			var wantDebug = WantDebugMenuItems;
			// Remove built-in items (except "Inspect" and "Refresh", if we're in a debugging context)
			var menuList = e.MenuItems;
			for (int index = 0; index < menuList.Count;)
			{
				if (wantDebug && new string[] { "inspectElement", "reload" }.Contains(menuList[index].Name))
				{
					index++;
					continue;
				}
				menuList.RemoveAt(index);
			}
			AdjustContextMenu(new WebViewItemAdder(_webview, menuList));
		}

		private static bool _clearedCache;

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
			// We played with this also when it seemed that the only way to record a video might be to
			// disable the gpu. It didn't work; not sure whether because using the GPU wasn't the
			// problem, or because I still haven't figured out how to make this API actually work,
			// or because that specific option is not supported in WebView2.
			//var op = new CoreWebView2EnvironmentOptions("--disable-gpu");
			//var env = await CoreWebView2Environment.CreateAsync(null, null, op);
			//await _webview.EnsureCoreWebView2Async(env);
			var op = new CoreWebView2EnvironmentOptions("--autoplay-policy=no-user-gesture-required");


			// John Hatton keeps getting broken by updates to WV2. This could happen to a user too. A workaround
			// is to point to the WV2 in edge using an environment variable.
			// THIS IS DESCRIBED in the troubleshooting documentation at https://docs.bloomlibrary.org/wv2trouble,
			// so if you change it here, change the instructions there.
			AlternativeWebView2Path = Environment.GetEnvironmentVariable("BloomWV2Path");

			if (!string.IsNullOrEmpty(AlternativeWebView2Path))
			{
				if (AlternativeWebView2Path.ToLower() == "edge")
				{
					AlternativeWebView2Path = GetEdgeInstallationPath();
				}

				if (!Directory.Exists(AlternativeWebView2Path))
				{
					MessageBox.Show(AlternativeWebView2Path + " does not exist anymore. Please remove or update the environment variable 'BloomWV2Path' to point to a valid folder.", "BloomWV2Path is invalid", MessageBoxButtons.OK, MessageBoxIcon.Error);
					MessageBox.Show("Bloom will now attempt with the default path (the WebView2 Evergreen Runtime");
					AlternativeWebView2Path = null;
				}
				Bloom.ErrorReporter.BloomErrorReport.NotifyUserUnobtrusively("Using alternate WebView2 path: " + AlternativeWebView2Path, "");
			}

			var env = await CoreWebView2Environment.CreateAsync(browserExecutableFolder: AlternativeWebView2Path, userDataFolder: ProjectContext.GetBloomAppDataFolder(), options: op);
			await _webview.EnsureCoreWebView2Async(env);

			// I is kinda hard to get a click event from webview2. This needs to be explicitly sent from the browser code,
			// e.g. (window as any).chrome.webview.postMessage("browser-clicked");
			_webview.WebMessageReceived += (o, e) =>
			{
				// for now the only thing we're using this for is to close the page thumbnail list context menu when the user clicks outside it
				if (e.TryGetWebMessageAsString() == "browser-clicked")
				{
					RaiseBrowserClick(null, null);
				}
			};

			// Now do the same thing for any iframes. When an iframe is created...
			_webview.CoreWebView2.FrameCreated += (o, e) =>
			{
				// ... register for a message that our javascript will send us.
				// We are using this in the Edit View
				// to know when to cancel a page context menu until we rewrite that in React.
				// Note that _webview.GotFocus() is easier, but I was not able to get the
				// winforms popup menu to receive focus such that the webview would lose it
				// and thus tell us when it regained it.
				e.Frame.WebMessageReceived += (a, b) =>
				{
					if (b.TryGetWebMessageAsString() == "browser-clicked")
					{
						RaiseBrowserClick(null, null);
					}
				};
			};



			if (!_clearedCache)
			{
				_clearedCache = true;
				// The intent here is that none of Bloom's assets should be cached from one run of the program to another
				// (in case a new version of Bloom has been installed).
				// OTOH, I don't want to clear things so drastically as to preclude using local storage or cookies.
				// The doc is unclear as to the distinction between CacheStorage and DiskCache, but I _think_
				// this should clear what we need and nothing else.
				await _webview.CoreWebView2.Profile.ClearBrowsingDataAsync(CoreWebView2BrowsingDataKinds.CacheStorage | CoreWebView2BrowsingDataKinds.DiskCache);
			}
		}

		// used when the WebView2 installation is broken
		public static string GetEdgeInstallationPath()
		{
			string path = null;

			// Check registry for Edge installation path
			RegistryKey regKey = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients");
			if (regKey != null)
			{
				string edgeAppId = "{56EB18F8-B008-4CBD-B6D2-8C97FE7E9062}";
				RegistryKey edgeKey = regKey.OpenSubKey(edgeAppId);
				if (edgeKey != null)
				{
					string version = edgeKey.GetValue("pv") as string;
					if (!string.IsNullOrEmpty(version))
					{
						string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
						path = Path.Combine(programFiles, "Microsoft", "EdgeCore", version);
					}
				}
			}

			return path;
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

		public override void ActivateFocussed()
		{
			// I can't find any place where this does anything useful in GeckoFx that would allow me to
			// test a WebView2 implementation. For example, from the comment in the ReactControl_Load
			// method which is currently the only caller, I would expect that using it would cause
			// something useful, possibly the OK button or the number, to be selected in the Duplicate Many
			// dialog, which is one thing that actually executes this method as it launches. But
			// in fact nothing helpful is focused in either Gecko mode or WV2 mode, and in both modes,
			// it takes the same number of tab presses to get focus to the desired control. I think we
			// can leave implementing this until someone identifies a difference in Gecko vs WV2 behavior
			// that we think is due to not implementing it.
		}

		protected override void UpdateDisplay(string newUrl)
		{
			EnsureBrowserReadyToNavigate();
			_webview.CoreWebView2.Navigate(newUrl);
		}

		protected override void EnsureBrowserReadyToNavigate()
		{
			// Don't really know if this is enough. Arguably, we should also
			// wait until we are sure all the awaits in InitWebView complete.
			// But that is very hard to do without making half Bloom's code async.
			// This seems to be enough for the one case (making epubs) where I
			// experienced a problem from navigating too soon.
			// True confessions: I'm not sure why this works, nor even absolutely
			// sure that it could not loop forever. But in every case I've tried,
			// it did terminate, and in the one case where Navigation previously
			// threw an Exception indicating it was not ready, waiting like this fixed it.
			while (!_readyToNavigate)
			{
				Application.DoEvents();
				Thread.Sleep(10);
			}
		}

		public override bool NavigateAndWaitTillDone(HtmlDom htmlDom, int timeLimit, InMemoryHtmlFileSource source, Func<bool> cancelCheck, bool throwOnTimeout)
		{
			// Should be called on UI thread. Since it is quite typical for this method to create the
			// window handle and browser, it can't do its own Invoke, which depends on already having a handle.
			// OTOH, Unit tests are often not run on the UI thread (and would therefore just pop up annoying asserts).
			// For future reference, if we are navigating to produce a preview, make sure that the api call that
			// requests the call is syncing on the correct thumbnail/preview sync object, otherwise we can get a
			// deadlock here while trying to navigate (See BL-11513).
			Debug.Assert(Program.RunningOnUiThread || Program.RunningUnitTests || Program.RunningInConsoleMode,
				"Should be running on UI Thread or Unit Tests or Console mode");
			var done = false;
			var navTimer = new Stopwatch();
			EnsureBrowserReadyToNavigate();

			navTimer.Start();
			_webview.CoreWebView2.NavigationCompleted += (sender, args) => done = true;
			// The Gecko implementation also had _browser.NavigationError += (sender, e) => done = true;
			// I can't find any equivalent for WebView2 and I think the doc says it will raise NavigationCompleted
			// even if there was an error, but consider this if implementing for yet another browser.
			Navigate(htmlDom, source: source);
			// If done is set (by NavigationError?) prematurely, we still need to wait while IsBusy
			// is true to give the loaded document time to become available for the checks later.
			// See https://issues.bloomlibrary.org/youtrack/issue/BL-8741.
			while ((!done) && navTimer.ElapsedMilliseconds < timeLimit)
			{
				Application.DoEvents(); // NOTE: this has bad consequences all down the line. See BL-6122.
				Thread.Sleep(10);
				// Remember this might be needed if we reimplement with a Linux-compatible control.
				// OTOH, it doesn't help on Windows, and may lead to unwanted reentrancy if multiple
				// navigation-involving tasks as waiting on Idle.
				// I haven't made it conditional-compilation because this whole WebView2-based class is Windows-only.
				// Application.RaiseIdle(new EventArgs()); // needed on Linux to avoid deadlock starving browser navigation
				if (cancelCheck != null && cancelCheck())
				{
					navTimer.Stop();
					return false;
				}
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

		// This should be used as little as possible, since it breaks the goal of being able to
		// just drop in another implementation of the base class. However, some code outside this
		// class (currently the PDF preview code in Publish tab) already has different behaviors
		// depending on which browser we're using, and it seems simpler to me to just let it get
		// at the underlying object. If we do introduce another browser, it may become clearer
		// how we might want to encapsulate the things we use this for.
		public WebView2 InternalBrowser => _webview;

		public override string Url => _webview.Source.ToString();
		public override Bitmap GetPreview()
		{
			var stream = new MemoryStream();
			var task = _webview.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, stream);
			while (!task.IsCompleted)
			{
				Application.DoEvents();
				Thread.Sleep(10);
			}
			stream.Position = 0;
			return new Bitmap(stream);
		}

		public override void SaveDocument(string path)
		{
			var html = RunJavaScript("document.documentElement.outerHTML");
			RobustFile.WriteAllText(path, html, Encoding.UTF8);
		}

		public override async Task SaveDocumentAsync(string path)
		{
			var html = await RunJavaScriptAsync("document.documentElement.outerHTML");
			RobustFile.WriteAllText(path, html, Encoding.UTF8);
		}
		// Review: base class currently explicitly opens FireFox. Should we instead open Chrome,
		// or whatever the default browser is, or...?
		//public override void OnOpenPageInSystemBrowser(object sender, EventArgs e)
		//{
		//	throw new NotImplementedException();
		//}

		public override string RunJavaScript(string script)
		{
			Task<string> task = RunJavaScriptAsync(script);
			// I don't fully understand why this works and many other things I tried don't (typically deadlock,
			// or complain that ExecuteScriptAsync must be done on the main thread).
			// Came from an answer in https://stackoverflow.com/questions/65327263/how-to-get-sync-return-from-executescriptasync-in-webview2'
			// The more elegant thing would be a drastic rewrite of many levels of callers to all be async.
			while (!task.IsCompleted)
			{
				Application.DoEvents();
				System.Threading.Thread.Sleep(10);
			}
			var result = task.Result;
			return result;
		}

		public override async Task<string> RunJavaScriptAsync(string script)
		{
			var result = await _webview.ExecuteScriptAsync(script);
			// Whatever the javascript produces gets JSON encoded automatically by ExecuteScriptAsync.
			// All the methods Bloom calls this way return strings (or null), so we just need to do this to recover them.
			var result2 = JsonConvert.DeserializeObject(result);
			var result3 = result2?.ToString();
			return result3;
		}

		public override void SaveHTML(string path)
		{
			throw new NotImplementedException();
		}

		public override void SetEditingCommands(CutCommand cutCommand, CopyCommand copyCommand, PasteCommand pasteCommand, UndoCommand undoCommand)
		{
			_cutCommand = cutCommand;
			_copyCommand = copyCommand;
			_pasteCommand = pasteCommand;
			_undoCommand = undoCommand;

			// These implementations are all specific to our Edit tab. This is currently the only place
			// we show the buttons that use these commands, but we will have to generalize somehow if
			// that changes. I'm not sure whether the checks for existence of editTabBundle etc are needed.
			// I deliberately use RunJavaScriptAsync here without awaiting it, because nothing requires the
			// result (we only care about the side effects on the clipboard and document)
			_cutCommand.Implementer = () =>
			{
				RunJavaScriptAsync("editTabBundle?.getEditablePageBundleExports()?.cutSelection()");
			};
			_copyCommand.Implementer = () =>
			{
				RunJavaScriptAsync("editTabBundle?.getEditablePageBundleExports()?.copySelection()");
			};
			_pasteCommand.Implementer = () =>
			{
				RunJavaScriptAsync("editTabBundle?.getEditablePageBundleExports()?.pasteClipboardText()");

			};
			_undoCommand.Implementer = () =>
			{
				// Note: this is only used for the Undo button in the toolbar;
				// ctrl-z is handled in JavaScript directly.
				RunJavaScript("editTabBundle.handleUndo()");
			};
		}

		public override void ShowHtml(string html)
		{
			throw new NotImplementedException();
		}

		/// <summary>
		/// We configure something in Javascript to keep track of this, since WebView2 doesn't provide an API for it
		/// (This means this method is only reliable in EditingView, but that's also the only context where we
		/// currently use it).
		/// </summary>
		/// <returns></returns>
		private bool IsThereACurrentTextSelection()
		{
			return EditingModel.IsTextSelected;
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
				_cutCommand.Enabled = isTextSelection;
				_copyCommand.Enabled = isTextSelection;
				_pasteCommand.Enabled = PortableClipboard.ContainsText();

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

		bool currentlyRunningCanUndo = false;
		private bool CanUndo
		{
			get
			{
				// once we got a stackoverflow exception here, when, apparently, JS took longer to complete this than the timer interval
				if (currentlyRunningCanUndo)
					return true;
				try
				{
					currentlyRunningCanUndo = true;
					var result = RunJavaScript("editTabBundle?.canUndo?.()");
					return result == "yes"; // currently only returns 'yes' or 'fail'
				}
				finally
				{
					currentlyRunningCanUndo = false;
				}
			}
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
			newItem.CustomItemSelected += (sender, args) => handler(sender, new EventArgs());
			newItem.IsEnabled = enabled;
			_menuList.Insert(_menuList.Count, newItem);
		}
	}
}
