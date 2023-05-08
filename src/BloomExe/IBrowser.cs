using Bloom.Api;
using Bloom.Book;
using L10NSharp;
using SIL.IO;
using SIL.Reporting;
using System;
using System.Diagnostics;
using System.Drawing;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml;

namespace Bloom
{
	// This is just a temporary thing to centralize switching during progressive coding and testing. 
	public class BrowserMaker
	{
		/// <summary>
		/// Create a new WebView2Browser.
		/// <summary>
		/// <param name="offScreen">
		/// Passing offscreen true means that, on Linux, we will use an OffScreenGeckoWebBrowser internally
		/// instead of the usual one.  The only known case where it should be true is when the browser will
		/// be used for GetPreview (and not actually displayed).  It is not necessary when the browser is
		/// used to navigate to a document and make queries about it using Javascript, even if the browser
		/// will never actually appear on the screen.
		/// </parameter>
		/// <remarks>
		/// We can remove the offScreen parameter when we retire Gecko, even if we decide to keep this
		/// static method for creating browsers.
		/// </remarks>
		public static Browser MakeBrowser()
		{
			return new WebView2Browser();
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
									  // Currently may be a TempFile (a real filesystem file) or a InMemoryHtmlFile (just a dictionary entry).
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

		public abstract void EnsureHandleCreated();

		public event EventHandler BrowserReady;
		public event EventHandler OnBrowserClick;
		public event EventHandler DocumentCompleted;

		public abstract string Url { get; }

		/// <summary>
		/// Get a bitmap showing the current state of the browser. Caller should dispose.
		/// </summary>
		/// <returns></returns>
		public abstract Bitmap GetPreview();

		/// <summary>
		/// If it returns true these are in place of our standard extensions; if false, the
		/// standard ones will follow whatever it adds.
		/// </summary>
		public Func<IMenuItemAdder, bool> ContextMenuProvider { get; set; }

		protected bool WantDebugMenuItems => ApplicationUpdateSupport.IsDevOrAlpha || ((ModifierKeys & Keys.Control) == Keys.Control);

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
			DocumentCompleted?.Invoke(this, e);
		}

		protected void RaiseBrowserClick(object sender, EventArgs e)
		{
			OnBrowserClick?.Invoke(sender, e);
		}
		public abstract void ActivateFocussed(); // review what should this be called?

		// NB: make sure you assigned HtmlDom.BaseForRelativePaths if the temporary document might
		// contain references to files in the directory of the original HTML file it is derived from,
		// 'cause that provides the information needed
		// to fake out the browser about where the 'file' is so internal references work.();
		public void Navigate(HtmlDom htmlDom, HtmlDom htmlEditDom = null, bool setAsCurrentPageForDebugging = false,
			InMemoryHtmlFileSource source = InMemoryHtmlFileSource.Nav)
		{
			if (InvokeRequired)
			{
				Invoke(new Action<HtmlDom, HtmlDom, bool, InMemoryHtmlFileSource>(Navigate), htmlDom, htmlEditDom, setAsCurrentPageForDebugging, source);
				return;
			}
			// This must already be called before calling Navigate(), but it doesn't really hurt to call it again.
			EnsureBrowserReadyToNavigate();

			XmlDocument dom = htmlDom.RawDom;
			XmlDocument editDom = htmlEditDom == null ? null : htmlEditDom.RawDom;

			_rootDom = dom;//.CloneNode(true); //clone because we want to modify it a bit
			_pageEditDom = editDom ?? dom;

			XmlHtmlConverter.MakeXmlishTagsSafeForInterpretationAsHtml(dom);
			var fakeTempFile = BloomServer.MakeInMemoryHtmlFileInBookFolder(htmlDom, setAsCurrentPageForDebugging: setAsCurrentPageForDebugging, source: source);
			SetNewDependent(fakeTempFile);
			UpdateDisplay(fakeTempFile.Key);
		}

		private void SetNewDependent(IDisposable dependent)
		{
			// Save information needed to prevent http://issues.bloomlibrary.org/youtrack/issue/BL-4268.
			var simulated = _dependentContent as InMemoryHtmlFile;
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

		public virtual bool IsReadyToNavigate => true;

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

			EnsureBrowserReadyToNavigate();

			//TODO: fix up this hack. We found that deleting the pdf while we're still showing it is a bad idea.
			if (cleanupFileAfterNavigating && !url.EndsWith(".pdf"))
			{
				SetNewDependent(TempFile.TrackExisting(url));
			}
			UpdateDisplay(url);
		}
		public abstract bool NavigateAndWaitTillDone(HtmlDom htmlDom, int timeLimit, InMemoryHtmlFileSource source, Func<bool> cancelCheck = null, bool throwOnTimeout = true);

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

		public void OnOpenPageInEdge(object sender, EventArgs e)
		{
			Debug.Assert(!InvokeRequired);
			Process.Start("msedge", $"{Url}");
			// intentionally letting any errors just escape, give us an error
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
			// Yes, this wipes out everything else in the head. At this point, the only things
			// we need in _pageEditDom are the user defined style sheet and the bloom-page element in the body.
			_pageEditDom.GetElementsByTagName("head")[0].InnerXml = HtmlDom.CreateUserModifiedStyles(userCssContent);
		}

		public abstract string RunJavaScript(string script);

		public abstract Task<string> RunJavaScriptAsync(string script);

		public abstract void SaveHTML(string path);

		public void SetEditDom(HtmlDom editDom)
		{
			_pageEditDom = editDom.RawDom;
		}
		public abstract void SetEditingCommands(CutCommand cutCommand, CopyCommand copyCommand, PasteCommand pasteCommand, UndoCommand undoCommand);
		public abstract void ShowHtml(string html);
		public abstract void UpdateEditButtons();

		protected void AdjustContextMenu(IMenuItemAdder adder)
		{
			Debug.Assert(!InvokeRequired);

			ContextMenuLocation = PointToClient(Cursor.Position);

			if (ContextMenuProvider != null)
			{
				var replacesStdMenu = ContextMenuProvider(adder);

				// Currently, this whole if condition is useless and we could just remove it.
				// But some day we may reinstitute non-debug, standard menu items, and then
				// this logic will be needed.
				//if (replacesStdMenu)
				//{
				//	AddOtherMenuItemsForDebugging(adder);
				//	return;
				//}
			}

			// Here is where we would add the standard menu items, if we had any.

			AddOtherMenuItemsForDebugging(adder);
		}

		private void AddOtherMenuItemsForDebugging(IMenuItemAdder adder)
		{
			if (!WantDebugMenuItems)
				return;

			adder.Add(
				"Open Page in Edge", // dev only, no need to localize
				OnOpenPageInEdge);
		}

		public abstract void SaveDocument(string path);

		public async virtual Task SaveDocumentAsync(string path)
		{
			SaveDocument(path);
		}
	}

	public interface IMenuItemAdder
	{
		void Add(string caption, EventHandler handler, bool enabled = true);
	}
}
