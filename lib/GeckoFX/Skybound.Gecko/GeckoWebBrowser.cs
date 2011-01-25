#region ***** BEGIN LICENSE BLOCK *****
/* Version: MPL 1.1/GPL 2.0/LGPL 2.1
 *
 * The contents of this file are subject to the Mozilla Public License Version
 * 1.1 (the "License"); you may not use this file except in compliance with
 * the License. You may obtain a copy of the License at
 * http://www.mozilla.org/MPL/
 *
 * Software distributed under the License is distributed on an "AS IS" basis,
 * WITHOUT WARRANTY OF ANY KIND, either express or implied. See the License
 * for the specific language governing rights and limitations under the
 * License.
 *
 * The Original Code is Skybound Software code.
 *
 * The Initial Developer of the Original Code is Skybound Software.
 * Portions created by the Initial Developer are Copyright (C) 2008-2009
 * the Initial Developer. All Rights Reserved.
 *
 * Contributor(s):
 *
 * Alternatively, the contents of this file may be used under the terms of
 * either the GNU General Public License Version 2 or later (the "GPL"), or
 * the GNU Lesser General Public License Version 2.1 or later (the "LGPL"),
 * in which case the provisions of the GPL or the LGPL are applicable instead
 * of those above. If you wish to allow use of your version of this file only
 * under the terms of either the GPL or the LGPL, and not to allow others to
 * use your version of this file under the terms of the MPL, indicate your
 * decision by deleting the provisions above and replace them with the notice
 * and other provisions required by the GPL or the LGPL. If you do not delete
 * the provisions above, a recipient may use your version of this file under
 * the terms of any one of the MPL, the GPL or the LGPL.
 */
#endregion END LICENSE BLOCK

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.IO;
using System.Reflection;
using System.Diagnostics;
using System.Text;

