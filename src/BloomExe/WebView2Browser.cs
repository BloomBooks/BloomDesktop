using Bloom.Api;
using Bloom.Book;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using Newtonsoft.Json;
using SIL.IO;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using SIL.Windows.Forms.Miscellaneous;
using Bloom.Edit;
using SIL.Reporting;
using SIL.Extensions;
using System.Linq;

namespace Bloom
{
	public partial class WebView2Browser : Browser
	{
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
				_webview.CoreWebView2.NavigationCompleted += (object sender2, CoreWebView2NavigationCompletedEventArgs args2) =>
					{
						RaiseDocumentCompleted(sender2, args2);
					};
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
				//		Process.Start(eventArgs.Uri);
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
			var env = await CoreWebView2Environment.CreateAsync(null, ProjectContext.GetBloomAppDataFolder(), op);
			await _webview.EnsureCoreWebView2Async(env);
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

		private string _targetUrl;
		private bool _ensuredCoreWebView2;
		private bool _finishedUpdateDisplay;
		private bool _urlMatches;

		// For now I have decided not to make this return a Task and to consistently await it.
		// The fan-out of methods that would have to be made async is daunting, and code
		// that cares about the completion of the navigation will already have some
		// mechanism in place for waiting not just until the call to Navigate after the
		// await, but until we get an indication that the navigation is complete.
		// Also, I suspect that EnsureCoreWebView2Async() will almost always be already completed
		// and no awaiting will really be needed.
		// Callers should nevertheless be aware that it is not absolutely guaranteed that
		// Navigation has even started when this method returns.
		protected override async void UpdateDisplay(string newUrl)
		{
			_targetUrl = newUrl;
			_ensuredCoreWebView2 = false;
			_finishedUpdateDisplay = false;

			await _webview.EnsureCoreWebView2Async();
			_ensuredCoreWebView2 = true;
			_webview.CoreWebView2.Navigate(newUrl);
			_finishedUpdateDisplay = true;
			_urlMatches = Url == newUrl;
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
		private bool CanUndo
		{
			get
			{
				var result = RunJavaScript("editTabBundle?.canUndo?.()");
				return result == "yes"; // currently only returns 'yes' or 'fail'
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