namespace Skybound.Gecko
{
	/// <summary>
	/// A Gecko-based web browser.
	/// </summary>
	public partial class GeckoWebBrowser : Control,
		nsIWebBrowserChrome,
		nsIContextMenuListener2,
		nsIWebProgressListener,
		nsIInterfaceRequestor,
		nsIEmbeddingSiteWindow2,
		nsISupportsWeakReference,
		nsIWeakReference,
		nsIDOMEventListener,
		nsISHistoryListener,
		nsITooltipListener
		//nsIWindowProvider,
	{
		/// <summary>
		/// Initializes a new instance of <see cref="GeckoWebBrowser"/>.
		/// </summary>
		public GeckoWebBrowser()
		{
		}

		//static Dictionary<nsIDOMDocument, GeckoWebBrowser> FromDOMDocumentTable = new Dictionary<nsIDOMDocument,GeckoWebBrowser>();

		//internal static GeckoWebBrowser FromDOMDocument(nsIDOMDocument doc)
		//{
		//      GeckoWebBrowser result;
		//      return FromDOMDocumentTable.TryGetValue(doc, out result) ? result : null;
		//}

		#region protected override void Dispose(bool disposing)

		protected override void Dispose(bool disposing)
		{
			if (BaseWindow == null)
				return;

			if (!Environment.HasShutdownStarted && !AppDomain.CurrentDomain.IsFinalizingForUnload())
			{
				// make sure the object is still alove before we call a method on it
				if (Xpcom.QueryInterface<nsIWebNavigation>(WebNav) != null)
				{
					WebNav.Stop(nsIWebNavigationConstants.STOP_ALL);
				}
				WebNav = null;

				if (Xpcom.QueryInterface<nsIBaseWindow>(BaseWindow) != null)
				{
					BaseWindow.Destroy();
				}
				BaseWindow = null;

			}

			base.Dispose(disposing);
		}
		#endregion

		nsIWebBrowser WebBrowser;
		nsIWebBrowserFocus WebBrowserFocus;
		nsIBaseWindow BaseWindow;
		nsIWebNavigation WebNav;
		int ChromeFlags;



		protected override void OnHandleCreated(EventArgs e)
		{
			if (!this.DesignMode)
			{
				Xpcom.Initialize();
				#if !NO_CUSTOM_PROMPT_SERVICE
				PromptServiceFactory.Register();
				#endif
				WindowCreator.Register();
				//CertificateDialogsFactory.Register();
				//ToolTipTextProviderFactory.Register();

				WebBrowser = Xpcom.CreateInstance<nsIWebBrowser>("@mozilla.org/embedding/browser/nsWebBrowser;1");
				WebBrowserFocus = (nsIWebBrowserFocus)WebBrowser;
				BaseWindow = (nsIBaseWindow)WebBrowser;
				WebNav = (nsIWebNavigation)WebBrowser;

				WebBrowser.SetContainerWindow(this);

				//int type = ((this.ChromeFlags & (int)GeckoWindowFlags.OpenAsChrome) != 0) ? nsIDocShellTreeItemConstants.typeChromeWrapper : nsIDocShellTreeItemConstants.typeContentWrapper;

				//nsIDocShellTreeItem shellTreeItem = Xpcom.QueryInterface<nsIDocShellTreeItem>(WebBrowser);
				//if (shellTreeItem != null)
				//      shellTreeItem.SetItemType(type);
				//else
				//{
				//      nsIDocShellTreeItem19 treeItem19 = Xpcom.QueryInterface<nsIDocShellTreeItem19>(WebBrowser);
				//      if (treeItem19 != null)
				//            treeItem19.SetItemType(type);
				//}
				BaseWindow.InitWindow(this.Handle, IntPtr.Zero, 0, 0, this.Width, this.Height);
				BaseWindow.Create();

				Guid nsIWebProgressListenerGUID = typeof(nsIWebProgressListener).GUID;
				WebBrowser.AddWebBrowserListener(this, ref nsIWebProgressListenerGUID);

				nsIDOMEventTarget target = Xpcom.QueryInterface<nsIDOMWindow2>(WebBrowser.GetContentDOMWindow()).GetWindowRoot();

				target.AddEventListener(new nsAString("submit"), this, true);
				target.AddEventListener(new nsAString("keydown"), this, true);
				target.AddEventListener(new nsAString("keyup"), this, true);
				target.AddEventListener(new nsAString("mousemove"), this, true);
				target.AddEventListener(new nsAString("mouseover"), this, true);
				target.AddEventListener(new nsAString("mouseout"), this, true);
				target.AddEventListener(new nsAString("mousedown"), this, true);
				target.AddEventListener(new nsAString("mouseup"), this, true);
				target.AddEventListener(new nsAString("click"), this, true);

				// history
				WebNav.GetSessionHistory().AddSHistoryListener(this);

				BaseWindow.SetVisibility(true);

				if ((this.ChromeFlags & (int)GeckoWindowFlags.OpenAsChrome) == 0)
				{
					// navigating to about:blank allows drag & drop to work properly before a page has been loaded into the browser
					Navigate("about:blank");
				}

				// this fix prevents the browser from crashing if the first page loaded is invalid (missing file, invalid URL, etc)
				Document.Cookie = "";
			}

			base.OnHandleCreated(e);
		}

		class WindowCreator : nsIWindowCreator
		{
			static WindowCreator()
			{
				// give an nsIWindowCreator to the WindowWatcher service
				nsIWindowWatcher watcher = Xpcom.GetService<nsIWindowWatcher>("@mozilla.org/embedcomp/window-watcher;1");
				if (watcher != null)
				{
					//disabled for now because it's not loading the proper URL automatically
					watcher.SetWindowCreator(new WindowCreator());
				}
			}

			public static void Register()
			{
				// calling this method simply invokes the static ctor
			}

			public nsIWebBrowserChrome CreateChromeWindow(nsIWebBrowserChrome parent, uint chromeFlags)
			{
				// for chrome windows, we can use the AppShellService to create the window using some built-in xulrunner code
				GeckoWindowFlags flags = (GeckoWindowFlags)chromeFlags;
				if ((flags & GeckoWindowFlags.OpenAsChrome) != 0)
				{
					  // obtain the services we need
					  nsIAppShellService appShellService = Xpcom.GetService<nsIAppShellService>("@mozilla.org/appshell/appShellService;1");
					  object appShell = Xpcom.GetService(new Guid("2d96b3df-c051-11d1-a827-0040959a28c9"));

					  // create the child window
					  nsIXULWindow xulChild = appShellService.CreateTopLevelWindow(null, null, chromeFlags, -1, -1, appShell);

					  // this little gem allows the GeckoWebBrowser to be properly activated when it gains the focus again
					  if (parent is GeckoWebBrowser && (flags & GeckoWindowFlags.OpenAsDialog) != 0)
					  {
						EventHandler gotFocus = null;
						gotFocus = delegate (object sender, EventArgs e)
						{
							(sender as GeckoWebBrowser).GotFocus -= gotFocus;
							(sender as GeckoWebBrowser).WebBrowserFocus.Activate();
						};
						(parent as GeckoWebBrowser).GotFocus += gotFocus;
					  }

					  // return the chrome
					  return Xpcom.QueryInterface<nsIWebBrowserChrome>(xulChild);
				}

				GeckoWebBrowser browser = parent as GeckoWebBrowser;
				if (browser != null)
				{
					GeckoCreateWindowEventArgs e = new GeckoCreateWindowEventArgs((GeckoWindowFlags)chromeFlags);
					browser.OnCreateWindow(e);

					if (e.WebBrowser != null)
					{
						// set flags
						((nsIWebBrowserChrome)e.WebBrowser).SetChromeFlags((int)chromeFlags);
						return e.WebBrowser;
					}

					System.Media.SystemSounds.Beep.Play();

					// prevents crash
					return new GeckoWebBrowser();
				}
				return null;
			}
		}

		protected override void OnPaint(PaintEventArgs e)
		{
			if (this.DesignMode)
			{
				string versionString = ((AssemblyFileVersionAttribute)Attribute.GetCustomAttribute(GetType().Assembly, typeof(AssemblyFileVersionAttribute))).Version;
				string copyright = ((AssemblyCopyrightAttribute)Attribute.GetCustomAttribute(GetType().Assembly, typeof(AssemblyCopyrightAttribute))).Copyright;

				using (Brush brush = new System.Drawing.Drawing2D.HatchBrush(System.Drawing.Drawing2D.HatchStyle.SolidDiamond, Color.FromArgb(240, 240, 240), Color.White))
					e.Graphics.FillRectangle(brush, this.ClientRectangle);

				e.Graphics.DrawString("Skybound GeckoFX v" + versionString + "\r\n" + copyright + "\r\n" + "http://www.geckofx.org", SystemFonts.MessageBoxFont, Brushes.Black,
					new RectangleF(2, 2, this.Width-4, this.Height-4));
				e.Graphics.DrawRectangle(SystemPens.ControlDark, 0, 0, Width-1, Height-1);
			}
			base.OnPaint(e);
		}

		#region public event GeckoCreateWindowEventHandler CreateWindow
		public event GeckoCreateWindowEventHandler CreateWindow
		{
			add { this.Events.AddHandler(CreateWindowEvent, value); }
			remove { this.Events.RemoveHandler(CreateWindowEvent, value); }
		}
		private static object CreateWindowEvent = new object();

		/// <summary>Raises the <see cref="CreateWindow"/> event.</summary>
		/// <param name="e">The data for the event.</param>
		protected virtual void OnCreateWindow(GeckoCreateWindowEventArgs e)
		{
			if (((GeckoCreateWindowEventHandler)this.Events[CreateWindowEvent]) != null)
				((GeckoCreateWindowEventHandler)this.Events[CreateWindowEvent])(this, e);
		}
		#endregion

		#region public event GeckoWindowSetBoundsEventHandler WindowSetBounds
		public event GeckoWindowSetBoundsEventHandler WindowSetBounds
		{
			add { this.Events.AddHandler(WindowSetBoundsEvent, value); }
			remove { this.Events.RemoveHandler(WindowSetBoundsEvent, value); }
		}
		private static object WindowSetBoundsEvent = new object();

		/// <summary>Raises the <see cref="WindowSetBounds"/> event.</summary>
		/// <param name="e">The data for the event.</param>
		protected virtual void OnWindowSetBounds(GeckoWindowSetBoundsEventArgs e)
		{
			if (((GeckoWindowSetBoundsEventHandler)this.Events[WindowSetBoundsEvent]) != null)
				((GeckoWindowSetBoundsEventHandler)this.Events[WindowSetBoundsEvent])(this, e);
		}
		#endregion

		#region protected override void WndProc(ref Message m)
		[DllImport("user32")]
		private static extern bool IsChild(IntPtr hWndParent, IntPtr hwnd);

		[DllImport("user32")]
		private static extern IntPtr GetFocus();

		protected override void WndProc(ref Message m)
		{
			const int WM_GETDLGCODE = 0x87;
			const int DLGC_WANTALLKEYS = 0x4;
			const int WM_MOUSEACTIVATE = 0x21;
			const int MA_ACTIVATE = 0x1;

			if (!DesignMode)
			{
				if (m.Msg == WM_GETDLGCODE)
				{
					m.Result = (IntPtr)DLGC_WANTALLKEYS;
					return;
				}
				else if (m.Msg == WM_MOUSEACTIVATE)
				{
					m.Result = (IntPtr)MA_ACTIVATE;

					if (!IsChild(Handle, GetFocus()))
					{
						this.Focus();
					}
					return;
				}
			}

			base.WndProc(ref m);
		}
		#endregion

		#region Overridden Properties & Event Handlers Handlers
		[Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public override Color BackColor
		{
			get { return base.BackColor; }
			set { base.BackColor = value; }
		}

		[Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public override Image BackgroundImage
		{
			get { return base.BackgroundImage; }
			set { base.BackgroundImage = value; }
		}

		[Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public override ImageLayout BackgroundImageLayout
		{
			get { return base.BackgroundImageLayout; }
			set { base.BackgroundImageLayout = value; }
		}

		[Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public override Color ForeColor
		{
			get { return base.ForeColor; }
			set { base.ForeColor = value; }
		}

		[Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public override Font Font
		{
			get { return base.Font; }
			set { base.Font = value; }
		}

		[Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public override string Text
		{
			get { return base.Text; }
			set { base.Text = value; }
		}

		protected override void OnEnter(EventArgs e)
		{
			  WebBrowserFocus.Activate();

			  base.OnEnter(e);
		}

		protected override void OnLeave(EventArgs e)
		{
			  if (!IsBusy)
					WebBrowserFocus.Deactivate();

			  base.OnLeave(e);
		}

		protected override void OnSizeChanged(EventArgs e)
		{
			if (BaseWindow != null)
			{
				BaseWindow.SetPositionAndSize(0, 0, ClientSize.Width, ClientSize.Height, true);
			}

			base.OnSizeChanged(e);
		}
		#endregion

		/// <summary>
		/// Navigates to the specified URL.
		/// </summary>
		/// <param name="url">The url to navigate to.</param>
		public void Navigate(string url)
		{
			Navigate(url, 0, null, null, null);
		}

		/// <summary>
		/// Navigates to the specified URL using the given load flags.
		/// </summary>
		/// <param name="url">The url to navigate to.  If the url is empty or null, the browser does not navigate and the method returns false.</param>
		/// <param name="loadFlags">Flags which specify how the page is loaded.</param>
		public bool Navigate(string url, GeckoLoadFlags loadFlags)
		{
			return Navigate(url, loadFlags, null, null, null);
		}

		/// <summary>
		/// Navigates to the specified URL using the given load flags, referrer, post data and headers.
		/// </summary>
		/// <param name="url">The url to navigate to.  If the url is empty or null, the browser does not navigate and the method returns false.</param>
		/// <param name="loadFlags">Flags which specify how the page is loaded.</param>
		/// <param name="referrer">The referring URL, or null.</param>
		/// <param name="postData">The post data to send, or null.  If you use post data, you must explicity specify a Content-Type and Content-Length
		/// header in <paramref name="additionalHeaders"/>.</param>
		/// <param name="additionalHeaders">Any additional HTTP headers to send, or null.  Separate multiple headers with CRLF.  For example,
		/// "Content-Type: application/x-www-form-urlencoded\r\nContent-Length: 50"</param>
		public bool Navigate(string url, GeckoLoadFlags loadFlags, string referrer, byte [] postData, string additionalHeaders)
		{
			if (string.IsNullOrEmpty(url))
				return false;

			if (IsHandleCreated)
			{
				if (IsBusy)
					this.Stop();

				// WebNav.LoadURI throws an exception if we try to open a file that doesn't exist...
				Uri created;
				if (Uri.TryCreate(url, UriKind.RelativeOrAbsolute, out created) && created.IsAbsoluteUri && created.IsFile)
				{
					if (!File.Exists(created.LocalPath) && !Directory.Exists(created.LocalPath))
						return false;
				}

				nsIInputStream postDataStream = null, headersStream = null;

				if (postData != null)
				{
					// post data must start with CRLF.  actually, you can put additional headers before this, but there's no
					// point because this method has an "additionalHeaders" argument.  so we might as well insert it automatically
					Array.Resize(ref postData, postData.Length + 2);
					Array.Copy(postData, 0, postData, 2, postData.Length - 2);
					postData[0] = (byte)'\r';
					postData[1] = (byte)'\n';
					postDataStream = ByteArrayInputStream.Create(postData);
				}

				if (!string.IsNullOrEmpty(additionalHeaders))
				{
					// each header must end with a CRLF (including the last one)
					// here we simply ensure that the last header has a CRLF.  if the header has other syntax problems,
					// they're the caller's responsibility
					if (!additionalHeaders.EndsWith("\r\n"))
						additionalHeaders += "\r\n";

					headersStream = ByteArrayInputStream.Create(Encoding.UTF8.GetBytes(additionalHeaders));
				}

				nsIURI referrerUri = null;
				if (!string.IsNullOrEmpty(referrer))
				{
					referrerUri = Xpcom.GetService<nsIIOService>("@mozilla.org/network/io-service;1").NewURI(new nsACString(referrer), null, null);
				}

				return (WebNav.LoadURI(url, (uint)loadFlags, referrerUri, postDataStream, headersStream) != 0);
			}
			else
			{
				throw new InvalidOperationException("Cannot call Navigate() before the window handle is created.");
			}
		}

		/// <summary>
		/// Streams a byte array using nsIInputStream.
		/// </summary>
		#region class ByteArrayInputStream : nsIInputStream
		class ByteArrayInputStream : nsIInputStream
		{
			private ByteArrayInputStream(byte [] data)
			{
				Data = data;
			}

			public static ByteArrayInputStream Create(byte [] data)
			{
				return (data == null) ? null : new ByteArrayInputStream(data);
			}

			byte [] Data;
			int Position;

			#region nsIInputStream Members

			public void Close()
			{
				// do nothing
			}

			public int Available()
			{
				return Data.Length - Position;
			}

			public int Read(IntPtr aBuf, uint aCount)
			{
				int count = (int)Math.Min(aCount, Available());

				if (count > 0)
				{
					Marshal.Copy(Data, Position, aBuf, count);
					Position += count;
				}

				return count;
			}

			public unsafe int ReadSegments(IntPtr aWriter, IntPtr aClosure, uint aCount)
			{
				int length = (int)Math.Min(aCount, Available());
				int writeCount = 0;

				if (length > 0)
				{
					  nsWriteSegmentFun fun = (nsWriteSegmentFun)Marshal.GetDelegateForFunctionPointer(aWriter, typeof(nsWriteSegmentFun));

					  fixed (byte * data = &Data[Position])
					  {
						int result = fun(this, aClosure, (IntPtr)data, Position, length, out writeCount);
					  }

					  Position += writeCount;
				}

				return writeCount;
			}

			public bool IsNonBlocking()
			{
				return true;
			}

			#endregion
		}
		#endregion

		/// <summary>
		/// Gets or sets whether all default items are removed from the standard context menu.
		/// </summary>
		[DefaultValue(false),Description("Removes default items from the standard context menu.  The ShowContextMenu event is still raised to add custom items.")]
		public bool NoDefaultContextMenu
		{
			get { return _NoDefaultContextMenu; }
			set { _NoDefaultContextMenu = value; }
		}
		bool _NoDefaultContextMenu;

		/// <summary>
		/// Gets or sets the text displayed in the status bar.
		/// </summary>
		[Browsable(false), DefaultValue("")]
		public string StatusText
		{
			get { return _StatusText ?? ""; }
			set
			{
				if (StatusText != value)
				{
					_StatusText = value;
					OnStatusTextChanged(EventArgs.Empty);
				}
			}
		}
		string _StatusText;

		#region public event EventHandler StatusTextChanged
		/// <summary>
		/// Occurs when the value of the <see cref="StatusText"/> property is changed.
		/// </summary>
		[Category("Property Changed"), Description("Occurs when the value of the StatusText property is changed.")]
		public event EventHandler StatusTextChanged
		{
			add { this.Events.AddHandler(StatusTextChangedEvent, value); }
			remove { this.Events.RemoveHandler(StatusTextChangedEvent, value); }
		}
		private static object StatusTextChangedEvent = new object();

		/// <summary>Raises the <see cref="StatusTextChanged"/> event.</summary>
		/// <param name="e">The data for the event.</param>
		protected virtual void OnStatusTextChanged(EventArgs e)
		{
			if (((EventHandler)this.Events[StatusTextChangedEvent]) != null)
				((EventHandler)this.Events[StatusTextChangedEvent])(this, e);
		}
		#endregion

		/// <summary>
		/// Gets the title of the document loaded into the web browser.
		/// </summary>
		[Browsable(false), DefaultValue("")]
		public string DocumentTitle
		{
			get { return _DocumentTitle ?? ""; }
			private set
			{
				if (DocumentTitle != value)
				{
					_DocumentTitle = value;
					OnDocumentTitleChanged(EventArgs.Empty);
				}
			}
		}
		string _DocumentTitle;

		#region public event EventHandler DocumentTitleChanged
		/// <summary>
		/// Occurs when the value of the <see cref="DocumentTitle"/> property is changed.
		/// </summary>
		[Category("Property Changed"), Description("Occurs when the value of the DocumentTitle property is changed.")]
		public event EventHandler DocumentTitleChanged
		{
			add { this.Events.AddHandler(DocumentTitleChangedEvent, value); }
			remove { this.Events.RemoveHandler(DocumentTitleChangedEvent, value); }
		}
		private static object DocumentTitleChangedEvent = new object();

		/// <summary>Raises the <see cref="DocumentTitleChanged"/> event.</summary>
		/// <param name="e">The data for the event.</param>
		protected virtual void OnDocumentTitleChanged(EventArgs e)
		{
			if (((EventHandler)this.Events[DocumentTitleChangedEvent]) != null)
				((EventHandler)this.Events[DocumentTitleChangedEvent])(this, e);
		}
		#endregion

		/// <summary>
		/// Gets whether the browser may navigate back in the history.
		/// </summary>
		[BrowsableAttribute(false)]
		public bool CanGoBack
		{
			get { return _CanGoBack; }
		}
		bool _CanGoBack;

		#region public event EventHandler CanGoBackChanged
		/// <summary>
		/// Occurs when the value of the <see cref="CanGoBack"/> property is changed.
		/// </summary>
		[Category("Property Changed"), Description("Occurs when the value of the CanGoBack property is changed.")]
		public event EventHandler CanGoBackChanged
		{
			add { this.Events.AddHandler(CanGoBackChangedEvent, value); }
			remove { this.Events.RemoveHandler(CanGoBackChangedEvent, value); }
		}
		private static object CanGoBackChangedEvent = new object();

		/// <summary>Raises the <see cref="CanGoBackChanged"/> event.</summary>
		/// <param name="e">The data for the event.</param>
		protected virtual void OnCanGoBackChanged(EventArgs e)
		{
			if (((EventHandler)this.Events[CanGoBackChangedEvent]) != null)
				((EventHandler)this.Events[CanGoBackChangedEvent])(this, e);
		}
		#endregion

		/// <summary>
		/// Gets whether the browser may navigate forward in the history.
		/// </summary>
		[BrowsableAttribute(false)]
		public bool CanGoForward
		{
			get { return _CanGoForward; }
		}
		bool _CanGoForward;

		#region public event EventHandler CanGoForwardChanged
		/// <summary>
		/// Occurs when the value of the <see cref="CanGoForward"/> property is changed.
		/// </summary>
		[Category("Property Changed"), Description("Occurs when the value of the CanGoForward property is changed.")]
		public event EventHandler CanGoForwardChanged
		{
			add { this.Events.AddHandler(CanGoForwardChangedEvent, value); }
			remove { this.Events.RemoveHandler(CanGoForwardChangedEvent, value); }
		}
		private static object CanGoForwardChangedEvent = new object();

		/// <summary>Raises the <see cref="CanGoForwardChanged"/> event.</summary>
		/// <param name="e">The data for the event.</param>
		protected virtual void OnCanGoForwardChanged(EventArgs e)
		{
			if (((EventHandler)this.Events[CanGoForwardChangedEvent]) != null)
				((EventHandler)this.Events[CanGoForwardChangedEvent])(this, e);
		}
		#endregion

		/// <summary>Raises the CanGoBackChanged or CanGoForwardChanged events when necessary.</summary>
		void UpdateCommandStatus()
		{
			bool canGoBack = false;
			bool canGoForward = false;
			if (WebNav != null)
			{
				canGoBack = WebNav.GetCanGoBack();
				canGoForward = WebNav.GetCanGoForward();
			}

			if (_CanGoBack != canGoBack)
			{
				_CanGoBack = canGoBack;
				OnCanGoBackChanged(EventArgs.Empty);
			}

			if (_CanGoForward != canGoForward)
			{
				_CanGoForward = canGoForward;
				OnCanGoForwardChanged(EventArgs.Empty);
			}
		}

		/// <summary>
		/// Navigates to the previous page in the history, if one is available.
		/// </summary>
		/// <returns></returns>
		public bool GoBack()
		{
			if (CanGoBack)
			{
				WebNav.GoBack();
				return true;
			}
			return false;
		}

		/// <summary>
		/// Navigates to the next page in the history, if one is available.
		/// </summary>
		/// <returns></returns>
		public bool GoForward()
		{
			if (CanGoForward)
			{
				WebNav.GoForward();
				return true;
			}
			return false;
		}

		/// <summary>
		/// Cancels any pending navigation and also stops any sound or animation.
		/// </summary>
		public void Stop()
		{
			if (WebNav != null)
				WebNav.Stop(nsIWebNavigationConstants.STOP_ALL);
		}

		/// <summary>
		/// Reloads the current page.
		/// </summary>
		/// <returns></returns>
		public bool Reload()
		{
			return Reload(GeckoLoadFlags.None);
		}

		/// <summary>
		/// Reloads the current page using the specified flags.
		/// </summary>
		/// <param name="flags"></param>
		/// <returns></returns>
		public bool Reload(GeckoLoadFlags flags)
		{
			Uri url = this.Url;
			if (url != null && url.IsFile && !File.Exists(url.LocalPath))
			{
				// can't reload a file which no longer exists--a COM exception will be thrown
				return false;
			}

			if (WebNav != null)
				WebNav.Reload((uint)flags);

			return true;
		}

		nsIClipboardCommands ClipboardCommands
		{
			get
			{
				if (_ClipboardCommands == null)
				{
					_ClipboardCommands = Xpcom.QueryInterface<nsIClipboardCommands>(WebBrowser);
				}
				return _ClipboardCommands;
			}
		}
		nsIClipboardCommands _ClipboardCommands;

		delegate int CanPerformMethod(out bool result);

		bool CanPerform(CanPerformMethod method)
		{
			bool result;
			if (method(out result) != 0)
				return false;
			return result;
		}

		/// <summary>
		/// Gets whether the image contents of the selection may be copied to the clipboard as an image.
		/// </summary>
		[Browsable(false)]
		public bool CanCopyImageContents
		{
			get { return CanPerform(ClipboardCommands.CanCopyImageContents); }
		}

		/// <summary>
		/// Copies the image contents of the selection to the clipboard as an image.
		/// </summary>
		/// <returns></returns>
		public bool CopyImageContents()
		{
			if (CanCopyImageContents)
			{
				ClipboardCommands.CopyImageContents();
				return true;
			}
			return false;
		}

		/// <summary>
		/// Returns true if the <see cref="CopyImageLocation"/> command is enabled.
		/// </summary>
		[Browsable(false)]
		public bool CanCopyImageLocation
		{
			get { return CanPerform(ClipboardCommands.CanCopyImageLocation); }
		}

		/// <summary>
		/// Copies the location of the currently selected image to the clipboard.
		/// </summary>
		/// <returns></returns>
		public bool CopyImageLocation()
		{
			if (CanCopyImageLocation)
			{
				ClipboardCommands.CopyImageLocation();
				return true;
			}
			return false;
		}

		/// <summary>
		/// Returns true if the <see cref="CopyLinkLocation"/> command is enabled.
		/// </summary>
		[Browsable(false)]
		public bool CanCopyLinkLocation
		{
			get { return CanPerform(ClipboardCommands.CanCopyLinkLocation); }
		}

		/// <summary>
		/// Copies the location of the currently selected link to the clipboard.
		/// </summary>
		/// <returns></returns>
		public bool CopyLinkLocation()
		{
			if (CanCopyLinkLocation)
			{
				ClipboardCommands.CopyLinkLocation();
				return true;
			}
			return false;
		}

		/// <summary>
		/// Returns true if the <see cref="CopySelection"/> command is enabled.
		/// </summary>
		[Browsable(false)]
		public bool CanCopySelection
		{
			get { return CanPerform(ClipboardCommands.CanCopySelection); }
		}

		/// <summary>
		/// Copies the selection to the clipboard.
		/// </summary>
		/// <returns></returns>
		public bool CopySelection()
		{
			if (CanCopySelection)
			{
				ClipboardCommands.CopySelection();
				return true;
			}
			return false;
		}

		/// <summary>
		/// Returns true if the <see cref="CutSelection"/> command is enabled.
		/// </summary>
		[Browsable(false)]
		public bool CanCutSelection
		{
			get { return CanPerform(ClipboardCommands.CanCutSelection); }
		}

		/// <summary>
		/// Cuts the selection to the clipboard.
		/// </summary>
		/// <returns></returns>
		public bool CutSelection()
		{
			if (CanCutSelection)
			{
				ClipboardCommands.CutSelection();
				return true;
			}
			return false;
		}

		/// <summary>
		/// Returns true if the <see cref="Paste"/> command is enabled.
		/// </summary>
		[Browsable(false)]
		public bool CanPaste
		{
			get { return CanPerform(ClipboardCommands.CanPaste); }
		}

		/// <summary>
		/// Pastes the contents of the clipboard at the current selection.
		/// </summary>
		/// <returns></returns>
		public bool Paste()
		{
			if (CanPaste)
			{
				ClipboardCommands.Paste();
				return true;
			}
			return false;
		}

		/// <summary>
		/// Selects the entire document.
		/// </summary>
		/// <returns></returns>
		public void SelectAll()
		{
			ClipboardCommands.SelectAll();
		}

		/// <summary>
		/// Empties the current selection.
		/// </summary>
		/// <returns></returns>
		public void SelectNone()
		{
			ClipboardCommands.SelectNone();
		}

		nsICommandManager CommandManager
		{
			get { return (_CommandManager == null) ? (_CommandManager = Xpcom.QueryInterface<nsICommandManager>(WebBrowser)) : _CommandManager; }
		}
		nsICommandManager _CommandManager;

		/// <summary>
		/// Returns true if the undo command is enabled.
		/// </summary>
		[Browsable(false)]
		public bool CanUndo
		{
			get { return CommandManager.IsCommandEnabled("cmd_undo", null); }
		}

		/// <summary>
		/// Undoes last executed command.
		/// </summary>
		/// <returns></returns>
		public bool Undo()
		{
			if (CanUndo)
			{
				CommandManager.DoCommand("cmd_undo", null, null);
				return true;
			}
			return false;
		}

		/// <summary>
		/// Returns true if the redo command is enabled.
		/// </summary>
		[Browsable(false)]
		public bool CanRedo
		{
			get { return CommandManager.IsCommandEnabled("cmd_redo", null); }
		}

		/// <summary>
		/// Redoes last undone command.
		/// </summary>
		/// <returns></returns>
		public bool Redo()
		{
			if (CanRedo)
			{
				CommandManager.DoCommand("cmd_redo", null, null);
				return true;
			}
			return false;
		}

		/// <summary>
		/// Executes the command with the specified name.
		/// </summary>
		/// <param name="name">The name of the command to execute.  See http://developer.mozilla.org/en/docs/Editor_Embedding_Guide for a list of available commands.</param>
		public void ExecuteCommand(string name)
		{
			if (string.IsNullOrEmpty(name))
				throw new ArgumentException("name");

			CommandManager.DoCommand(name, null, null);
		}

		/// <summary>
		/// Gets the <see cref="Url"/> currently displayed in the web browser.
		/// Use the <see cref="Navigate(string)"/> method to change the URL.
		/// </summary>
		[BrowsableAttribute(false)]
		public Uri Url
		{
			get
			{
				if (WebNav == null)
					return null;

				nsURI location = WebNav.GetCurrentURI();
				if (!location.IsNull)
				{
					Uri result;
					return Uri.TryCreate(location.Spec, UriKind.Absolute, out result) ? result : null;
				}

				return new Uri("about:blank");
			}
		}

		/// <summary>
		/// Gets the <see cref="Url"/> of the current page's referrer.
		/// </summary>
		[BrowsableAttribute(false)]
		public Uri ReferrerUrl
		{
			get
			{
				if (WebNav == null)
					return null;

				nsIURI location = WebNav.GetReferringURI();
				if (location != null)
				{
					return new Uri(nsString.Get(location.GetSpec));
				}

				return new Uri("about:blank");
			}
		}

		/// <summary>
		/// Gets the <see cref="GeckoWindow"/> object for this browser.
		/// </summary>
		[Browsable(false)]
		public GeckoWindow Window
		{
			get
			{
				if (WebBrowser == null)
					return null;

				if (_Window == null)
				{
					_Window = GeckoWindow.Create((nsIDOMWindow)WebBrowser.GetContentDOMWindow());
				}
				return _Window;
			}
		}
		GeckoWindow _Window;

		/// <summary>
		/// Gets the <see cref="GeckoDocument"/> for the page currently loaded in the browser.
		/// </summary>
		[Browsable(false)]
		public GeckoDocument Document
		{
			get
			{
				if (WebBrowser == null)
					return null;

				if (_Document == null)
				{
					_Document = GeckoDocument.Create(Xpcom.QueryInterface<nsIDOMHTMLDocument>(WebBrowser.GetContentDOMWindow().GetDocument()));
					//FromDOMDocumentTable.Add((nsIDOMDocument)_Document.DomObject, this);
				}
				return _Document;
			}
		}
		GeckoDocument _Document;

		private void UnloadDocument()
		{
			//if (_Document != null)
			//{
			//      FromDOMDocumentTable.Remove((nsIDOMDocument)_Document.DomObject);
			//}
			_Document = null;
		}

		#region public event GeckoNavigatingEventHandler Navigating
		/// <summary>
		/// Occurs before the browser navigates to a new page.
		/// </summary>
		[Category("Navigation"), Description("Occurs before the browser navigates to a new page.")]
		public event GeckoNavigatingEventHandler Navigating
		{
			add { this.Events.AddHandler(NavigatingEvent, value); }
			remove { this.Events.RemoveHandler(NavigatingEvent, value); }
		}
		private static object NavigatingEvent = new object();

		/// <summary>Raises the <see cref="Navigating"/> event.</summary>
		/// <param name="e">The data for the event.</param>
		protected virtual void OnNavigating(GeckoNavigatingEventArgs e)
		{
			if (((GeckoNavigatingEventHandler)this.Events[NavigatingEvent]) != null)
				((GeckoNavigatingEventHandler)this.Events[NavigatingEvent])(this, e);
		}
		#endregion

		#region public event GeckoNavigatedEventHandler Navigated
		/// <summary>
		/// Occurs after the browser has navigated to a new page.
		/// </summary>
		[Category("Navigation"), Description("Occurs after the browser has navigated to a new page.")]
		public event GeckoNavigatedEventHandler Navigated
		{
			add { this.Events.AddHandler(NavigatedEvent, value); }
			remove { this.Events.RemoveHandler(NavigatedEvent, value); }
		}
		private static object NavigatedEvent = new object();

		/// <summary>Raises the <see cref="Navigated"/> event.</summary>
		/// <param name="e">The data for the event.</param>
		protected virtual void OnNavigated(GeckoNavigatedEventArgs e)
		{
			if (((GeckoNavigatedEventHandler)this.Events[NavigatedEvent]) != null)
				((GeckoNavigatedEventHandler)this.Events[NavigatedEvent])(this, e);
		}
		#endregion

		#region public event EventHandler DocumentCompleted
		/// <summary>
		/// Occurs after the browser has finished parsing a new page and updated the <see cref="Document"/> property.
		/// </summary>
		[Category("Navigation"), Description("Occurs after the browser has finished parsing a new page and updated the Document property.")]
		public event EventHandler DocumentCompleted
		{
			add { this.Events.AddHandler(DocumentCompletedEvent, value); }
			remove { this.Events.RemoveHandler(DocumentCompletedEvent, value); }
		}
		private static object DocumentCompletedEvent = new object();

		/// <summary>Raises the <see cref="DocumentCompleted"/> event.</summary>
		/// <param name="e">The data for the event.</param>
		protected virtual void OnDocumentCompleted(EventArgs e)
		{
			if (((EventHandler)this.Events[DocumentCompletedEvent]) != null)
				((EventHandler)this.Events[DocumentCompletedEvent])(this, e);
		}
		#endregion

		/// <summary>
		/// Gets whether the browser is busy loading a page.
		/// </summary>
		[Browsable(false)]
		public bool IsBusy
		{
			get { return _IsBusy; }
			private set { _IsBusy = value; }
		}
		bool _IsBusy;

		#region public event GeckoProgressEventHandler ProgressChanged
		/// <summary>
		/// Occurs when the control has updated progress information.
		/// </summary>
		[Category("Navigation"), Description("Occurs when the control has updated progress information.")]
		public event GeckoProgressEventHandler ProgressChanged
		{
			add { this.Events.AddHandler(ProgressChangedEvent, value); }
			remove { this.Events.RemoveHandler(ProgressChangedEvent, value); }
		}
		private static object ProgressChangedEvent = new object();

		/// <summary>Raises the <see cref="ProgressChanged"/> event.</summary>
		/// <param name="e">The data for the event.</param>
		protected virtual void OnProgressChanged(GeckoProgressEventArgs e)
		{
			if (((GeckoProgressEventHandler)this.Events[ProgressChangedEvent]) != null)
				((GeckoProgressEventHandler)this.Events[ProgressChangedEvent])(this, e);
		}
		#endregion

		/// <summary>
		/// Saves the current document to the specified file name.
		/// </summary>
		/// <param name="filename"></param>
		/// <returns></returns>
		public void SaveDocument(string filename, string outputMimeType)//hatton added mimeType param
		{
			if (!Directory.Exists(Path.GetDirectoryName(filename)))
				throw new System.IO.DirectoryNotFoundException();
			else if (this.Document == null)
				throw new InvalidOperationException("No document has been loaded into the web browser.");

			nsIWebBrowserPersist persist = Xpcom.QueryInterface<nsIWebBrowserPersist>(WebBrowser);
			if (persist != null)
			{
				persist.SaveDocument((nsIDOMDocument)Document.DomObject, Xpcom.NewNativeLocalFile(filename), null,
					outputMimeType, 0, 0);
			}
			else
			{
				throw new InvalidOperationException();
			}
		}

		/// <summary>
		/// Gets the session history for the current browser.
		/// </summary>
		[Browsable(false)]
		public GeckoSessionHistory History
		{
			get
			{
				if (WebNav == null)
					return null;

				return (_History == null) ? (_History = new GeckoSessionHistory(WebNav)) : _History;
			}
		}
		GeckoSessionHistory _History;

		#region nsIWebBrowserChrome Members

		void nsIWebBrowserChrome.SetStatus(int statusType, string status)
		{
			this.StatusText = status;
		}

		nsIWebBrowser nsIWebBrowserChrome.GetWebBrowser()
		{
			return this.WebBrowser;
		}

		void nsIWebBrowserChrome.SetWebBrowser(nsIWebBrowser webBrowser)
		{
			this.WebBrowser = webBrowser;
		}

		int nsIWebBrowserChrome.GetChromeFlags()
		{
			return this.ChromeFlags;
		}

		void nsIWebBrowserChrome.SetChromeFlags(int flags)
		{
			this.ChromeFlags = flags;
		}

		void nsIWebBrowserChrome.DestroyBrowserWindow()
		{
			//throw new NotImplementedException();
			OnWindowClosed(EventArgs.Empty);
		}

		#region public event EventHandler WindowClosed
		public event EventHandler WindowClosed
		{
			add { this.Events.AddHandler(WindowClosedEvent, value); }
			remove { this.Events.RemoveHandler(WindowClosedEvent, value); }
		}
		private static object WindowClosedEvent = new object();

		/// <summary>Raises the <see cref="WindowClosed"/> event.</summary>
		/// <param name="e">The data for the event.</param>
		protected virtual void OnWindowClosed(EventArgs e)
		{
			if (((EventHandler)this.Events[WindowClosedEvent]) != null)
				((EventHandler)this.Events[WindowClosedEvent])(this, e);
		}
		#endregion

		void nsIWebBrowserChrome.SizeBrowserTo(int cx, int cy)
		{
			OnWindowSetBounds(new GeckoWindowSetBoundsEventArgs(new Rectangle(0, 0, cx, cy), BoundsSpecified.Size));
		}

		void nsIWebBrowserChrome.ShowAsModal()
		{
			//throw new NotImplementedException();
			Debug.WriteLine("ShowAsModal");

			_IsWindowModal = true;
		}
		bool _IsWindowModal;

		bool nsIWebBrowserChrome.IsWindowModal()
		{
			//throw new NotImplementedException();
			Debug.WriteLine("IsWindowModal");

			return _IsWindowModal;
		}

		void nsIWebBrowserChrome.ExitModalEventLoop(int status)
		{
			//throw new NotImplementedException();
			Debug.WriteLine("ExitModalEventLoop");

			_IsWindowModal = false;
		}

		#endregion

		#region nsIContextMenuListener2 Members

		void nsIContextMenuListener2.OnShowContextMenu(uint aContextFlags, nsIContextMenuInfo info)
		{
			// if we don't have a target node, we can't do anything by default.  this happens in XUL forms (i.e. about:config)
			if (info.GetTargetNode() == null)
				return;

			ContextMenu menu = new ContextMenu();

			// no default items are added when the context menu is disabled
			if (!this.NoDefaultContextMenu)
			{
				List<MenuItem> optionals = new List<MenuItem>();

				if (this.CanUndo || this.CanRedo)
				{
					optionals.Add(new MenuItem("Undo", delegate { Undo(); }));
					optionals.Add(new MenuItem("Redo", delegate { Redo(); }));

					optionals[0].Enabled = this.CanUndo;
					optionals[1].Enabled = this.CanRedo;
				}
				else
				{
					optionals.Add(new MenuItem("Back", delegate { GoBack(); }));
					optionals.Add(new MenuItem("Forward", delegate { GoForward(); }));

					optionals[0].Enabled = this.CanGoBack;
					optionals[1].Enabled = this.CanGoForward;
				}

				optionals.Add(new MenuItem("-"));

				if (this.CanCopyImageContents)
					optionals.Add(new MenuItem("Copy Image Contents", delegate { CopyImageContents(); }));

				if (this.CanCopyImageLocation)
					optionals.Add(new MenuItem("Copy Image Location", delegate { CopyImageLocation(); }));

				if (this.CanCopyLinkLocation)
					optionals.Add(new MenuItem("Copy Link Location", delegate { CopyLinkLocation(); }));

				if (this.CanCopySelection)
					optionals.Add(new MenuItem("Copy Selection", delegate { CopySelection(); }));

				MenuItem mnuSelectAll = new MenuItem("Select All");
				mnuSelectAll.Click += delegate { SelectAll(); };

				GeckoDocument doc = GeckoDocument.Create((nsIDOMHTMLDocument)info.GetTargetNode().GetOwnerDocument());

				string viewSourceUrl = (doc == null) ? null : Convert.ToString(doc.Url);

				MenuItem mnuViewSource = new MenuItem("View Source");
				mnuViewSource.Enabled = !string.IsNullOrEmpty(viewSourceUrl);
				mnuViewSource.Click += delegate { ViewSource(viewSourceUrl); };

				string properties = (doc != null && doc.Url == Document.Url) ? "Page Properties" : "IFRAME Properties";

				MenuItem mnuProperties = new MenuItem(properties);
				mnuProperties.Enabled = doc != null;
				mnuProperties.Click += delegate { ShowPageProperties(doc); };

				menu.MenuItems.AddRange(optionals.ToArray());
				menu.MenuItems.Add(mnuSelectAll);
				menu.MenuItems.Add("-");
				menu.MenuItems.Add(mnuViewSource);
				menu.MenuItems.Add(mnuProperties);
			}

			// get image urls
			Uri backgroundImageSrc = null, imageSrc = null;
			nsIURI src;

			if (info.GetBackgroundImageSrc(out src) == 0)
			{
				backgroundImageSrc = new Uri(new nsURI(src).Spec);
			}

			if (info.GetImageSrc(out src) == 0)
			{
				imageSrc = new Uri(new nsURI(src).Spec);
			}

			// get associated link.  note that this needs to be done manually because GetAssociatedLink returns a non-zero
			// result when no associated link is available, so an exception would be thrown by nsString.Get()
			string associatedLink = null;
			using (nsAString str = new nsAString())
			{
				if (info.GetAssociatedLink(str) == 0)
				{
					associatedLink = str.ToString();
				}
			}

			GeckoContextMenuEventArgs e = new GeckoContextMenuEventArgs(
				PointToClient(MousePosition), menu, associatedLink, backgroundImageSrc, imageSrc,
				GeckoNode.Create(Xpcom.QueryInterface<nsIDOMNode>(info.GetTargetNode()))
				);

			OnShowContextMenu(e);

			if (e.ContextMenu != null && e.ContextMenu.MenuItems.Count > 0)
			{
				e.ContextMenu.Show(this, e.Location);
			}
		}

		#region public event GeckoContextMenuEventHandler ShowContextMenu
		public event GeckoContextMenuEventHandler ShowContextMenu
		{
			add { this.Events.AddHandler(ShowContextMenuEvent, value); }
			remove { this.Events.RemoveHandler(ShowContextMenuEvent, value); }
		}
		private static object ShowContextMenuEvent = new object();

		/// <summary>Raises the <see cref="ShowContextMenu"/> event.</summary>
		/// <param name="e">The data for the event.</param>
		protected virtual void OnShowContextMenu(GeckoContextMenuEventArgs e)
		{
			if (((GeckoContextMenuEventHandler)this.Events[ShowContextMenuEvent]) != null)
				((GeckoContextMenuEventHandler)this.Events[ShowContextMenuEvent])(this, e);
		}
		#endregion

		/// <summary>
		/// Opens a new window which contains the source code for the current page.
		/// </summary>
		public void ViewSource()
		{
			ViewSource(Url.ToString());
		}

		/// <summary>
		/// Opens a new window which contains the source code for the specified page.
		/// </summary>
		/// <param name="url"></param>
		public void ViewSource(string url)
		{
			//#if GECKO_1_9
			//OpenDialog("chrome://global/content/viewSource.xul", "all,dialog=no", "a", "b", "c");
			//#elif GECKO_1_8
			Form form = new Form();
			form.Text = "View Source";
			GeckoWebBrowser browser = new GeckoWebBrowser();
			browser.Dock = DockStyle.Fill;
			form.Controls.Add(browser);
			form.Load += delegate { browser.Navigate("view-source:" + url); };
			form.Icon = FindForm().Icon;
			form.ClientSize = this.ClientSize;
			form.StartPosition = FormStartPosition.CenterParent;
			form.Show();
			//#endif
		}

		// this is disabled for now because it stopped working

		//#if GECKO_1_9
		///// <summary>
		///// Opens a chrome dialog.  This method is private for now until we finalize how it's going to work.  And it doesn't work in 1.8 because
		///// the method signature for OpenWindowJS is different (it takes a jsval* for the arguments).
		///// </summary>
		///// <param name="chromeUrl"></param>
		///// <param name="features"></param>
		///// <param name="args"></param>
		//void OpenDialog(string chromeUrl, string features, params object [] args)
		//{
		//      nsPIWindowWatcher watcher = Xpcom.GetService<nsPIWindowWatcher>("@mozilla.org/embedcomp/window-watcher;1");
		//      if (watcher == null)
		//      {
		//            throw new Exception("Couldn't obtain the window watcher service.");
		//      }

		//      nsIArray argsArray = (args.Length > 0) ? nsArray.Create(args) : null;

		//      // open the window
		//      nsIDOMWindow window = watcher.OpenWindowJS((nsIDOMWindow)this.Window.DomWindow, chromeUrl, "_blank", features, true, argsArray);
		//}
		//#endif

		/// <summary>
		/// Displays a properties dialog for the current page.
		/// </summary>
		public void ShowPageProperties()
		{
			ShowPageProperties(Document);
		}

		public void ShowPageProperties(GeckoDocument document)
		{
			if (document == null)
				throw new ArgumentNullException("document");

			new PropertiesDialog((nsIDOMHTMLDocument)document.DomObject).ShowDialog(this);
		}

		#endregion

		#region nsIInterfaceRequestor Members

		IntPtr nsIInterfaceRequestor.GetInterface(ref Guid uuid)
		{
			object obj = this;

			// note: when a new window is created, gecko calls GetInterface on the webbrowser to get a DOMWindow in order
			// to set the starting url
			if (this.WebBrowser != null)
			{
				if (uuid == typeof(nsIDOMWindow).GUID)
				{
					obj = this.WebBrowser.GetContentDOMWindow();
				}
				else if (uuid == typeof(nsIDOMDocument).GUID)
				{
					obj = this.WebBrowser.GetContentDOMWindow().GetDocument();
				}
			}

			IntPtr ppv, pUnk = Marshal.GetIUnknownForObject(obj);

			Marshal.QueryInterface(pUnk, ref uuid, out ppv);

			Marshal.Release(pUnk);

			return ppv;
		}

		#endregion

		#region nsIEmbeddingSiteWindow Members

		void nsIEmbeddingSiteWindow.SetDimensions(uint flags, int x, int y, int cx, int cy)
		{
			const int DIM_FLAGS_POSITION = 1;
			const int DIM_FLAGS_SIZE_INNER = 2;
			const int DIM_FLAGS_SIZE_OUTER = 4;

			BoundsSpecified specified = 0;
			if ((flags & DIM_FLAGS_POSITION) != 0)
			{
				specified |= BoundsSpecified.Location;
			}
			if ((flags & DIM_FLAGS_SIZE_INNER) != 0 || (flags & DIM_FLAGS_SIZE_OUTER) != 0)
			{
				specified |= BoundsSpecified.Size;
			}

			OnWindowSetBounds(new GeckoWindowSetBoundsEventArgs(new Rectangle(x, y, cx, cy), specified));
		}

		void nsIEmbeddingSiteWindow.GetDimensions(uint flags, ref int x, ref int y, ref int cx, ref int cy)
		{
			if ((flags & nsIEmbeddingSiteWindowConstants.DIM_FLAGS_POSITION) != 0)
			{
				Point pt = PointToScreen(Point.Empty);
				x = pt.X;
				y = pt.Y;
			}

			Control topLevel = this.TopLevelControl;

			Size nonClient = new Size(topLevel.Width - ClientSize.Width, topLevel.Height - ClientSize.Height);
			cx = ClientSize.Width;
			cy = ClientSize.Height;

			if ((this.ChromeFlags & (int)GeckoWindowFlags.OpenAsChrome) != 0)
			{
				BaseWindow.GetSize(out cx, out cy);
			}

			if ((flags & nsIEmbeddingSiteWindowConstants.DIM_FLAGS_SIZE_INNER) == 0)
			{
				cx += nonClient.Width;
				cy += nonClient.Height;
			}
		}

		void nsIEmbeddingSiteWindow.SetFocus()
		{
			Focus();
			if (BaseWindow != null)
			{
				BaseWindow.SetFocus();
			}
		}

		bool nsIEmbeddingSiteWindow.GetVisibility()
		{
			return Visible;
		}

		void nsIEmbeddingSiteWindow.SetVisibility(bool aVisibility)
		{
			//if (aVisibility)
			//{
			//      Form form = FindForm();
			//      if (form != null)
			//      {
			//            form.Visible = aVisibility;
			//      }
			//}

			Visible = aVisibility;
		}

		string nsIEmbeddingSiteWindow.GetTitle()
		{
			return DocumentTitle;
		}

		void nsIEmbeddingSiteWindow.SetTitle(string aTitle)
		{
			DocumentTitle = aTitle;
		}

		IntPtr nsIEmbeddingSiteWindow.GetSiteWindow()
		{
			return Handle;
		}

		#endregion

		#region nsIEmbeddingSiteWindow2 Members

		void nsIEmbeddingSiteWindow2.SetDimensions(uint flags, int x, int y, int cx, int cy)
		{
			(this as nsIEmbeddingSiteWindow).SetDimensions(flags, x, y, cx, y);
		}

		void nsIEmbeddingSiteWindow2.GetDimensions(uint flags, ref int x, ref int y, ref int cx, ref int cy)
		{
			(this as nsIEmbeddingSiteWindow).GetDimensions(flags, ref x, ref y, ref cx, ref y);
		}

		void nsIEmbeddingSiteWindow2.SetFocus()
		{
			(this as nsIEmbeddingSiteWindow).SetFocus();
		}

		bool nsIEmbeddingSiteWindow2.GetVisibility()
		{
			return (this as nsIEmbeddingSiteWindow).GetVisibility();
		}

		void nsIEmbeddingSiteWindow2.SetVisibility(bool aVisibility)
		{
			(this as nsIEmbeddingSiteWindow).SetVisibility(aVisibility);
		}

		string nsIEmbeddingSiteWindow2.GetTitle()
		{
			return (this as nsIEmbeddingSiteWindow).GetTitle();
		}

		void nsIEmbeddingSiteWindow2.SetTitle(string aTitle)
		{
			(this as nsIEmbeddingSiteWindow).SetTitle(aTitle);
		}

		IntPtr nsIEmbeddingSiteWindow2.GetSiteWindow()
		{
			return (this as nsIEmbeddingSiteWindow).GetSiteWindow();
		}

		void nsIEmbeddingSiteWindow2.Blur()
		{
			  //throw new NotImplementedException();
		}

		#endregion

		#region nsISupportsWeakReference Members

		nsIWeakReference nsISupportsWeakReference.GetWeakReference()
		{
			return this;
		}

		#endregion

		#region nsIWeakReference Members

		IntPtr nsIWeakReference.QueryReferent(ref Guid uuid)
		{
			IntPtr ppv, pUnk = Marshal.GetIUnknownForObject(this);

			Marshal.QueryInterface(pUnk, ref uuid, out ppv);

			Marshal.Release(pUnk);

			if (ppv != IntPtr.Zero)
			{
				  Marshal.Release(ppv);
			}

			return ppv;
		}

		#endregion

		#region nsIWebProgressListener Members

		void nsIWebProgressListener.OnStateChange(nsIWebProgress aWebProgress, nsIRequest aRequest, int aStateFlags, int aStatus)
		{
			bool cancelled = false;

			if ((aStateFlags & nsIWebProgressListenerConstants.STATE_START) != 0 && (aStateFlags & nsIWebProgressListenerConstants.STATE_IS_NETWORK) != 0)
			{
				IsBusy = true;

				Uri uri;
				Uri.TryCreate(nsString.Get(aRequest.GetName), UriKind.Absolute, out uri);

				GeckoNavigatingEventArgs ea = new GeckoNavigatingEventArgs(uri);
				OnNavigating(ea);

				if (ea.Cancel)
				{
					aRequest.Cancel(0);
					cancelled = true;
				}
			}

			// maybe we'll add another event here to allow users to cancel certain content types
			//if ((aStateFlags & nsIWebProgressListenerConstants.STATE_TRANSFERRING) != 0)
			//{
			//      GeckoResponse rsp = new GeckoResponse(aRequest);
			//      if (rsp.ContentType == "application/x-executable")
			//      {
			//            // do something
			//      }
			//}

			if (cancelled || ((aStateFlags & nsIWebProgressListenerConstants.STATE_STOP) != 0 && (aStateFlags & nsIWebProgressListenerConstants.STATE_IS_NETWORK) != 0))
			{
				// clear busy state
				IsBusy = false;

				// kill any cached document and raise DocumentCompleted event
				UnloadDocument();
				OnDocumentCompleted(EventArgs.Empty);

				// clear progress bar
				OnProgressChanged(new GeckoProgressEventArgs(100, 100));

				// clear status bar
				StatusText = "";
			}
		}

		void nsIWebProgressListener.OnProgressChange(nsIWebProgress aWebProgress, nsIRequest aRequest, int aCurSelfProgress, int aMaxSelfProgress, int aCurTotalProgress, int aMaxTotalProgress)
		{
			int nProgress = aCurTotalProgress;
			int nProgressMax = Math.Max(aMaxTotalProgress, 0);

			if (nProgressMax == 0)
				nProgressMax = Int32.MaxValue;

			if (nProgress > nProgressMax)
				nProgress = nProgressMax;

			OnProgressChanged(new GeckoProgressEventArgs(nProgress, nProgressMax));
		}

		void nsIWebProgressListener.OnLocationChange(nsIWebProgress aWebProgress, nsIRequest aRequest, nsIURI aLocation)
		{
			// make sure we're loading the top-level window
			nsIDOMWindow domWindow = aWebProgress.GetDOMWindow();
			if (domWindow != null)
			{
				  if (domWindow != domWindow.GetTop())
						return;
			}

			Uri uri = new Uri(nsString.Get(aLocation.GetSpec));

			OnNavigated(new GeckoNavigatedEventArgs(uri, new GeckoResponse(aRequest)));
			UpdateCommandStatus();
		}

		void nsIWebProgressListener.OnStatusChange(nsIWebProgress aWebProgress, nsIRequest aRequest, int aStatus, string aMessage)
		{
			if (aWebProgress.GetIsLoadingDocument())
			{
				StatusText = aMessage;
				UpdateCommandStatus();
			}
		}

		void nsIWebProgressListener.OnSecurityChange(nsIWebProgress aWebProgress, nsIRequest aRequest, int aState)
		{
			SetSecurityState((GeckoSecurityState) aState);
		}

		/// <summary>
		/// Gets a value which indicates whether the current page is secure.
		/// </summary>
		[Browsable(false)]
		public GeckoSecurityState SecurityState
		{
			get { return _SecurityState; }
		}
		GeckoSecurityState _SecurityState;

		void SetSecurityState(GeckoSecurityState value)
		{
			if (_SecurityState != value)
			{
				_SecurityState = value;
				OnSecurityStateChanged(EventArgs.Empty);
			}
		}

		#region public event EventHandler SecurityStateChanged
		/// <summary>
		/// Occurs when the value of the <see cref="SecurityState"/> property is changed.
		/// </summary>
		[Category("Property Changed"), Description("Occurs when the value of the SecurityState property is changed.")]
		public event EventHandler SecurityStateChanged
		{
			add { this.Events.AddHandler(SecurityStateChangedEvent, value); }
			remove { this.Events.RemoveHandler(SecurityStateChangedEvent, value); }
		}
		private static object SecurityStateChangedEvent = new object();

		/// <summary>Raises the <see cref="SecurityStateChanged"/> event.</summary>
		/// <param name="e">The data for the event.</param>
		protected virtual void OnSecurityStateChanged(EventArgs e)
		{
			if (((EventHandler)this.Events[SecurityStateChangedEvent]) != null)
				((EventHandler)this.Events[SecurityStateChangedEvent])(this, e);
		}
		#endregion

		#endregion

		#region nsIDOMEventListener Members

		void nsIDOMEventListener.HandleEvent(nsIDOMEvent e)
		{
			string type;
			using (nsAString str = new nsAString())
			{
				e.GetType(str);
				type = str.ToString();
			}

			GeckoDomEventArgs ea = null;

			switch (type)
			{
				case "keydown": OnDomKeyDown((GeckoDomKeyEventArgs)(ea = new GeckoDomKeyEventArgs((nsIDOMKeyEvent)e))); break;
				case "keyup": OnDomKeyUp((GeckoDomKeyEventArgs)(ea = new GeckoDomKeyEventArgs((nsIDOMKeyEvent)e))); break;

				case "mousedown": OnDomMouseDown((GeckoDomMouseEventArgs)(ea = new GeckoDomMouseEventArgs((nsIDOMMouseEvent)e))); break;
				case "mouseup": OnDomMouseUp((GeckoDomMouseEventArgs)(ea = new GeckoDomMouseEventArgs((nsIDOMMouseEvent)e))); break;
				case "mousemove": OnDomMouseMove((GeckoDomMouseEventArgs)(ea = new GeckoDomMouseEventArgs((nsIDOMMouseEvent)e))); break;
				case "mouseover": OnDomMouseOver((GeckoDomMouseEventArgs)(ea = new GeckoDomMouseEventArgs((nsIDOMMouseEvent)e))); break;
				case "mouseout": OnDomMouseOut((GeckoDomMouseEventArgs)(ea = new GeckoDomMouseEventArgs((nsIDOMMouseEvent)e))); break;
				case "click": OnDomClick(ea = new GeckoDomEventArgs(e)); break;
				case "submit": OnDomSubmit(ea = new GeckoDomEventArgs(e)); break;
			}

			if (ea != null && ea.Cancelable && ea.Handled)
				e.PreventDefault();
		}

		#region public event GeckoDomKeyEventHandler DomKeyDown
		[Category("DOM Events")]
		public event GeckoDomKeyEventHandler DomKeyDown
		{
			add { this.Events.AddHandler(DomKeyDownEvent, value); }
			remove { this.Events.RemoveHandler(DomKeyDownEvent, value); }
		}
		private static object DomKeyDownEvent = new object();

		/// <summary>Raises the <see cref="DomKeyDown"/> event.</summary>
		/// <param name="e">The data for the event.</param>
		protected virtual void OnDomKeyDown(GeckoDomKeyEventArgs e)
		{
			if (((GeckoDomKeyEventHandler)this.Events[DomKeyDownEvent]) != null)
				((GeckoDomKeyEventHandler)this.Events[DomKeyDownEvent])(this, e);
		}
		#endregion

		#region public event GeckoDomKeyEventHandler DomKeyUp
		[Category("DOM Events")]
		public event GeckoDomKeyEventHandler DomKeyUp
		{
			add { this.Events.AddHandler(DomKeyUpEvent, value); }
			remove { this.Events.RemoveHandler(DomKeyUpEvent, value); }
		}
		private static object DomKeyUpEvent = new object();

		/// <summary>Raises the <see cref="DomKeyUp"/> event.</summary>
		/// <param name="e">The data for the event.</param>
		protected virtual void OnDomKeyUp(GeckoDomKeyEventArgs e)
		{
			if (((GeckoDomKeyEventHandler)this.Events[DomKeyUpEvent]) != null)
				((GeckoDomKeyEventHandler)this.Events[DomKeyUpEvent])(this, e);
		}
		#endregion

		#region public event GeckoDomMouseEventHandler DomMouseDown
		[Category("DOM Events")]
		public event GeckoDomMouseEventHandler DomMouseDown
		{
			add { this.Events.AddHandler(DomMouseDownEvent, value); }
			remove { this.Events.RemoveHandler(DomMouseDownEvent, value); }
		}
		private static object DomMouseDownEvent = new object();

		/// <summary>Raises the <see cref="DomMouseDown"/> event.</summary>
		/// <param name="e">The data for the event.</param>
		protected virtual void OnDomMouseDown(GeckoDomMouseEventArgs e)
		{
			if (((GeckoDomMouseEventHandler)this.Events[DomMouseDownEvent]) != null)
				((GeckoDomMouseEventHandler)this.Events[DomMouseDownEvent])(this, e);
		}
		#endregion

		#region public event GeckoDomMouseEventHandler DomMouseUp
		[Category("DOM Events")]
		public event GeckoDomMouseEventHandler DomMouseUp
		{
			add { this.Events.AddHandler(DomMouseUpEvent, value); }
			remove { this.Events.RemoveHandler(DomMouseUpEvent, value); }
		}
		private static object DomMouseUpEvent = new object();

		/// <summary>Raises the <see cref="DomMouseUp"/> event.</summary>
		/// <param name="e">The data for the event.</param>
		protected virtual void OnDomMouseUp(GeckoDomMouseEventArgs e)
		{
			if (((GeckoDomMouseEventHandler)this.Events[DomMouseUpEvent]) != null)
				((GeckoDomMouseEventHandler)this.Events[DomMouseUpEvent])(this, e);
		}
		#endregion

		#region public event GeckoDomMouseEventHandler DomMouseOver
		[Category("DOM Events")]
		public event GeckoDomMouseEventHandler DomMouseOver
		{
			add { this.Events.AddHandler(DomMouseOverEvent, value); }
			remove { this.Events.RemoveHandler(DomMouseOverEvent, value); }
		}
		private static object DomMouseOverEvent = new object();

		/// <summary>Raises the <see cref="DomMouseOver"/> event.</summary>
		/// <param name="e">The data for the event.</param>
		protected virtual void OnDomMouseOver(GeckoDomMouseEventArgs e)
		{
			if (((GeckoDomMouseEventHandler)this.Events[DomMouseOverEvent]) != null)
				((GeckoDomMouseEventHandler)this.Events[DomMouseOverEvent])(this, e);
		}
		#endregion

		#region public event GeckoDomMouseEventHandler DomMouseOut
		[Category("DOM Events")]
		public event GeckoDomMouseEventHandler DomMouseOut
		{
			add { this.Events.AddHandler(DomMouseOutEvent, value); }
			remove { this.Events.RemoveHandler(DomMouseOutEvent, value); }
		}
		private static object DomMouseOutEvent = new object();

		/// <summary>Raises the <see cref="DomMouseOut"/> event.</summary>
		/// <param name="e">The data for the event.</param>
		protected virtual void OnDomMouseOut(GeckoDomMouseEventArgs e)
		{
			if (((GeckoDomMouseEventHandler)this.Events[DomMouseOutEvent]) != null)
				((GeckoDomMouseEventHandler)this.Events[DomMouseOutEvent])(this, e);
		}
		#endregion

		#region public event GeckoDomMouseEventHandler DomMouseMove
		[Category("DOM Events")]
		public event GeckoDomMouseEventHandler DomMouseMove
		{
			add { this.Events.AddHandler(DomMouseMoveEvent, value); }
			remove { this.Events.RemoveHandler(DomMouseMoveEvent, value); }
		}
		private static object DomMouseMoveEvent = new object();

		/// <summary>Raises the <see cref="DomMouseMove"/> event.</summary>
		/// <param name="e">The data for the event.</param>
		protected virtual void OnDomMouseMove(GeckoDomMouseEventArgs e)
		{
			if (((GeckoDomMouseEventHandler)this.Events[DomMouseMoveEvent]) != null)
				((GeckoDomMouseEventHandler)this.Events[DomMouseMoveEvent])(this, e);
		}
		#endregion

		#region public event GeckoDomEventHandler DomSubmit
		[Category("DOM Events")]
		public event GeckoDomEventHandler DomSubmit
		{
			add { this.Events.AddHandler(DomSubmitEvent, value); }
			remove { this.Events.RemoveHandler(DomSubmitEvent, value); }
		}
		private static object DomSubmitEvent = new object();

		/// <summary>Raises the <see cref="DomSubmit"/> event.</summary>
		/// <param name="e">The data for the event.</param>
		protected virtual void OnDomSubmit(GeckoDomEventArgs e)
		{
			if (((GeckoDomEventHandler)this.Events[DomSubmitEvent]) != null)
				((GeckoDomEventHandler)this.Events[DomSubmitEvent])(this, e);
		}
		#endregion

		#region public event GeckoDomEventHandler DomClick
		[Category("DOM Events")]
		public event GeckoDomEventHandler DomClick
		{
			add { this.Events.AddHandler(DomClickEvent, value); }
			remove { this.Events.RemoveHandler(DomClickEvent, value); }
		}
		private static object DomClickEvent = new object();

		/// <summary>Raises the <see cref="DomClick"/> event.</summary>
		/// <param name="e">The data for the event.</param>
		protected virtual void OnDomClick(GeckoDomEventArgs e)
		{
			if (((GeckoDomEventHandler)this.Events[DomClickEvent]) != null)
				((GeckoDomEventHandler)this.Events[DomClickEvent])(this, e);
		}
		#endregion

		#endregion

		#region nsIWindowProvider Members
		//int nsIWindowProvider.provideWindow(nsIDOMWindow aParent, uint aChromeFlags, bool aPositionSpecified, bool aSizeSpecified, nsIURI aURI, nsAString aName, nsAString aFeatures, out bool aWindowIsNew, out nsIDOMWindow ret)
		//{
		//      if (!this.IsDisposed)
		//      {
		//            GeckoCreateWindowEventArgs e = new GeckoCreateWindowEventArgs((GeckoWindowFlags)aChromeFlags);
		//            this.OnCreateWindow(e);

		//            if (e.WebBrowser != null)
		//            {
		//                  aWindowIsNew = true;
		//                  ret = (nsIDOMWindow)e.WebBrowser.Window.DomWindow;
		//                  return 0;
		//            }
		//            else
		//            {
		//                  System.Media.SystemSounds.Beep.Play();
		//            }
		//      }

		//      aWindowIsNew = true;
		//      ret = null;
		//      return -1;
		//}
		#endregion

		#region nsISHistoryListener Members

		#region public event GeckoHistoryEventHandler HistoryNewEntry
		[Category("History")]
		public event GeckoHistoryEventHandler HistoryNewEntry
		{
			add { this.Events.AddHandler(HistoryNewEntryEvent, value); }
			remove { this.Events.RemoveHandler(HistoryNewEntryEvent, value); }
		}
		private static object HistoryNewEntryEvent = new object();

		/// <summary>Raises the <see cref="HistoryNewEntry"/> event.</summary>
		/// <param name="e">The data for the event.</param>
		protected virtual void OnHistoryNewEntry(GeckoHistoryEventArgs e)
		{
			if (((GeckoHistoryEventHandler)this.Events[HistoryNewEntryEvent]) != null)
				((GeckoHistoryEventHandler)this.Events[HistoryNewEntryEvent])(this, e);
		}
		#endregion

		#region public event GeckoHistoryEventHandler HistoryGoBack
		[Category("History")]
		public event GeckoHistoryEventHandler HistoryGoBack
		{
			add { this.Events.AddHandler(HistoryGoBackEvent, value); }
			remove { this.Events.RemoveHandler(HistoryGoBackEvent, value); }
		}
		private static object HistoryGoBackEvent = new object();

		/// <summary>Raises the <see cref="HistoryGoBack"/> event.</summary>
		/// <param name="e">The data for the event.</param>
		protected virtual void OnHistoryGoBack(GeckoHistoryEventArgs e)
		{
			if (((GeckoHistoryEventHandler)this.Events[HistoryGoBackEvent]) != null)
				((GeckoHistoryEventHandler)this.Events[HistoryGoBackEvent])(this, e);
		}
		#endregion

		#region public event GeckoHistoryEventHandler HistoryGoForward
		[Category("History")]
		public event GeckoHistoryEventHandler HistoryGoForward
		{
			add { this.Events.AddHandler(HistoryGoForwardEvent, value); }
			remove { this.Events.RemoveHandler(HistoryGoForwardEvent, value); }
		}
		private static object HistoryGoForwardEvent = new object();

		/// <summary>Raises the <see cref="HistoryGoForward"/> event.</summary>
		/// <param name="e">The data for the event.</param>
		protected virtual void OnHistoryGoForward(GeckoHistoryEventArgs e)
		{
			if (((GeckoHistoryEventHandler)this.Events[HistoryGoForwardEvent]) != null)
				((GeckoHistoryEventHandler)this.Events[HistoryGoForwardEvent])(this, e);
		}
		#endregion

		#region public event GeckoHistoryEventHandler HistoryReload
		[Category("History")]
		public event GeckoHistoryEventHandler HistoryReload
		{
			add { this.Events.AddHandler(HistoryReloadEvent, value); }
			remove { this.Events.RemoveHandler(HistoryReloadEvent, value); }
		}
		private static object HistoryReloadEvent = new object();

		/// <summary>Raises the <see cref="HistoryReload"/> event.</summary>
		/// <param name="e">The data for the event.</param>
		protected virtual void OnHistoryReload(GeckoHistoryEventArgs e)
		{
			if (((GeckoHistoryEventHandler)this.Events[HistoryReloadEvent]) != null)
				((GeckoHistoryEventHandler)this.Events[HistoryReloadEvent])(this, e);
		}
		#endregion

		#region public event GeckoHistoryGotoIndexEventHandler HistoryGotoIndex
		[Category("History")]
		public event GeckoHistoryGotoIndexEventHandler HistoryGotoIndex
		{
			add { this.Events.AddHandler(HistoryGotoIndexEvent, value); }
			remove { this.Events.RemoveHandler(HistoryGotoIndexEvent, value); }
		}
		private static object HistoryGotoIndexEvent = new object();

		/// <summary>Raises the <see cref="HistoryGotoIndex"/> event.</summary>
		/// <param name="e">The data for the event.</param>
		protected virtual void OnHistoryGotoIndex(GeckoHistoryGotoIndexEventArgs e)
		{
			if (((GeckoHistoryGotoIndexEventHandler)this.Events[HistoryGotoIndexEvent]) != null)
				((GeckoHistoryGotoIndexEventHandler)this.Events[HistoryGotoIndexEvent])(this, e);
		}
		#endregion

		#region public event GeckoHistoryPurgeEventHandler HistoryPurge
		[Category("History")]
		public event GeckoHistoryPurgeEventHandler HistoryPurge
		{
			add { this.Events.AddHandler(HistoryPurgeEvent, value); }
			remove { this.Events.RemoveHandler(HistoryPurgeEvent, value); }
		}
		private static object HistoryPurgeEvent = new object();

		/// <summary>Raises the <see cref="HistoryPurge"/> event.</summary>
		/// <param name="e">The data for the event.</param>
		protected virtual void OnHistoryPurge(GeckoHistoryPurgeEventArgs e)
		{
			if (((GeckoHistoryPurgeEventHandler)this.Events[HistoryPurgeEvent]) != null)
				((GeckoHistoryPurgeEventHandler)this.Events[HistoryPurgeEvent])(this, e);
		}
		#endregion

		void nsISHistoryListener.OnHistoryNewEntry(nsIURI aNewURI)
		{
			OnHistoryNewEntry(new GeckoHistoryEventArgs(new Uri(nsString.Get(aNewURI.GetSpec))));
		}

		bool nsISHistoryListener.OnHistoryGoBack(nsIURI aBackURI)
		{
			GeckoHistoryEventArgs e = new GeckoHistoryEventArgs(new Uri(nsString.Get(aBackURI.GetSpec)));
			OnHistoryGoBack(e);
			return !e.Cancel;
		}

		bool nsISHistoryListener.OnHistoryGoForward(nsIURI aForwardURI)
		{
			GeckoHistoryEventArgs e = new GeckoHistoryEventArgs(new Uri(nsString.Get(aForwardURI.GetSpec)));
			OnHistoryGoForward(e);
			return !e.Cancel;
		}

		bool nsISHistoryListener.OnHistoryReload(nsIURI aReloadURI, uint aReloadFlags)
		{
			GeckoHistoryEventArgs e = new GeckoHistoryEventArgs(new Uri(nsString.Get(aReloadURI.GetSpec)));
			OnHistoryReload(e);
			return !e.Cancel;
		}

		bool nsISHistoryListener.OnHistoryGotoIndex(int aIndex, nsIURI aGotoURI)
		{
			GeckoHistoryGotoIndexEventArgs e = new GeckoHistoryGotoIndexEventArgs(new Uri(nsString.Get(aGotoURI.GetSpec)), aIndex);
			OnHistoryGotoIndex(e);
			return !e.Cancel;
		}

		bool nsISHistoryListener.OnHistoryPurge(int aNumEntries)
		{
			GeckoHistoryPurgeEventArgs e = new GeckoHistoryPurgeEventArgs(aNumEntries);
			OnHistoryPurge(e);
			return !e.Cancel;
		}

		#endregion

		#region nsITooltipListener Members

		void nsITooltipListener.OnShowTooltip(int aXCoords, int aYCoords, string aTipText)
		{
			ToolTip = new ToolTipWindow();
			ToolTip.Location = PointToScreen(new Point(aXCoords, aYCoords)) + new Size(0, 24);
			ToolTip.Text = aTipText;
			ToolTip.Show();
		}

		ToolTipWindow ToolTip;

		void nsITooltipListener.OnHideTooltip()
		{
			if (ToolTip != null)
			{
				ToolTip.Close();
			}
		}

		#region class ToolTipWindow : Form
		/// <summary>
		/// A window to contain a tool tip.
		/// </summary>
		class ToolTipWindow : Form
		{
			public ToolTipWindow()
			{
				//this.ControlBox = false;
				this.FormBorderStyle = FormBorderStyle.None;
				this.ShowInTaskbar = false;
				this.StartPosition = FormStartPosition.Manual;
				this.VisibleChanged += delegate { UpdateSize(); };

				this.BackColor = SystemColors.Info;
				this.ForeColor = SystemColors.InfoText;
				this.Font = SystemFonts.DialogFont;

				label = new Label();
				label.Location = new Point(5, 5);
				label.AutoSize = true;
				label.SizeChanged += delegate { UpdateSize(); };
				this.Controls.Add(label);
			}

			void UpdateSize()
			{
				this.Size = label.Size + new Size(10, 10);
			}

			Label label;

			public override string Text
			{
				get { return (label == null) ? "" : label.Text; }
				set
				{
					if (label != null)
						label.Text = value;
				}
			}

			protected override bool ShowWithoutActivation
			{
				get { return true; }
			}

			protected override void OnPaint(PaintEventArgs e)
			{
				// draw border and background
				e.Graphics.DrawRectangle(SystemPens.InfoText, 0, 0, Width-1, Height-1);
				e.Graphics.FillRectangle(SystemBrushes.Info, 1, 1, Width-2, Height-2);
			}

			protected override CreateParams CreateParams
			{
				get
				{
					const int CS_DROPSHADOW = 0x20000;

					// adds a soft drop shadow (windows xp or later required)
					CreateParams cp = base.CreateParams;
					cp.ClassStyle |= CS_DROPSHADOW;
					return cp;
				}
			}
		}
		#endregion

		#endregion
	}

	#region public delegate void GeckoHistoryEventHandler(object sender, GeckoHistoryEventArgs e);
	public delegate void GeckoHistoryEventHandler(object sender, GeckoHistoryEventArgs e);

	/// <summary>Provides data for the <see cref="GeckoHistoryEventHandler"/> event.</summary>
	public class GeckoHistoryEventArgs : System.EventArgs
	{
		/// <summary>Creates a new instance of a <see cref="GeckoHistoryEventArgs"/> object.</summary>
		/// <param name="url"></param>
		public GeckoHistoryEventArgs(Uri url)
		{
			_Url = url;
		}

		/// <summary>
		/// Gets the URL of the history entry.
		/// </summary>
		public Uri Url { get { return _Url; } }
		Uri _Url;

		/// <summary>
		/// Gets or sets whether the history action should be cancelled.
		/// </summary>
		public bool Cancel
		{
			get { return _Cancel; }
			set { _Cancel = value; }
		}
		bool _Cancel;
	}
	#endregion

	#region public delegate void GeckoHistoryGotoIndexEventHandler(object sender, GeckoHistoryGotoIndexEventArgs e);
	public delegate void GeckoHistoryGotoIndexEventHandler(object sender, GeckoHistoryGotoIndexEventArgs e);

	/// <summary>Provides data for the <see cref="GeckoHistoryGotoIndexEventHandler"/> event.</summary>
	public class GeckoHistoryGotoIndexEventArgs : GeckoHistoryEventArgs
	{
		/// <summary>Creates a new instance of a <see cref="GeckoHistoryGotoIndexEventArgs"/> object.</summary>
		/// <param name="url"></param>
		public GeckoHistoryGotoIndexEventArgs(Uri url, int index) : base(url)
		{
			_Index = index;
		}

		/// <summary>
		/// Gets the index in history of the document to be loaded.
		/// </summary>
		public int Index
		{
			get { return _Index; }
		}
		int _Index;
	}
	#endregion

	#region public delegate void GeckoHistoryPurgeEventHandler(object sender, GeckoHistoryPurgeEventArgs e);
	public delegate void GeckoHistoryPurgeEventHandler(object sender, GeckoHistoryPurgeEventArgs e);

	/// <summary>Provides data for the <see cref="GeckoHistoryPurgeEventHandler"/> event.</summary>
	public class GeckoHistoryPurgeEventArgs : CancelEventArgs
	{
		/// <summary>Creates a new instance of a <see cref="GeckoHistoryPurgeEventArgs"/> object.</summary>
		/// <param name="count"></param>
		public GeckoHistoryPurgeEventArgs(int count)
		{
			_Count = count;
		}

		/// <summary>
		/// Gets the number of entries to be purged from the history.
		/// </summary>
		public int Count { get { return _Count; } }
		int _Count;
	}
	#endregion

	#region public delegate void GeckoProgressEventHandler(object sender, GeckoProgressEventArgs e);
	public delegate void GeckoProgressEventHandler(object sender, GeckoProgressEventArgs e);

	/// <summary>Provides data for the <see cref="GeckoProgressEventHandler"/> event.</summary>
	public class GeckoProgressEventArgs : System.EventArgs
	{
		/// <summary>Creates a new instance of a <see cref="GeckoProgressEventArgs"/> object.</summary>
		public GeckoProgressEventArgs(int current, int max)
		{
			_CurrentProgress = current;
			_MaximumProgress = max;
		}

		public int CurrentProgress { get { return _CurrentProgress; } }
		int _CurrentProgress;

		public int MaximumProgress { get { return _MaximumProgress; } }
		int _MaximumProgress;
	}
	#endregion

	#region public delegate void GeckoNavigatedEventHandler(object sender, GeckoNavigatedEventArgs e);
	public delegate void GeckoNavigatedEventHandler(object sender, GeckoNavigatedEventArgs e);

	/// <summary>Provides data for the <see cref="GeckoNavigatedEventHandler"/> event.</summary>
	public class GeckoNavigatedEventArgs : System.EventArgs
	{
		/// <summary>Creates a new instance of a <see cref="GeckoNavigatedEventArgs"/> object.</summary>
		/// <param name="value"></param>
		public GeckoNavigatedEventArgs(Uri value, GeckoResponse response)
		{
			_Uri = value;
			_Response = response;
		}

		public Uri Uri { get { return _Uri; } }
		Uri _Uri;

		public GeckoResponse Response { get { return _Response; } }
		GeckoResponse _Response;
	}
	#endregion

	#region public delegate void GeckoNavigatingEventHandler(object sender, GeckoNavigatingEventArgs e);
	public delegate void GeckoNavigatingEventHandler(object sender, GeckoNavigatingEventArgs e);

	/// <summary>Provides data for the <see cref="GeckoNavigatingEventHandler"/> event.</summary>
	public class GeckoNavigatingEventArgs : System.ComponentModel.CancelEventArgs
	{
		/// <summary>Creates a new instance of a <see cref="GeckoNavigatingEventArgs"/> object.</summary>
		/// <param name="value"></param>
		public GeckoNavigatingEventArgs(Uri value)
		{
			_Uri = value;
		}

		public Uri Uri { get { return _Uri; } }
		Uri _Uri;

		public GeckoResponse Response { get { return _Response; } }
		GeckoResponse _Response;
	}
	#endregion

	#region public delegate void GeckoCreateWindowEventHandler(object sender, GeckoCreateWindowEventArgs e);
	public delegate void GeckoCreateWindowEventHandler(object sender, GeckoCreateWindowEventArgs e);

	/// <summary>Provides data for the <see cref="GeckoCreateWindowEventHandler"/> event.</summary>
	public class GeckoCreateWindowEventArgs : System.EventArgs
	{
		/// <summary>Creates a new instance of a <see cref="GeckoCreateWindowEventArgs"/> object.</summary>
		/// <param name="flags"></param>
		public GeckoCreateWindowEventArgs(GeckoWindowFlags flags)
		{
			_Flags = flags;
		}

		public GeckoWindowFlags Flags { get { return _Flags; } }
		GeckoWindowFlags _Flags;

		/// <summary>
		/// Gets or sets the <see cref="GeckoWebBrowser"/> used in the new window.
		/// </summary>
		public GeckoWebBrowser WebBrowser
		{
			get { return _WebBrowser; }
			set { _WebBrowser = value; }
		}
		GeckoWebBrowser _WebBrowser;
	}
	#endregion

	#region public delegate void GeckoWindowSetBoundsEventHandler(object sender, GeckoWindowSetBoundsEventArgs e);
	public delegate void GeckoWindowSetBoundsEventHandler(object sender, GeckoWindowSetBoundsEventArgs e);

	/// <summary>Provides data for the <see cref="GeckoWindowSetBoundsEventHandler"/> event.</summary>
	public class GeckoWindowSetBoundsEventArgs : System.EventArgs
	{
		/// <summary>Creates a new instance of a <see cref="GeckoWindowSetBoundsEventArgs"/> object.</summary>
		/// <param name="bounds"></param>
		/// <param name="specified"></param>
		public GeckoWindowSetBoundsEventArgs(Rectangle bounds, BoundsSpecified specified)
		{
			_Bounds = bounds;
			_BoundsSpecified = specified;
		}

		public Rectangle Bounds { get { return _Bounds; } }
		Rectangle _Bounds;

		public BoundsSpecified BoundsSpecified { get { return _BoundsSpecified; } }
		BoundsSpecified _BoundsSpecified;
	}
	#endregion

	#region public delegate void GeckoContextMenuEventHandler(object sender, GeckoContextMenuEventArgs e);
	public delegate void GeckoContextMenuEventHandler(object sender, GeckoContextMenuEventArgs e);

	/// <summary>Provides data for the <see cref="GeckoContextMenuEventHandler"/> event.</summary>
	public class GeckoContextMenuEventArgs : System.EventArgs
	{
		/// <summary>Creates a new instance of a <see cref="GeckoContextMenuEventArgs"/> object.</summary>
		public GeckoContextMenuEventArgs(Point location, ContextMenu contextMenu, string associatedLink, Uri backgroundImageSrc, Uri imageSrc, GeckoNode targetNode)
		{
			this._Location = location;
			this._ContextMenu = contextMenu;
			this._AssociatedLink = associatedLink;
			this._BackgroundImageSrc = backgroundImageSrc;
			this._ImageSrc = ImageSrc;
			this._TargetNode = targetNode;
		}

		/// <summary>
		/// Gets the location where the context menu will be displayed.
		/// </summary>
		public Point Location
		{
			get { return _Location; }
		}
		Point _Location;

		/// <summary>
		/// Gets or sets the context menu to be displayed.  Set this property to null to disable
		/// the context menu.
		/// </summary>
		public ContextMenu ContextMenu
		{
			get { return _ContextMenu; }
			set { _ContextMenu = value; }
		}
		ContextMenu _ContextMenu;

		public string AssociatedLink
		{
			get { return _AssociatedLink; }
		}
		string _AssociatedLink;

		public Uri BackgroundImageSrc
		{
			get { return _BackgroundImageSrc; }
		}
		Uri _BackgroundImageSrc;

		public Uri ImageSrc
		{
			get { return _ImageSrc; }
		}
		Uri _ImageSrc;

		public GeckoNode TargetNode
		{
			get { return _TargetNode; }
		}
		GeckoNode _TargetNode;
	}
	#endregion

	#region public enum GeckoLoadFlags
	public enum GeckoLoadFlags
	{
		/// <summary>
		/// This is the default value for the load flags parameter.
		/// </summary>
		None = nsIWebNavigationConstants.LOAD_FLAGS_NONE,

		/// <summary>
		/// This flag specifies that the load should have the semantics of an HTML
		/// META refresh (i.e., that the cache should be validated).  This flag is
		/// only applicable to loadURI.
		/// XXX the meaning of this flag is poorly defined.
		/// </summary>
		IsRefresh = nsIWebNavigationConstants.LOAD_FLAGS_IS_REFRESH,

		/// <summary>
		/// This flag specifies that the load should have the semantics of a link
		/// click.  This flag is only applicable to loadURI.
		/// XXX the meaning of this flag is poorly defined.
		/// </summary>
		IsLink = nsIWebNavigationConstants.LOAD_FLAGS_IS_LINK,

		/// <summary>
		/// This flag specifies that history should not be updated.  This flag is only
		/// applicable to loadURI.
		/// </summary>
		BypassHistory = nsIWebNavigationConstants.LOAD_FLAGS_BYPASS_HISTORY,

		/// <summary>
		/// This flag specifies that any existing history entry should be replaced.
		/// This flag is only applicable to loadURI.
		/// </summary>
		ReplaceHistory = nsIWebNavigationConstants.LOAD_FLAGS_REPLACE_HISTORY,

		/// <summary>
		/// This flag specifies that the local web cache should be bypassed, but an
		/// intermediate proxy cache could still be used to satisfy the load.
		/// </summary>
		BypassCache = nsIWebNavigationConstants.LOAD_FLAGS_BYPASS_CACHE,

		/// <summary>
		/// This flag specifies that any intermediate proxy caches should be bypassed
		/// (i.e., that the content should be loaded from the origin server).
		/// </summary>
		BypassProxy = nsIWebNavigationConstants.LOAD_FLAGS_BYPASS_PROXY,

		/// <summary>
		/// This flag specifies that a reload was triggered as a result of detecting
		/// an incorrect character encoding while parsing a previously loaded
		/// document.
		/// </summary>
		CharsetChange = nsIWebNavigationConstants.LOAD_FLAGS_CHARSET_CHANGE,

		/// <summary>
		/// If this flag is set, Stop() will be called before the load starts
		/// and will stop both content and network activity (the default is to
		/// only stop network activity).  Effectively, this passes the
		/// STOP_CONTENT flag to Stop(), in addition to the STOP_NETWORK flag.
		/// </summary>
		StopContent = nsIWebNavigationConstants.LOAD_FLAGS_STOP_CONTENT,

		/// <summary>
		/// A hint this load was prompted by an external program: take care!
		/// </summary>
		FromExternal = nsIWebNavigationConstants.LOAD_FLAGS_FROM_EXTERNAL,

		/// <summary>
		/// This flag specifies that the URI may be submitted to a third-party
		/// server for correction. This should only be applied to non-sensitive
		/// URIs entered by users.
		/// </summary>
		AllowThirdPartyFixup = nsIWebNavigationConstants.LOAD_FLAGS_ALLOW_THIRD_PARTY_FIXUP,

		/// <summary>
		/// This flag specifies that this is the first load in this object.
		/// Set with care, since setting incorrectly can cause us to assume that
		/// nothing was actually loaded in this object if the load ends up being
		/// handled by an external application.
		/// </summary>
		FirstLoad = nsIWebNavigationConstants.LOAD_FLAGS_FIRST_LOAD,
	}
	#endregion

	#region public enum GeckoSecurityState
	public enum GeckoSecurityState
	{
		/// <summary>
		/// This flag indicates that the data corresponding to the request was received over an insecure channel.
		/// </summary>
		Insecure = nsIWebProgressListenerConstants.STATE_IS_INSECURE,
		/// <summary>
		/// This flag indicates an unknown security state.  This may mean that the request is being loaded as part of
		/// a page in which some content was received over an insecure channel.
		/// </summary>
		Broken = nsIWebProgressListenerConstants.STATE_IS_BROKEN,
		/// <summary>
		/// This flag indicates that the data corresponding to the request was received over a secure channel.
		/// The degree of security is expressed by GeckoSecurityStrength.
		/// </summary>
		Secure = nsIWebProgressListenerConstants.STATE_IS_SECURE,
	}
	#endregion

	#region public enum GeckoWindowFlags
	[Flags]
	public enum GeckoWindowFlags
	{
		Default = 0x1,
		WindowBorders = 0x2,
		WindowClose = 0x4,
		WindowResize = 0x8,
		MenuBar = 0x10,
		ToolBar = 0x20,
		LocationBar = 0x40,
		StatusBar = 0x80,
		PersonalToolbar = 0x100,
		ScrollBars = 0x200,
		TitleBar = 0x400,
		Extra = 0x800,

		CreateWithSize = 0x1000,
		CreateWithPosition = 0x2000,

		WindowMin = 0x00004000,
		WindowPopup = 0x00008000,
		WindowRaised = 0x02000000,
		WindowLowered = 0x04000000,
		CenterScreen = 0x08000000,
		Dependent = 0x10000000,
		Modal = 0x20000000,
		OpenAsDialog = 0x40000000,
		OpenAsChrome = unchecked((int)0x80000000),
		All = 0x00000ffe,
	}
	#endregion
}
