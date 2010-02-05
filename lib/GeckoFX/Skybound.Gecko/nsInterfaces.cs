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
using System.Runtime.InteropServices;
using System.Text;
using System.Runtime.CompilerServices;

namespace Skybound.Gecko
{
	[Guid("00000000-0000-0000-c000-000000000046"), ComImport]
	public interface nsISupports
	{
		object QueryInterface(ref Guid iid);
		int AddRef();
		int Release();
	}

	[Guid("a88e5a60-205a-4bb1-94e1-2628daf51eae"), ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	interface nsInterfaces
	{
		[return: MarshalAs(UnmanagedType.IUnknown)] object GetClassObject(ref Guid aClass, ref Guid aIID);
		[return: MarshalAs(UnmanagedType.IUnknown)] object GetClassObjectByContractID(string aContractID, ref Guid aIID);
		[return: MarshalAs(UnmanagedType.IUnknown)] object CreateInstance(ref Guid aClass, nsISupports aDelegate, ref Guid aIID);
		[return: MarshalAs(UnmanagedType.IUnknown)] object CreateInstanceByContractID([MarshalAs(UnmanagedType.LPStr)] string aContractID, nsISupports aDelegate, ref Guid aIID);
	}

	[Guid("8bb35ed9-e332-462d-9155-4a002ab5c958"), ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	interface nsIServiceManager
	{
		[return: MarshalAs(UnmanagedType.IUnknown)] object GetService(ref Guid aClass, ref Guid aIID);
		[return: MarshalAs(UnmanagedType.IUnknown)] object GetServiceByContractID([MarshalAs(UnmanagedType.LPStr)] string aContractID, ref Guid aIID);
		bool IsServiceInstantiated(ref Guid aClass, ref Guid aIID);
		bool IsServiceInstantiatedByContractID([MarshalAs(UnmanagedType.LPStr)] string aContractID, ref Guid aIID);
	}

	[Guid("69E5DF00-7B8B-11d3-AF61-00A024FFC08C"), ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	interface nsIWebBrowser
	{
		void AddWebBrowserListener(nsIWeakReference aListener, ref Guid aIID);
		void RemoveWebBrowserListener(nsIWeakReference aListener, ref Guid aIID);
		nsIWebBrowserChrome GetContainerWindow();
		void SetContainerWindow(nsIWebBrowserChrome containerWindow);
		nsIURIContentListener GetParentURIContentListener();
		void SetParentURIContentListener([MarshalAs(UnmanagedType.IUnknown)] object parentURIContentListener);
		nsIDOMWindow GetContentDOMWindow();
	}

	[Guid("94928ab3-8b63-11d3-989d-001083010e9b"), ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	interface nsIURIContentListener
	{
		bool OnStartURIOpen(nsIURI aURI);
		bool DoContent([MarshalAs(UnmanagedType.LPStr)] string aContentType, bool aIsContentPreferred, nsIRequest aRequest, out IntPtr aContentHandler); // aContentHandler=nsIStreamListener
		bool IsPreferred([MarshalAs(UnmanagedType.LPStr)] string aContentType, [MarshalAs(UnmanagedType.LPStr)] out string aDesiredContentType);
		bool CanHandleContent([MarshalAs(UnmanagedType.LPStr)] string aContentType, bool aIsContentPreferred, [MarshalAs(UnmanagedType.LPStr)] out string aDesiredContentType);
		nsISupports GetLoadCookie();
		void SetLoadCookie(nsISupports aLoadCookie);
		nsIURIContentListener GetParentContentListener();
		void SetParentContentListener(nsIURIContentListener aParentContentListener);
	}

	static class nsIWebBrowserChromeConstants
	{
		public const int STATUS_SCRIPT = 1;
		public const int STATUS_SCRIPT_DEFAULT = 2;
		public const int STATUS_LINK = 3;

		public const int CHROME_DEFAULT = 1;
		public const int CHROME_WINDOW_BORDERS = 2;
		public const int CHROME_WINDOW_CLOSE = 4;
		public const int CHROME_WINDOW_RESIZE = 8;
		public const int CHROME_MENUBAR = 16;
		public const int CHROME_TOOLBAR = 32;
		public const int CHROME_LOCATIONBAR = 64;
		public const int CHROME_STATUSBAR = 128;
		public const int CHROME_PERSONAL_TOOLBAR = 256;
		public const int CHROME_SCROLLBARS = 512;
		public const int CHROME_TITLEBAR = 1024;
		public const int CHROME_EXTRA = 2048;
		public const int CHROME_WITH_SIZE = 4096;
		public const int CHROME_WITH_POSITION = 8192;
		public const int CHROME_WINDOW_MIN = 16384;
		public const int CHROME_WINDOW_POPUP = 32768;
		public const int CHROME_WINDOW_RAISED = 33554432;
		public const int CHROME_WINDOW_LOWERED = 67108864;
		public const int CHROME_CENTER_SCREEN = 134217728;
		public const int CHROME_DEPENDENT = 268435456;
		public const int CHROME_MODAL = 536870912;
		public const int CHROME_OPENAS_DIALOG = 1073741824;
		public const int CHROME_OPENAS_CHROME = unchecked((int)2147483648);
		public const int CHROME_ALL = 4094;
	}

	[Guid("ba434c60-9d52-11d3-afb0-00a024ffc08c"), ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	interface nsIWebBrowserChrome
	{
		void SetStatus(int statusType, [MarshalAs(UnmanagedType.LPWStr)] string status);
		nsIWebBrowser GetWebBrowser();
		void SetWebBrowser(nsIWebBrowser webBrowser);
		int GetChromeFlags();
		void SetChromeFlags(int flags);
		void DestroyBrowserWindow();
		void SizeBrowserTo(int cx, int cy);
		void ShowAsModal();
		bool IsWindowModal();
		void ExitModalEventLoop(int status);
	}

	[Guid("30465632-a777-44cc-90f9-8145475ef999"), ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	interface nsIWindowCreator
	{
		nsIWebBrowserChrome CreateChromeWindow(nsIWebBrowserChrome parent, uint chromeFlags);
	}

	[Guid("002286a8-494b-43b3-8ddd-49e3fc50622b"), ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	interface nsIWindowWatcher
	{
		nsIDOMWindow OpenWindow(nsIDOMWindow aParent, [MarshalAs(UnmanagedType.LPStr)] string aUrl, [MarshalAs(UnmanagedType.LPStr)] string aName, [MarshalAs(UnmanagedType.LPStr)] string aFeatures, nsISupports aArguments);
		void RegisterNotification(nsIObserver aObserver);
		void UnregisterNotification(nsIObserver aObserver);
		nsISimpleEnumerator GetWindowEnumerator();
		IntPtr GetNewPrompter(nsIDOMWindow aParent);
		IntPtr GetNewAuthPrompter(nsIDOMWindow aParent);
		void SetWindowCreator(nsIWindowCreator creator);
		nsIWebBrowserChrome GetChromeForWindow(nsIDOMWindow aWindow);
		nsIDOMWindow GetWindowByName([MarshalAs(UnmanagedType.LPWStr)] string aTargetName, nsIDOMWindow aCurrentWindow);
		nsIDOMWindow GetActiveWindow();
		void SetActiveWindow(nsIDOMWindow aActiveWindow);
	}

	[Guid("5119ac7f-81dd-4061-96a7-71f2cf5efee4"), ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	interface nsIWindowProvider
	{
		[PreserveSig]
		int provideWindow(nsIDOMWindow aParent, uint aChromeFlags, bool aPositionSpecified, bool aSizeSpecified, nsIURI aURI, nsAString aName, nsAString aFeatures, out bool aWindowIsNew, out nsIDOMWindow ret);
	}

	[Guid("b6c2f9e1-53a0-45f2-a2b8-fe37861fe8a8"), ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	interface nsIXULWindow
	{
		IntPtr GetDocShell(); // nsIDocShell
		bool GetIntrinsicallySized();
		void SetIntrinsicallySized(bool aIntrinsicallySized);
		nsIDocShellTreeItem GetPrimaryContentShell();
		nsIDocShellTreeItem GetContentShellById([MarshalAs(UnmanagedType.LPWStr)] string ID);
		void AddChildWindow(nsIXULWindow aChild);
		void RemoveChildWindow(nsIXULWindow aChild);
		void Center(nsIXULWindow aRelative, bool aScreen, bool aAlert);
		void ShowModal();
		uint GetZLevel();
		void SetZLevel(uint aZLevel);
		uint GetContextFlags();
		void SetContextFlags(uint aContextFlags);
		uint GetChromeFlags();
		void SetChromeFlags(uint aChromeFlags);
		nsIXULWindow CreateNewWindow(uint aChromeFlags, [MarshalAs(UnmanagedType.IUnknown)] object aAppShell); // aAppShell=nsIAppShell
		IntPtr GetXULBrowserWindow(); // nsIXULBrowserWindow
		void SetXULBrowserWindow(IntPtr aXULBrowserWindow); // nsIXULBrowserWindow
		void ApplyChromeFlags();
	}

	[Guid("93a28ba2-7e22-11d9-9b6f-000a95d535fa"), ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	interface nsIAppShellService
	{
		nsIXULWindow CreateTopLevelWindow(nsIXULWindow aParent, nsIURI aUrl, uint aChromeMask, int aInitialWidth, int aInitialHeight, [MarshalAs(UnmanagedType.IUnknown)] object aAppShell);
		void CreateHiddenWindow([MarshalAs(UnmanagedType.IUnknown)] object aAppShell); // nsIAppShell
		void DestroyHiddenWindow();
		nsIXULWindow GetHiddenWindow();
		IntPtr GetHiddenDOMWindow(); // nsIDOMWindowInternal
		void GetHiddenWindowAndJSContext(out IntPtr aHiddenDOMWindow, IntPtr aJSContext); // nsIDOMWindowInternal JSContext
		bool GetApplicationProvidedHiddenWindow();
		void RegisterTopLevelWindow(nsIXULWindow aWindow);
		void UnregisterTopLevelWindow(nsIXULWindow aWindow);
		void TopLevelWindowIsModal(nsIXULWindow aWindow, bool aModal);
	}

	[Guid("3aaad312-e09d-4010-a013-78ef653dac99"), ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	interface nsPIWindowWatcher
	{
		void AddWindow(nsIDOMWindow aWindow, nsIWebBrowserChrome aChrome);
		void RemoveWindow(nsIDOMWindow aWindow);
		nsIDOMWindow OpenWindowJS(nsIDOMWindow aParent, [MarshalAs(UnmanagedType.LPStr)] string aUrl, [MarshalAs(UnmanagedType.LPStr)] string aName, [MarshalAs(UnmanagedType.LPStr)] string aFeatures, [MarshalAs(UnmanagedType.Bool)] bool aDialog, [MarshalAs(UnmanagedType.IUnknown)] object aArgs);
		nsIDocShellTreeItem FindItemWithName([MarshalAs(UnmanagedType.LPWStr)] string aName, nsIDocShellTreeItem aRequestor, nsIDocShellTreeItem aOriginalRequestor);
	}

	[Guid("db242e01-e4d9-11d2-9dde-000064657374"), ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	interface nsIObserver
	{
		void Observe(nsISupports aSubject, [MarshalAs(UnmanagedType.LPStr)] string aTopic, [MarshalAs(UnmanagedType.LPWStr)] string aData);
	}

	[Guid("046bc8a0-8015-11d3-af70-00a024ffc08c"), ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	interface nsIBaseWindow
	{
		//void InitWindow(nativeWindow parentNativeWindow, nsIWidget * parentWidget, int x, int y, int cx, int cy);
		void InitWindow(IntPtr parentNativeWindow, IntPtr /* nsIWidget */ parentWidget, int x, int y, int cx, int cy);
		void Create();
		void Destroy();
		void SetPosition(int x, int y);
		void GetPosition(out int x, out int y);
		void SetSize(int cx, int cy, bool fRepaint);
		void GetSize(out int cx, out int cy);
		void SetPositionAndSize(int x, int y, int cx, int cy, bool fRepaint);
		void GetPositionAndSize(out int x, out int y, out int cx, out int cy);
		void Repaint(bool force);
		IntPtr GetParentWidget(); // returns: nsIWidget
		void SetParentWidget(IntPtr aParentWidget);
		IntPtr GetParentNativeWindow(); // returns: nativeWindow
		void SetParentNativeWindow(IntPtr aParentNativeWindow);
		bool GetVisibility();
		void SetVisibility(bool aVisibility);
		bool GetEnabled();
		void SetEnabled(bool aEnabled);
		bool GetBlurSuppression();
		void SetBlurSuppression(bool aBlurSuppression);
		IntPtr GetMainWidget(); // returns: nsIWidget
		void SetFocus();
		[return: MarshalAs(UnmanagedType.LPWStr)] string GetTitle();
		void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string aTitle);
	}

	static class nsIWebNavigationConstants
	{
		public const int LOAD_FLAGS_MASK = 65535;
		public const int LOAD_FLAGS_NONE = 0;
		public const int LOAD_FLAGS_IS_REFRESH = 16;
		public const int LOAD_FLAGS_IS_LINK = 32;
		public const int LOAD_FLAGS_BYPASS_HISTORY = 64;
		public const int LOAD_FLAGS_REPLACE_HISTORY = 128;
		public const int LOAD_FLAGS_BYPASS_CACHE = 256;
		public const int LOAD_FLAGS_BYPASS_PROXY = 512;
		public const int LOAD_FLAGS_CHARSET_CHANGE = 1024;
		public const int LOAD_FLAGS_STOP_CONTENT = 2048;
		public const int LOAD_FLAGS_FROM_EXTERNAL = 4096;
		public const int LOAD_FLAGS_ALLOW_THIRD_PARTY_FIXUP = 8192;
		public const int LOAD_FLAGS_FIRST_LOAD = 16384;

		/****************************************************************************
		* The following flags may be passed as the stop flags parameter to the stop
		* method defined on this interface.
		*/
		public const int STOP_NETWORK = 1;
		public const int STOP_CONTENT = 2;
		public const int STOP_ALL = 3;
	}

	[Guid("f5d9e7b0-d930-11d3-b057-00a024ffc08c"), ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	public interface nsIWebNavigation
	{
		bool GetCanGoBack();
		bool GetCanGoForward();
		void GoBack();
		void GoForward();
		void GotoIndex(int index);
		[PreserveSig] int LoadURI([MarshalAs(UnmanagedType.LPWStr)] string aURI, uint aLoadFlags, nsIURI aReferrer, nsIInputStream aPostData, nsIInputStream aHeaders);
		void Reload(uint aReloadFlags);
		void Stop(uint aStopFlags);
		nsIDOMDocument GetDocument();
		nsURI GetCurrentURI();
		nsIURI GetReferringURI();
		nsISHistory GetSessionHistory();
		void SetSessionHistory([MarshalAs(UnmanagedType.IUnknown)] object aSessionHistory);
	}

	[Guid("3b07f591-e8e1-11d4-9882-00c04fa02f40"), ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	public interface nsISHistoryListener
	{
		void OnHistoryNewEntry(nsIURI aNewURI);
		bool OnHistoryGoBack(nsIURI aBackURI);
		bool OnHistoryGoForward(nsIURI aForwardURI);
		bool OnHistoryReload(nsIURI aReloadURI, uint aReloadFlags);
		bool OnHistoryGotoIndex(int aIndex, nsIURI aGotoURI);
		bool OnHistoryPurge(int aNumEntries);
	}

	[Guid("d1899240-f9d2-11d2-bdd6-000064657374"), ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	public interface nsISimpleEnumerator
	{
		bool HasMoreElements();
		nsISupports GetNext();
	}

	[Guid("9c5d3c58-1dd1-11b2-a1c9-f3699284657a"), ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	public interface nsIWebBrowserFocus
	{
		void Activate();
		void Deactivate();
		void SetFocusAtFirstElement();
		void SetFocusAtLastElement();
		nsIDOMWindow GetFocusedWindow();
		void SetFocusedWindow(nsIDOMWindow aFocusedWindow);
		nsIDOMElement GetFocusedElement();
		void SetFocusedElement(nsIDOMElement aFocusedElement);
	}

	[Guid("dd4e0a6a-210f-419a-ad85-40e8543b9465"), ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	public interface nsIWebBrowserPersist
	{
		// nsICancelable:
		void Cancel(int aReason);

		// nsIWebBrowserPersist:
		uint GetPersistFlags();
		void SetPersistFlags(uint aPersistFlags);
		uint GetCurrentState();
		uint GetResult();
		nsIWebProgressListener GetProgressListener();
		void SetProgressListener(nsIWebProgressListener aProgressListener);
		void SaveURI(nsIURI aURI, nsISupports aCacheKey, nsIURI aReferrer, IntPtr aPostData, [MarshalAs(UnmanagedType.LPStr)] string aExtraHeaders, nsISupports aFile); // aPostData=nsIInputStream
		void SaveChannel(IntPtr aChannel, nsISupports aFile); // aChannel=nsIChannel
		void SaveDocument(nsIDOMDocument aDocument, [MarshalAs(UnmanagedType.IUnknown)] object aFile, nsISupports aDataPath, [MarshalAs(UnmanagedType.LPStr)] string aOutputContentType, uint aEncodingFlags, uint aWrapColumn);
		void CancelSave();
	}

	[Guid("7fb719b3-d804-4964-9596-77cf924ee314"), ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	public interface nsIContextMenuListener2
	{
		void OnShowContextMenu(uint aContextFlags, nsIContextMenuInfo aUtils);
	}

	[Guid("2f977d56-5485-11d4-87e2-0010a4e75ef2"), ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	public interface nsIContextMenuInfo
	{
		nsIDOMEvent GetMouseEvent();
		nsIDOMNode GetTargetNode();
		[PreserveSig] int GetAssociatedLink(nsAString aAssociatedLink);
		imgIContainer GetImageContainer();
		[PreserveSig] int GetImageSrc(out nsIURI result);
		imgIContainer GetBackgroundImageContainer();
		[PreserveSig] int GetBackgroundImageSrc(out nsIURI result);
	}

	[Guid("1a6290e6-8285-4e10-963d-d001f8d327b8"), ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	public interface imgIContainer
	{
		void Init(int aWidth, int aHeight, IntPtr aObserver); // imgIContainerObserver
		IntPtr GetPreferredAlphaChannelFormat(); // gfx_format
		int GetWidth();
		int GetHeight();
		IntPtr GetCurrentFrame(); // gfxIImageFrame
		uint GetNumFrames();
		ushort GetAnimationMode();
		void SetAnimationMode(ushort aAnimationMode);
		IntPtr GetFrameAt(uint index); // gfxIImageFrame
		void AppendFrame(IntPtr item); // gfxIImageFrame
		void RemoveFrame(IntPtr item); // gfxIImageFrame
		void EndFrameDecode(uint framenumber, uint timeout);
		void DecodingComplete();
		void Clear();
		void StartAnimation();
		void StopAnimation();
		void ResetAnimation();
		int GetLoopCount();
		void SetLoopCount(int aLoopCount);
	}

	[Guid("033a1470-8b2a-11d3-af88-00a024ffc08c"), ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	public interface nsIInterfaceRequestor
	{
		IntPtr GetInterface(ref Guid uuid);
	}

	static class nsIEmbeddingSiteWindowConstants
	{
		public const int DIM_FLAGS_POSITION = 1;
		public const int DIM_FLAGS_SIZE_INNER = 2;
		public const int DIM_FLAGS_SIZE_OUTER = 4;
	}

	[Guid("3e5432cd-9568-4bd1-8cbe-d50aba110743"), ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	public interface nsIEmbeddingSiteWindow
	{
		void SetDimensions(uint flags, int x, int y, int cx, int cy);
		void GetDimensions(uint flags, ref int x, ref int y, ref int cx, ref int cy);
		void SetFocus();
		bool GetVisibility();
		void SetVisibility(bool aVisibility);
		[return: MarshalAs(UnmanagedType.LPWStr)] string GetTitle();
		void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string aTitle);
		IntPtr GetSiteWindow();
	}

	[Guid("e932bf55-0a64-4beb-923a-1f32d3661044"), ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	public interface nsIEmbeddingSiteWindow2 : nsIEmbeddingSiteWindow
	{
		new void SetDimensions(uint flags, int x, int y, int cx, int cy);
		new void GetDimensions(uint flags, ref int x, ref int y, ref int cx, ref int cy);
		new void SetFocus();
		new bool GetVisibility();
		new void SetVisibility(bool aVisibility);
		[return: MarshalAs(UnmanagedType.LPWStr)] new string GetTitle();
		new void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string aTitle);
		new IntPtr GetSiteWindow();

		void Blur();
	}

	[Guid("29fb2a18-1dd2-11b2-8dd9-a6fd5d5ad12f"), ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	public interface nsIDOM3Node
	{
		void GetBaseURI(nsAString aBaseURI);
		ushort CompareDocumentPosition(nsIDOMNode other);
		void GetTextContent(nsAString aTextContent);
		void SetTextContent(nsAString aTextContent);
		bool IsSameNode(nsIDOMNode other);
		void LookupPrefix(nsAString namespaceURI, nsAString _retval);
		bool IsDefaultNamespace(nsAString namespaceURI);
		void LookupNamespaceURI(nsAString prefix, nsAString _retval);
		bool IsEqualNode(nsIDOMNode arg);
		nsISupports GetFeature(nsAString feature, nsAString version);
		IntPtr SetUserData([MarshalAs(UnmanagedType.LPStr)] string key, IntPtr data, IntPtr handler); // data=nsIVariant, handler=nsIDOMUserDataHandler, returns nsIVariant
		IntPtr GetUserData([MarshalAs(UnmanagedType.LPStr)] string key); // returns nsIVariant
	}

	[Guid("9188bc86-f92e-11d2-81ef-0060083a0bcf"), ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	public interface nsISupportsWeakReference
	{
		nsIWeakReference GetWeakReference();
	}

	[Guid("9188bc85-f92e-11d2-81ef-0060083a0bcf"), ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	public interface nsIWeakReference
	{
		IntPtr QueryReferent(ref Guid uuid);
	}

	[Guid("7d935d63-6d2a-4600-afb5-9a4f7d68b825"), ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	public interface nsIDocShellTreeItem
	{
		[return: MarshalAs(UnmanagedType.LPWStr)] string GetName();
		void SetName([MarshalAs(UnmanagedType.LPWStr)] string aName);
		bool NameEquals([MarshalAs(UnmanagedType.LPWStr)] string name);
		int GetItemType();
		void SetItemType(int aItemType);
		nsIDocShellTreeItem GetParent();
		nsIDocShellTreeItem GetSameTypeParent();
		nsIDocShellTreeItem GetRootTreeItem();
		nsIDocShellTreeItem GetSameTypeRootTreeItem();
		nsIDocShellTreeItem FindItemWithName([MarshalAs(UnmanagedType.LPWStr)] string name, [MarshalAs(UnmanagedType.IUnknown)] object aRequestor, nsIDocShellTreeItem aOriginalRequestor);
		IntPtr GetTreeOwner(); // nsIDocShellTreeOwner
		void SetTreeOwner(IntPtr treeOwner);
		int GetChildOffset();
		void SetChildOffset(int aChildOffset);
	}

	[Guid("09b54ec1-d98a-49a9-bc95-3219e8b55089"), ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	public interface nsIDocShellTreeItem19
	{
		// nsIDocShellTreeNode:
		int GetChildCount();
		void AddChild(nsIDocShellTreeItem child);
		void RemoveChild(nsIDocShellTreeItem child);
		nsIDocShellTreeItem GetChildAt(int index);
		nsIDocShellTreeItem FindChildWithName([MarshalAs(UnmanagedType.LPWStr)] string aName, bool aRecurse, bool aSameType, nsIDocShellTreeItem aRequestor, nsIDocShellTreeItem aOriginalRequestor);

		// nsIDocShellTreeItem:
		[return: MarshalAs(UnmanagedType.LPWStr)] string GetName();
		void SetName([MarshalAs(UnmanagedType.LPWStr)] string aName);
		bool NameEquals([MarshalAs(UnmanagedType.LPWStr)] string name);
		int GetItemType();
		void SetItemType(int aItemType);
		nsIDocShellTreeItem GetParent();
		nsIDocShellTreeItem GetSameTypeParent();
		nsIDocShellTreeItem GetRootTreeItem();
		nsIDocShellTreeItem GetSameTypeRootTreeItem();
		nsIDocShellTreeItem FindItemWithName([MarshalAs(UnmanagedType.LPWStr)] string name, nsISupports aRequestor, nsIDocShellTreeItem aOriginalRequestor);
		IntPtr GetTreeOwner(); // nsIDocShellTreeOwner
		void SetTreeOwner(IntPtr treeOwner); // nsIDocShellTreeOwner
	}

	static class nsIDocShellTreeItemConstants
	{
		public const int typeChrome = 0;
		public const int typeContent = 1;
		public const int typeContentWrapper = 2;
		public const int typeChromeWrapper = 3;
		public const int typeAll = 2147483647;
	}

	static class nsIWebProgressListenerConstants
	{
		public const int STATE_START = 1;
		public const int STATE_REDIRECTING = 2;
		public const int STATE_TRANSFERRING = 4;
		public const int STATE_NEGOTIATING = 8;
		public const int STATE_STOP = 16;
		public const int STATE_IS_REQUEST = 65536;
		public const int STATE_IS_DOCUMENT = 131072;
		public const int STATE_IS_NETWORK = 262144;
		public const int STATE_IS_WINDOW = 524288;
		public const int STATE_RESTORING = 16777216;
		public const int STATE_IS_INSECURE = 4;
		public const int STATE_IS_BROKEN = 1;
		public const int STATE_IS_SECURE = 2;
		public const int STATE_SECURE_HIGH = 262144;
		public const int STATE_SECURE_MED = 65536;
		public const int STATE_SECURE_LOW = 131072;
	}

	[Guid("570f39d1-efd0-11d3-b093-00a024ffc08c"), ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	public interface nsIWebProgressListener
	{
		void OnStateChange(nsIWebProgress aWebProgress, nsIRequest aRequest, int aStateFlags, int aStatus);
		void OnProgressChange(nsIWebProgress aWebProgress, nsIRequest aRequest, int aCurSelfProgress, int aMaxSelfProgress, int aCurTotalProgress, int aMaxTotalProgress);
		void OnLocationChange(nsIWebProgress aWebProgress, nsIRequest aRequest, nsIURI aLocation);
		void OnStatusChange(nsIWebProgress aWebProgress, nsIRequest aRequest, int aStatus, [MarshalAs(UnmanagedType.LPWStr)] string aMessage);
		void OnSecurityChange(nsIWebProgress aWebProgress, nsIRequest aRequest, int aState);
	}

	[Guid("ef6bfbd2-fd46-48d8-96b7-9f8f0fd387fe"), ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	public interface nsIRequest
	{
		void GetName(nsACString aName);
		bool IsPending();
		int GetStatus();
		void Cancel(int aStatus);
		void Suspend();
		void Resume();
		IntPtr GetLoadGroup(); // nsILoadGroup
		void SetLoadGroup(IntPtr aLoadGroup);
		int GetLoadFlags();
		void SetLoadFlags(int aLoadFlags);
	}

	[Guid("570f39d0-efd0-11d3-b093-00a024ffc08c"), ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	public interface nsIWebProgress
	{
		void AddProgressListener(nsIWebProgressListener aListener, int aNotifyMask);
		void RemoveProgressListener(nsIWebProgressListener aListener);
		nsIDOMWindow GetDOMWindow();
		bool GetIsLoadingDocument();
	}

	[Guid("07a22cc0-0ce5-11d3-9331-00104ba0fd40"), ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	public interface nsIURI
	{
		void GetSpec(nsAUTF8String outSpec);
		void SetSpec(nsAUTF8String inSpec);
		void GetPrePath(nsAUTF8String outPrePath);
		void GetScheme(nsAUTF8String outScheme);
		void SetScheme(nsAUTF8String inScheme);
		void GetUserPass(nsAUTF8String outUserPass);
		void SetUserPass(nsAUTF8String inUserPass);
		void GetUsername(nsAUTF8String outUsername);
		void SetUsername(nsAUTF8String aUsername);
		void GetPassword(nsAUTF8String aUsername);
		void SetPassword(nsAUTF8String aPassword);
		void GetHostPort(nsAUTF8String aHostPort);
		void SetHostPort(nsAUTF8String aHostPort);
		void GetHost(nsAUTF8String aHost);
		void SetHost(nsAUTF8String aHost);
		int GetPort();
		void SetPort(int aPort);
		void GetPath(nsAUTF8String aPath);
		void SetPath(nsAUTF8String aPath);
		bool Equals(nsIURI other);
		bool SchemeIs([MarshalAs(UnmanagedType.LPStr)] string scheme);
		nsIURI Clone();
		void Resolve(nsACString relativePath, nsACString resolved);
		void GetAsciiSpec(nsACString spec);
		void GetAsciiHost(nsACString spec);
		void GetOriginCharset(nsACString charset);
	}

	[Guid("df31c120-ded6-11d1-bd85-00805f8ae3f4"), ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	public interface nsIDOMEventListener
	{
		void HandleEvent(nsIDOMEvent e);
	}

	[Guid("a66b7b80-ff46-bd97-0080-5f8ae38add32"), ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	public interface nsIDOMEvent
	{
		void GetType(nsAString aType);
		nsIDOMEventTarget GetTarget();
		nsIDOMEventTarget GetCurrentTarget();
		ushort GetEventPhase();
		bool GetBubbles();
		bool GetCancelable();
		IntPtr GetTimeStamp(); // DOMTimeStamp
		void StopPropagation();
		void PreventDefault();
		void InitEvent(nsACString eventTypeArg, bool canBubbleArg, bool cancelableArg);
	}

	[Guid("a6cf90c3-15b3-11d2-932e-00805f8add32"), ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	public interface nsIDOMUIEvent : nsIDOMEvent
	{
		// nsIDOMEvent:
		new void GetType(nsAString aType);
		new nsIDOMEventTarget GetTarget();
		new nsIDOMEventTarget GetCurrentTarget();
		new ushort GetEventPhase();
		new bool GetBubbles();
		new bool GetCancelable();
		new IntPtr GetTimeStamp(); // DOMTimeStamp
		new void StopPropagation();
		new void PreventDefault();
		new void InitEvent(nsACString eventTypeArg, bool canBubbleArg, bool cancelableArg);

		// nsIDOMUIEvent:
		IntPtr GetView(); // nsIDOMAbstractView
		int GetDetail();
		void InitUIEvent(nsAString typeArg, bool canBubbleArg, bool cancelableArg, IntPtr /*nsIDOMAbstractView*/ viewArg, int detailArg);
	}

	[Guid("028e0e6e-8b01-11d3-aae7-0010838a3123"), ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	public interface nsIDOMKeyEvent : nsIDOMUIEvent
	{
		// nsIDOMEvent:
		new void GetType(nsAString aType);
		new nsIDOMEventTarget GetTarget();
		new nsIDOMEventTarget GetCurrentTarget();
		new ushort GetEventPhase();
		new bool GetBubbles();
		new bool GetCancelable();
		new IntPtr GetTimeStamp(); // DOMTimeStamp
		new void StopPropagation();
		new void PreventDefault();
		new void InitEvent(nsACString eventTypeArg, bool canBubbleArg, bool cancelableArg);

		// nsIDOMUIEvent:
		new IntPtr GetView(); // nsIDOMAbstractView
		new int GetDetail();
		new void InitUIEvent(nsAString typeArg, bool canBubbleArg, bool cancelableArg, IntPtr /*nsIDOMAbstractView*/ viewArg, int detailArg);

		// nsIDOMKeyEvent:
		uint GetCharCode();
		uint GetKeyCode();
		bool GetAltKey();
		bool GetCtrlKey();
		bool GetShiftKey();
		bool GetMetaKey();
		void InitKeyEvent(nsAString typeArg, bool canBubbleArg, bool cancelableArg, IntPtr /*nsIDOMAbstractView*/ viewArg, bool ctrlKeyArg, bool altKeyArg, bool shiftKeyArg, bool metaKeyArg, uint keyCodeArg, uint charCodeArg);
	}

	[Guid("ff751edc-8b02-aae7-0010-8301838a3123"), ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	public interface nsIDOMMouseEvent : nsIDOMUIEvent
	{
		// nsIDOMEvent:
		new void GetType(nsAString aType);
		new nsIDOMEventTarget GetTarget();
		new nsIDOMEventTarget GetCurrentTarget();
		new ushort GetEventPhase();
		new bool GetBubbles();
		new bool GetCancelable();
		new UInt64 GetTimeStamp(); // DOMTimeStamp
		new void StopPropagation();
		new void PreventDefault();
		new void InitEvent(nsACString eventTypeArg, bool canBubbleArg, bool cancelableArg);

		// nsIDOMUIEvent:
		new IntPtr GetView(); // nsIDOMAbstractView
		new int GetDetail();
		new void InitUIEvent(nsAString typeArg, bool canBubbleArg, bool cancelableArg, IntPtr /*nsIDOMAbstractView*/ viewArg, int detailArg);

		// nsIDOMMouseEvent:
		int GetScreenX();
		int GetScreenY();
		int GetClientX();
		int GetClientY();
		bool GetCtrlKey();
		bool GetShiftKey();
		bool GetAltKey();
		int GetMetaKey();
		ushort GetButton();
		nsIDOMEventTarget GetRelatedTarget();
		void InitMouseEvent(nsAString typeArg, bool canBubbleArg, bool cancelableArg, IntPtr /*nsIDOMAbstractView*/ viewArg, int detailArg, int screenXArg, int screenYArg, int clientXArg, int clientYArg, bool ctrlKeyArg, bool altKeyArg, bool shiftKeyArg, bool metaKeyArg, ushort buttonArg, nsIDOMEventTarget relatedTargetArg);

	}

	[Guid("a6cf906b-15b3-11d2-932e-00805f8add32"), ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	public interface nsIDOMWindow
	{
		nsIDOMDocument GetDocument();
		nsIDOMWindow GetParent();
		nsIDOMWindow GetTop();
		IntPtr GetScrollbars(); // nsIDOMBarProp
		IntPtr GetFrames(); // nsIDOMWindowCollection
		void GetName(nsAString aName);
		void SetName(nsAString aName);
		float GetTextZoom();
		void SetTextZoom(float aTextZoom);
		int GetScrollX();
		int GetScrollY();
		void ScrollTo(int xScroll, int yScroll);
		void ScrollBy(int xScrollDif, int yScrollDif);
		nsISelection GetSelection(); // nsISelection
		void ScrollByLines(int numLines);
		void ScrollByPages(int numPages);
		void SizeToContent();
	}

	#if GECKO_1_8
	[Guid("65455132-b96a-40ec-adea-52fa22b1028c"), ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	#elif GECKO_1_9
	[Guid("73c5fa35-3add-4c87-a303-a850ccf4d65a"), ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	#endif
	public interface nsIDOMWindow2 : nsIDOMWindow
	{
		// nsIDOMWindow:
		new nsIDOMDocument GetDocument();
		new nsIDOMWindow GetParent();
		new nsIDOMWindow GetTop();
		new IntPtr GetScrollbars(); // nsIDOMBarProp
		new nsIDOMWindowCollection GetFrames();
		new void GetName(nsAString aName);
		new void SetName(nsAString aName);
		new float GetTextZoom();
		new void SetTextZoom(float aTextZoom);
		new int GetScrollX();
		new int GetScrollY();
		new void ScrollTo(int xScroll, int yScroll);
		new void ScrollBy(int xScrollDif, int yScrollDif);
		new nsISelection GetSelection(); // nsISelection
		new void ScrollByLines(int numLines);
		new void ScrollByPages(int numPages);
		new void SizeToContent();

		// nsIDOMWindow2:
		nsIDOMEventTarget GetWindowRoot();
		#if GECKO_1_9
		void GetApplicationCache(IntPtr aApplicationCache); // nsIDOMOfflineResourceList
		#endif
	}

	[Guid("b2c7ed59-8634-4352-9e37-5484c8b6e4e1"), ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	public interface nsISelection
	{
		nsIDOMNode GetAnchorNode();
		int GetAnchorOffset();
		nsIDOMNode GetFocusNode();
		int GetFocusOffset();
		bool GetIsCollapsed();
		int GetRangeCount();
		nsIDOMRange GetRangeAt(int index);
		void Collapse(nsIDOMNode parentNode, int offset);
		void Extend(nsIDOMNode parentNode, int offset);
		void CollapseToStart();
		void CollapseToEnd();
		bool ContainsNode(nsIDOMNode node, bool partlyContained);
		void SelectAllChildren(nsIDOMNode parentNode);
		void AddRange(nsIDOMRange range);
		void RemoveRange(nsIDOMRange range);
		void RemoveAllRanges();
		void DeleteFromDocument();
		void SelectionLanguageChange(bool langRTL);
		[return: MarshalAs(UnmanagedType.LPWStr)] string ToString();
	}

	[Guid("a6cf90ce-15b3-11d2-932e-00805f8add32"), ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	public interface nsIDOMRange
	{
		nsIDOMNode GetStartContainer();
		int GetStartOffset();
		nsIDOMNode GetEndContainer();
		int GetEndOffset();
		bool GetCollapsed();
		nsIDOMNode GetCommonAncestorContainer();
		void SetStart(nsIDOMNode refNode, int offset);
		void SetEnd(nsIDOMNode refNode, int offset);
		void SetStartBefore(nsIDOMNode refNode);
		void SetStartAfter(nsIDOMNode refNode);
		void SetEndBefore(nsIDOMNode refNode);
		void SetEndAfter(nsIDOMNode refNode);
		void Collapse(bool toStart);
		void SelectNode(nsIDOMNode refNode);
		void SelectNodeContents(nsIDOMNode refNode);
		short CompareBoundaryPoints(ushort how, nsIDOMRange sourceRange);
		void DeleteContents();
		nsIDOMNode ExtractContents();
		nsIDOMNode CloneContents();
		void InsertNode(nsIDOMNode newNode);
		void SurroundContents(nsIDOMNode newParent);
		nsIDOMRange CloneRange();
		void ToString(nsAString _retval);
		void Detach();
	}

	/* GECKO 1.9
	[Guid("73c5fa35-3add-4c87-a303-a850ccf4d65a"), ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	interface nsIDOMWindow2
	{
		// nsIDOMWindow:
		nsIDOMDocument GetDocument();
		nsIDOMWindow GetParent();
		nsIDOMWindow GetTop();
		IntPtr GetScrollbars(); // nsIDOMBarProp
		nsIDOMWindowCollection GetFrames();
		void GetName(nsAString aName);
		void SetName(nsAString aName);
		float GetTextZoom();
		void SetTextZoom(float aTextZoom);
		int GetScrollX();
		int GetScrollY();
		void ScrollTo(int xScroll, int yScroll);
		void ScrollBy(int xScrollDif, int yScrollDif);
		IntPtr GetSelection(); // nsISelection
		void ScrollByLines(int numLines);
		void ScrollByPages(int numPages);
		void SizeToContent();

		// nsIDOMWindow2:
		nsIDOMEventTarget GetWindowRoot();
		IntPtr GetApplicationCache(); // nsIDOMOfflineResourceList
	}
	*/

	[Guid("a6cf906f-15b3-11d2-932e-00805f8add32"), ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	public interface nsIDOMWindowCollection
	{
		uint GetLength();
		nsIDOMWindow Item(uint index);
		nsIDOMWindow NamedItem(nsAString name);
	}

	[Guid("1c773b30-d1cf-11d2-bd95-00805f8ae3f4"), ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	public interface nsIDOMEventTarget
	{
		void AddEventListener(nsAString type, nsIDOMEventListener listener, bool useCapture);
		void RemoveEventListener(nsAString type, nsIDOMEventListener listener, bool useCapture);
		bool DispatchEvent(nsIDOMEvent evt);
	}

	static class nsIDOMNodeConstants
	{
		public const int ELEMENT_NODE = 1;
		public const int ATTRIBUTE_NODE = 2;
		public const int TEXT_NODE = 3;
		public const int CDATA_SECTION_NODE = 4;
		public const int ENTITY_REFERENCE_NODE = 5;
		public const int ENTITY_NODE = 6;
		public const int PROCESSING_INSTRUCTION_NODE = 7;
		public const int COMMENT_NODE = 8;
		public const int DOCUMENT_NODE = 9;
		public const int DOCUMENT_TYPE_NODE = 10;
		public const int DOCUMENT_FRAGMENT_NODE = 11;
		public const int NOTATION_NODE = 12;
	}

	[Guid("a6cf907b-15b3-11d2-932e-00805f8add32"), ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	public interface nsIDOMNamedNodeMap
	{
		nsIDOMNode GetNamedItem(nsAString name);
		nsIDOMNode SetNamedItem(nsIDOMNode arg);
		nsIDOMNode RemoveNamedItem(nsAString name);
		nsIDOMNode Item(int index);
		int GetLength();
		nsIDOMNode GetNamedItemNS(nsAString namespaceURI, nsAString localName);
		nsIDOMNode SetNamedItemNS(nsIDOMNode arg);
		nsIDOMNode RemoveNamedItemNS(nsAString namespaceURI, nsAString localName);
	}

	[Guid("a6cf907c-15b3-11d2-932e-00805f8add32"), ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	public  interface nsIDOMNode
	{
		void GetNodeName(nsAString aNodeName);
		void GetNodeValue(nsAString aNodeValue);
		void SetNodeValue(nsAString aNodeValue);
		ushort GetNodeType();
		nsIDOMNode GetParentNode();
		nsIDOMNodeList GetChildNodes();
		nsIDOMNode GetFirstChild();
		nsIDOMNode GetLastChild();
		nsIDOMNode GetPreviousSibling();
		nsIDOMNode GetNextSibling();
		nsIDOMNamedNodeMap GetAttributes();
		nsIDOMDocument GetOwnerDocument();
		nsIDOMNode InsertBefore(nsIDOMNode newChild, nsIDOMNode refChild);
		nsIDOMNode ReplaceChild(nsIDOMNode newChild, nsIDOMNode oldChild);
		nsIDOMNode RemoveChild(nsIDOMNode oldChild);
		nsIDOMNode AppendChild(nsIDOMNode newChild);
		bool HasChildNodes();
		nsIDOMNode CloneNode(bool deep);
		void Normalize();
		bool IsSupported(nsAString feature, nsAString version);
		void GetNamespaceURI(nsAString aNamespaceURI);
		void GetPrefix(nsAString aPrefix);
		void SetPrefix(nsAString aPrefix);
		void GetLocalName(nsAString aLocalName);
		bool HasAttributes();
	}

	[Guid("a6cf907d-15b3-11d2-932e-00805f8add32"), ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	public interface nsIDOMNodeList
	{
		nsIDOMNode Item(int index);
		int GetLength();
	}

	[Guid("a6cf9070-15b3-11d2-932e-00805f8add32"), ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	public interface nsIDOMAttr : nsIDOMNode
	{
		// nsIDOMNode:
		new void GetNodeName(nsAString aNodeName);
		new void GetNodeValue(nsAString aNodeValue);
		new void SetNodeValue(nsAString aNodeValue);
		new ushort GetNodeType();
		new nsIDOMNode GetParentNode();
		new nsIDOMNodeList GetChildNodes();
		new nsIDOMNode GetFirstChild();
		new nsIDOMNode GetLastChild();
		new nsIDOMNode GetPreviousSibling();
		new nsIDOMNode GetNextSibling();
		new nsIDOMNamedNodeMap GetAttributes();
		new nsIDOMDocument GetOwnerDocument();
		new nsIDOMNode InsertBefore(nsIDOMNode newChild, nsIDOMNode refChild);
		new nsIDOMNode ReplaceChild(nsIDOMNode newChild, nsIDOMNode oldChild);
		new nsIDOMNode RemoveChild(nsIDOMNode oldChild);
		new nsIDOMNode AppendChild(nsIDOMNode newChild);
		new bool HasChildNodes();
		new nsIDOMNode CloneNode(bool deep);
		new void Normalize();
		new bool IsSupported(nsAString feature, nsAString version);
		new void GetNamespaceURI(nsAString aNamespaceURI);
		new void GetPrefix(nsAString aPrefix);
		new void SetPrefix(nsAString aPrefix);
		new void GetLocalName(nsAString aLocalName);
		new bool HasAttributes();

		void GetName(nsAString aName);
		bool GetSpecified();
		void GetValue(nsAString aValue);
		void SetValue(nsAString aValue);
		nsIDOMElement GetOwnerElement();
	}

	[Guid("a6cf9078-15b3-11d2-932e-00805f8add32"), ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	public interface nsIDOMElement : nsIDOMNode
	{
		// nsIDOMNode:
		new void GetNodeName(nsAString aNodeName);
		new void GetNodeValue(nsAString aNodeValue);
		new void SetNodeValue(nsAString aNodeValue);
		new ushort GetNodeType();
		new nsIDOMNode GetParentNode();
		new nsIDOMNodeList GetChildNodes();
		new nsIDOMNode GetFirstChild();
		new nsIDOMNode GetLastChild();
		new nsIDOMNode GetPreviousSibling();
		new nsIDOMNode GetNextSibling();
		new nsIDOMNamedNodeMap GetAttributes();
		new nsIDOMDocument GetOwnerDocument();
		new nsIDOMNode InsertBefore(nsIDOMNode newChild, nsIDOMNode refChild);
		new nsIDOMNode ReplaceChild(nsIDOMNode newChild, nsIDOMNode oldChild);
		new nsIDOMNode RemoveChild(nsIDOMNode oldChild);
		new nsIDOMNode AppendChild(nsIDOMNode newChild);
		new bool HasChildNodes();
		new nsIDOMNode CloneNode(bool deep);
		new void Normalize();
		new bool IsSupported(nsAString feature, nsAString version);
		new void GetNamespaceURI(nsAString aNamespaceURI);
		new void GetPrefix(nsAString aPrefix);
		new void SetPrefix(nsAString aPrefix);
		new void GetLocalName(nsAString aLocalName);
		new bool HasAttributes();

		void GetTagName(nsAString aTagName);
		void GetAttribute(nsAString name, nsAString _retval);
		void SetAttribute(nsAString name, nsAString value);
		void RemoveAttribute(nsAString name);
		nsIDOMAttr GetAttributeNode(nsAString name);
		nsIDOMAttr SetAttributeNode(nsIDOMAttr newAttr);
		nsIDOMAttr RemoveAttributeNode(nsIDOMAttr oldAttr);
		nsIDOMNodeList GetElementsByTagName(nsAString name);
		void GetAttributeNS(nsAString namespaceURI, nsAString localName, nsAString _retval);
		void SetAttributeNS(nsAString namespaceURI, nsAString qualifiedName, nsAString value);
		void RemoveAttributeNS(nsAString namespaceURI, nsAString localName);
		nsIDOMAttr GetAttributeNodeNS(nsAString namespaceURI, nsAString localName);
		void SetAttributeNodeNS(nsIDOMAttr newAttr);
		nsIDOMNodeList GetElementsByTagNameNS(nsAString namespaceURI, nsAString localName);
		bool HasAttribute(nsAString name);
		bool HasAttributeNS(nsAString namespaceURI, nsAString localName);
	}

	[Guid("a6cf9075-15b3-11d2-932e-00805f8add32"), ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	public interface nsIDOMDocument : nsIDOMNode
	{
		// nsIDOMNode:
		new void GetNodeName(nsAString aNodeName);
		new void GetNodeValue(nsAString aNodeValue);
		new void SetNodeValue(nsAString aNodeValue);
		new ushort GetNodeType();
		new nsIDOMNode GetParentNode();
		new nsIDOMNodeList GetChildNodes();
		new nsIDOMNode GetFirstChild();
		new nsIDOMNode GetLastChild();
		new nsIDOMNode GetPreviousSibling();
		new nsIDOMNode GetNextSibling();
		new nsIDOMNamedNodeMap GetAttributes();
		new nsIDOMDocument GetOwnerDocument();
		new nsIDOMNode InsertBefore(nsIDOMNode newChild, nsIDOMNode refChild);
		new nsIDOMNode ReplaceChild(nsIDOMNode newChild, nsIDOMNode oldChild);
		new nsIDOMNode RemoveChild(nsIDOMNode oldChild);
		new nsIDOMNode AppendChild(nsIDOMNode newChild);
		new bool HasChildNodes();
		new nsIDOMNode CloneNode(bool deep);
		new void Normalize();
		new bool IsSupported(nsAString feature, nsAString version);
		new void GetNamespaceURI(nsAString aNamespaceURI);
		new void GetPrefix(nsAString aPrefix);
		new void SetPrefix(nsAString aPrefix);
		new void GetLocalName(nsAString aLocalName);
		new bool HasAttributes();

		IntPtr GetDoctype(); // nsIDOMDocumentType
		IntPtr GetImplementation(); // nsIDOMDOMImplementation
		nsIDOMElement GetDocumentElement();
		nsIDOMElement CreateElement(nsAString tagName);
		IntPtr CreateDocumentFragment(); // nsIDOMDocumentFragment
		nsIDOMNode CreateTextNode(nsAString data); // nsIDOMText
		IntPtr CreateComment(nsAString data); // nsIDOMComment
		IntPtr CreateCDATASection(nsAString data); // nsIDOMCDATASection
		IntPtr CreateProcessingInstruction(nsAString target, nsAString data); // nsIDOMProcessingInstruction
		nsIDOMAttr CreateAttribute(nsAString name);
		IntPtr CreateEntityReference(nsAString name); // nsIDOMEntityReference
		nsIDOMNodeList GetElementsByTagName(nsAString tagname);
		nsIDOMNode ImportNode(nsIDOMNode importedNode, bool deep);
		nsIDOMElement CreateElementNS(nsAString namespaceURI, nsAString qualifiedName);
		nsIDOMAttr CreateAttributeNS(nsAString namespaceURI, nsAString qualifiedName);
		nsIDOMNodeList GetElementsByTagNameNS(nsAString namespaceURI, nsAString localName);
		nsIDOMElement GetElementById(nsAString elementId);
	}

	[Guid("a6cf9085-15b3-11d2-932e-00805f8add32"), ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	interface nsIDOMHTMLElement : nsIDOMElement
	{
		// nsIDOMNode:
		new void GetNodeName(nsAString aNodeName);
		new void GetNodeValue(nsAString aNodeValue);
		new void SetNodeValue(nsAString aNodeValue);
		new ushort GetNodeType();
		new nsIDOMNode GetParentNode();
		new nsIDOMNodeList GetChildNodes();
		new nsIDOMNode GetFirstChild();
		new nsIDOMNode GetLastChild();
		new nsIDOMNode GetPreviousSibling();
		new nsIDOMNode GetNextSibling();
		new nsIDOMNamedNodeMap GetAttributes();
		new nsIDOMDocument GetOwnerDocument();
		new nsIDOMNode InsertBefore(nsIDOMNode newChild, nsIDOMNode refChild);
		new nsIDOMNode ReplaceChild(nsIDOMNode newChild, nsIDOMNode oldChild);
		new nsIDOMNode RemoveChild(nsIDOMNode oldChild);
		new nsIDOMNode AppendChild(nsIDOMNode newChild);
		new bool HasChildNodes();
		new nsIDOMNode CloneNode(bool deep);
		new void Normalize();
		new bool IsSupported(nsAString feature, nsAString version);
		new void GetNamespaceURI(nsAString aNamespaceURI);
		new void GetPrefix(nsAString aPrefix);
		new void SetPrefix(nsAString aPrefix);
		new void GetLocalName(nsAString aLocalName);
		new bool HasAttributes();

		// nsIDOMElement:
		new void GetTagName(nsAString aTagName);
		new void GetAttribute(nsAString name, nsAString _retval);
		new void SetAttribute(nsAString name, nsAString value);
		new void RemoveAttribute(nsAString name);
		new nsIDOMAttr GetAttributeNode(nsAString name);
		new nsIDOMAttr SetAttributeNode(nsIDOMAttr newAttr);
		new nsIDOMAttr RemoveAttributeNode(nsIDOMAttr oldAttr);
		new nsIDOMNodeList GetElementsByTagName(nsAString name);
		new void GetAttributeNS(nsAString namespaceURI, nsAString localName, nsAString _retval);
		new void SetAttributeNS(nsAString namespaceURI, nsAString qualifiedName, nsAString value);
		new void RemoveAttributeNS(nsAString namespaceURI, nsAString localName);
		new nsIDOMAttr GetAttributeNodeNS(nsAString namespaceURI, nsAString localName);
		new nsIDOMAttr SetAttributeNodeNS(nsIDOMAttr newAttr);
		new nsIDOMNodeList GetElementsByTagNameNS(nsAString namespaceURI, nsAString localName);
		new bool HasAttribute(nsAString name);
		new bool HasAttributeNS(nsAString namespaceURI, nsAString localName);

		// nsIDOMHTMLElement:
		void GetId(nsAString aId);
		void SetId(nsAString aId);
		void GetTitle(nsAString aTitle);
		void SetTitle(nsAString aTitle);
		void GetLang(nsAString aLang);
		void SetLang(nsAString aLang);
		void GetDir(nsAString aDir);
		void SetDir(nsAString aDir);
		void GetClassName(nsAString aClassName);
		void SetClassName(nsAString aClassName);
	}

	[Guid("a6cf9084-15b3-11d2-932e-00805f8add32"), ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	interface nsIDOMHTMLDocument : nsIDOMDocument
	{
		// nsIDOMNode:
		new void GetNodeName(nsAString aNodeName);
		new void GetNodeValue(nsAString aNodeValue);
		new void SetNodeValue(nsAString aNodeValue);
		new ushort GetNodeType();
		new nsIDOMNode GetParentNode();
		new nsIDOMNodeList GetChildNodes();
		new nsIDOMNode GetFirstChild();
		new nsIDOMNode GetLastChild();
		new nsIDOMNode GetPreviousSibling();
		new nsIDOMNode GetNextSibling();
		new nsIDOMNamedNodeMap GetAttributes();
		new nsIDOMDocument GetOwnerDocument();
		new nsIDOMNode InsertBefore(nsIDOMNode newChild, nsIDOMNode refChild);
		new nsIDOMNode ReplaceChild(nsIDOMNode newChild, nsIDOMNode oldChild);
		new nsIDOMNode RemoveChild(nsIDOMNode oldChild);
		new nsIDOMNode AppendChild(nsIDOMNode newChild);
		new bool HasChildNodes();
		new nsIDOMNode CloneNode(bool deep);
		new void Normalize();
		new bool IsSupported(nsAString feature, nsAString version);
		new void GetNamespaceURI(nsAString aNamespaceURI);
		new void GetPrefix(nsAString aPrefix);
		new void SetPrefix(nsAString aPrefix);
		new void GetLocalName(nsAString aLocalName);
		new bool HasAttributes();

		// nsIDOMDocument:
		new nsIDOMDocumentType GetDoctype();
		new IntPtr GetImplementation();
		new nsIDOMElement GetDocumentElement();
		new nsIDOMElement CreateElement(nsAString tagName);
		new IntPtr CreateDocumentFragment();
		new IntPtr CreateTextNode(nsAString data);
		new IntPtr CreateComment(nsAString data);
		new IntPtr CreateCDATASection(nsAString data);
		new IntPtr CreateProcessingInstruction(nsAString target, nsAString data);
		new nsIDOMAttr CreateAttribute(nsAString name);
		new IntPtr CreateEntityReference(nsAString name);
		new nsIDOMNodeList GetElementsByTagName(nsAString tagname);
		new nsIDOMNode ImportNode(nsIDOMNode importedNode, bool deep);
		new nsIDOMElement CreateElementNS(nsAString namespaceURI, nsAString qualifiedName);
		new nsIDOMAttr CreateAttributeNS(nsAString namespaceURI, nsAString qualifiedName);
		new nsIDOMNodeList GetElementsByTagNameNS(nsAString namespaceURI, nsAString localName);
		new nsIDOMElement GetElementById(nsAString elementId);

		// nsIDOMHTMLDocument:
		void GetTitle(nsAUTF8String aTitle);
		void SetTitle(nsAUTF8String aTitle);
		void GetReferrer(nsAString aReferrer);
		void GetDomain(nsAString aDomain);
		void GetURL(nsAString aURL);
		nsIDOMHTMLElement GetBody();
		void SetBody(nsIDOMHTMLElement aBody);
		nsIDOMHTMLCollection GetImages();
		nsIDOMHTMLCollection GetApplets();
		nsIDOMHTMLCollection GetLinks();
		nsIDOMHTMLCollection GetForms();
		nsIDOMHTMLCollection GetAnchors();
		void GetCookie(nsAString aCookie);
		void SetCookie(nsAString aCookie);
		void Open();
		void Close();
		void Write(nsAString text);
		void Writeln(nsAString text);
		nsIDOMNodeList GetElementsByName(nsAString elementName);
	}

	[Guid("a6cf9077-15b3-11d2-932e-00805f8add32"), ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	interface nsIDOMDocumentType : nsIDOMNode
	{
		// nsIDOMNode:
		new void GetNodeName(nsAString aNodeName);
		new void GetNodeValue(nsAString aNodeValue);
		new void SetNodeValue(nsAString aNodeValue);
		new ushort GetNodeType();
		new nsIDOMNode GetParentNode();
		new nsIDOMNodeList GetChildNodes();
		new nsIDOMNode GetFirstChild();
		new nsIDOMNode GetLastChild();
		new nsIDOMNode GetPreviousSibling();
		new nsIDOMNode GetNextSibling();
		new nsIDOMNamedNodeMap GetAttributes();
		new nsIDOMDocument GetOwnerDocument();
		new nsIDOMNode InsertBefore(nsIDOMNode newChild, nsIDOMNode refChild);
		new nsIDOMNode ReplaceChild(nsIDOMNode newChild, nsIDOMNode oldChild);
		new nsIDOMNode RemoveChild(nsIDOMNode oldChild);
		new nsIDOMNode AppendChild(nsIDOMNode newChild);
		new bool HasChildNodes();
		new nsIDOMNode CloneNode(bool deep);
		new void Normalize();
		new bool IsSupported(nsAString feature, nsAString version);
		new void GetNamespaceURI(nsAString aNamespaceURI);
		new void GetPrefix(nsAString aPrefix);
		new void SetPrefix(nsAString aPrefix);
		new void GetLocalName(nsAString aLocalName);
		new bool HasAttributes();

		// nsIDOMDocumentType:
		void GetName(nsAString aName);
		nsIDOMNamedNodeMap GetEntities();
		nsIDOMNamedNodeMap GetNotations();
		void GetPublicId(nsAString aPublicId);
		void GetSystemId(nsAString aSystemId);
		void GetInternalSubset(nsAString aInternalSubset);
	}

	[Guid("a6cf9083-15b3-11d2-932e-00805f8add32"), ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	interface nsIDOMHTMLCollection
	{
		int GetLength();
		nsIDOMNode Item(int index);
		nsIDOMNode NamedItem(nsAString name);
	}

	#if GECKO_1_9_1
	[Guid("7F142F9A-FBA7-4949-93D6-CF08A974AC51"), ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	interface nsIDOMNSHTMLElement
	{
		int GetOffsetTop();
		int GetOffsetLeft();
		int GetOffsetWidth();
		int GetOffsetHeight();
		nsIDOMElement GetOffsetParent();
		void GetInnerHTML(nsAString aInnerHTML);
		void SetInnerHTML(nsAString aInnerHTML);
		int GetTabIndex();
		void SetTabIndex(int aTabIndex);
		void GetContentEditable(nsAString aContentEditable);
		void SetContentEditable(nsAString aContentEditable);
		void Blur();
		void Focus();
		void ScrollIntoView(bool top);
		bool GetSpellcheck();
		void SetSpellcheck(bool aSpellcheck);
	}
	#else
	#if GECKO_1_8
	[Guid("da83b2ec-8264-4410-8496-ada3acd2ae42"), ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	#elif GECKO_1_9
	[Guid("eac0a4ee-2e4f-403c-9b77-5cf32cfb42f7"), ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	#endif
	interface nsIDOMNSHTMLElement
	{
		int GetOffsetTop();
		int GetOffsetLeft();
		int GetOffsetWidth();
		int GetOffsetHeight();
		nsIDOMElement GetOffsetParent();
		void GetInnerHTML(nsAString aInnerHTML);
		void SetInnerHTML(nsAString aInnerHTML);
		int GetScrollTop();
		void SetScrollTop(int aScrollTop);
		int GetScrollLeft();
		void SetScrollLeft(int aScrollLeft);
		int GetScrollHeight();
		int GetScrollWidth();
		#if GECKO_1_8
		int GetClientHeight();
		int GetClientWidth();
		int GetTabIndex();
		void SetTabIndex(int aTabIndex);
		void Blur();
		void Focus();
		void ScrollIntoView(bool top);
		#elif GECKO_1_9
		int GetClientTop();
		int GetClientLeft();
		int GetClientHeight();
		int GetClientWidth();
		int GetTabIndex();
		void SetTabIndex(int aTabIndex);
		void GetContentEditable(nsAString aContentEditable);
		void SetContentEditable(nsAString aContentEditable);
		void Blur();
		void Focus();
		void ScrollIntoView(bool top);
		bool GetSpellcheck();
		void SetSpellcheck(bool aSpellcheck);
		#endif
	}
	#endif

	[Guid("3d9f4973-dd2e-48f5-b5f7-2634e09eadd9"), ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	interface nsIDOMDocumentStyle
	{
		nsIDOMStyleSheetList GetStyleSheets();
	}

	[Guid("a6cf9081-15b3-11d2-932e-00805f8add32"), ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	interface nsIDOMStyleSheetList
	{
		int GetLength();
		nsIDOMStyleSheet Item(int index);
	}

	[Guid("a6cf9080-15b3-11d2-932e-00805f8add32"), ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	interface nsIDOMStyleSheet
	{
		void GetType(nsAString aType);
		bool GetDisabled();
		void SetDisabled(bool aDisabled);
		nsIDOMNode GetOwnerNode();
		nsIDOMStyleSheet GetParentStyleSheet();
		void GetHref(nsAString aHref);
		void GetTitle(nsAString aTitle);
		nsIDOMMediaList GetMedia();
	}

	[Guid("9b0c2ed7-111c-4824-adf9-ef0da6dad371"), ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	interface nsIDOMMediaList
	{
		void GetMediaText(nsAString aMediaText);
		void SetMediaText(nsAString aMediaText);
		int GetLength();
		void Item(int index, nsAString _retval);
		void DeleteMedium(nsAString oldMedium);
		void AppendMedium(nsAString newMedium);
	}

	[Guid("a6cf90c2-15b3-11d2-932e-00805f8add32"), ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	interface nsIDOMCSSStyleSheet
	{
		// nsIDOMStyleSheet:
		void GetType(nsAString aType);
		bool GetDisabled();
		void SetDisabled(bool aDisabled);
		nsIDOMNode GetOwnerNode();
		nsIDOMStyleSheet GetParentStyleSheet();
		void GetHref(nsAString aHref);
		void GetTitle(nsAString aTitle);
		nsIDOMMediaList GetMedia();

		// nsIDOMCSSStyleSheet:
		nsIDOMCSSRule GetOwnerRule();
		[PreserveSig] int GetCssRules(out nsIDOMCSSRuleList ret); // 0x8053000F
		[PreserveSig] int InsertRule(nsAString rule, int index, out int result);
		void DeleteRule(int index);
	}

	[Guid("a6cf90c1-15b3-11d2-932e-00805f8add32"), ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	interface nsIDOMCSSRule
	{
		ushort GetType();
		void GetCssText(nsAString aCssText);
		void SetCssText(nsAString aCssText);
		nsIDOMCSSStyleSheet GetParentStyleSheet();
		nsIDOMCSSRule GetParentRule();
	}

	[Guid("a6cf90bf-15b3-11d2-932e-00805f8add32"), ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	interface nsIDOMCSSStyleRule : nsIDOMCSSRule
	{
		// nsIDOMCSSRule:
		new ushort GetType();
		new void GetCssText(nsAString aCssText);
		new void SetCssText(nsAString aCssText);
		new nsIDOMCSSStyleSheet GetParentStyleSheet();
		new nsIDOMCSSRule GetParentRule();

		// nsIDOMCSSStyleRule:
		void GetSelectorText(nsAString aSelectorText);
		void SetSelectorText(nsAString aSelectorText);
		nsIDOMCSSStyleDeclaration GetStyle();
	}

	[Guid("a6cf90cf-15b3-11d2-932e-00805f8add32"), ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	interface nsIDOMCSSImportRule
	{
		// nsIDOMCSSRule:
		ushort GetType();
		void GetCssText(nsAString aCssText);
		void SetCssText(nsAString aCssText);
		nsIDOMCSSStyleSheet GetParentStyleSheet();
		nsIDOMCSSRule GetParentRule();

		// nsIDOMCSSImportRule:
		void GetHref(nsAString str);
		nsIDOMMediaList GetMedia();
		nsIDOMCSSStyleSheet GetStyleSheet();
	}

	[Guid("a6cf90c0-15b3-11d2-932e-00805f8add32"), ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	interface nsIDOMCSSRuleList
	{
		int GetLength();
		nsIDOMCSSRule Item(int index);
	}

	[Guid("a6cf90be-15b3-11d2-932e-00805f8add32"), ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	interface nsIDOMCSSStyleDeclaration
	{
		void GetCssText(nsAString aCssText);
		void SetCssText(nsAString aCssText);
		void GetPropertyValue(nsAString propertyName, nsAString _retval);
		nsIDOMCSSValue GetPropertyCSSValue(nsAString propertyName);
		void RemoveProperty(nsAString propertyName, nsAString _retval);
		void GetPropertyPriority(nsAString propertyName, nsAString _retval);
		void SetProperty(nsAString propertyName, nsAString value, nsAString priority);
		uint GetLength();
		void Item(int index, nsAString _retval);
		nsIDOMCSSRule GetParentRule();
	}

	[Guid("009f7ea5-9e80-41be-b008-db62f10823f2"), ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	interface nsIDOMCSSValue
	{
		void GetCssText(nsAString aCssText);
		void SetCssText(nsAString aCssText);
		ushort GetCssValueType();
	}

	class PRUnicharMarshaler : ICustomMarshaler
	{
		public static ICustomMarshaler GetInstance(string cookie)
		{
			return (_Instance == null) ? (_Instance = new PRUnicharMarshaler()) : _Instance;
		}
		static PRUnicharMarshaler _Instance;

		public void CleanUpManagedData(object ManagedObj) { }
		public void CleanUpNativeData(IntPtr pNativeData) { }
		public int GetNativeDataSize() { return 0; }

		public IntPtr MarshalManagedToNative(object ManagedObj)
		{
			byte [] bytes = Encoding.Unicode.GetBytes(ManagedObj.ToString() + "\0");
			IntPtr alloc = Xpcom.Alloc(bytes.Length + 2);
			Marshal.Copy(bytes, 0, alloc, bytes.Length);
			return alloc;
		}

		public object MarshalNativeToManaged(IntPtr pNativeData)
		{
			return Marshal.PtrToStringUni(pNativeData);
		}
	}

	#if !NO_CUSTOM_PROMPT_SERVICE
	[Guid("1630c61a-325e-49ca-8759-a31b16c47aa5"), ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	interface nsIPromptService
	{
		void Alert(nsIDOMWindow aParent, [MarshalAs(UnmanagedType.LPWStr)] string aDialogTitle, [MarshalAs(UnmanagedType.LPWStr)] string aText);
		[PreserveSig] void AlertCheck(nsIDOMWindow aParent, [MarshalAs(UnmanagedType.LPWStr)] string aDialogTitle, [MarshalAs(UnmanagedType.LPWStr)] string aText, [MarshalAs(UnmanagedType.LPWStr)] string aCheckMsg, ref bool aCheckState);
		bool Confirm(nsIDOMWindow aParent, [MarshalAs(UnmanagedType.LPWStr)] string aDialogTitle, [MarshalAs(UnmanagedType.LPWStr)] string aText);
		[PreserveSig] bool ConfirmCheck(nsIDOMWindow aParent, [MarshalAs(UnmanagedType.LPWStr)] string aDialogTitle, [MarshalAs(UnmanagedType.LPWStr)] string aText, [MarshalAs(UnmanagedType.LPWStr)] string aCheckMsg, ref bool aCheckState);
		int ConfirmEx(nsIDOMWindow aParent, [MarshalAs(UnmanagedType.LPWStr)] string aDialogTitle, [MarshalAs(UnmanagedType.LPWStr)] string aText, uint aButtonFlags, [MarshalAs(UnmanagedType.LPWStr)] string aButton0Title, [MarshalAs(UnmanagedType.LPWStr)] string aButton1Title, [MarshalAs(UnmanagedType.LPWStr)] string aButton2Title, [MarshalAs(UnmanagedType.LPWStr)] string aCheckMsg, out bool aCheckState);
		bool Prompt(nsIDOMWindow aParent, [MarshalAs(UnmanagedType.LPWStr)] string aDialogTitle, [MarshalAs(UnmanagedType.LPWStr)] string aText, [MarshalAs(UnmanagedType.CustomMarshaler, MarshalTypeRef=typeof(PRUnicharMarshaler))] ref string aValue, [MarshalAs(UnmanagedType.LPWStr)] string aCheckMsg, IntPtr aCheckState);
		//[PreserveSig] int Prompt(IntPtr aParent, IntPtr aDialogTitle, IntPtr aText, IntPtr aValue, IntPtr aCheckMsg, IntPtr aCheckState, out bool result);
		bool PromptUsernameAndPassword(nsIDOMWindow aParent, [MarshalAs(UnmanagedType.LPWStr)] string aDialogTitle, [MarshalAs(UnmanagedType.LPWStr)] string aText, [MarshalAs(UnmanagedType.CustomMarshaler, MarshalTypeRef=typeof(PRUnicharMarshaler))] ref string aUsername, [MarshalAs(UnmanagedType.CustomMarshaler, MarshalTypeRef=typeof(PRUnicharMarshaler))] ref string aPassword, [MarshalAs(UnmanagedType.LPWStr)] string aCheckMsg, IntPtr aCheckState);
		bool PromptPassword(nsIDOMWindow aParent, [MarshalAs(UnmanagedType.LPWStr)] string aDialogTitle, [MarshalAs(UnmanagedType.LPWStr)] string aText, [MarshalAs(UnmanagedType.CustomMarshaler, MarshalTypeRef=typeof(PRUnicharMarshaler))] ref string aPassword, [MarshalAs(UnmanagedType.LPWStr)] string aCheckMsg, IntPtr aCheckState);
		bool Select(nsIDOMWindow aParent, [MarshalAs(UnmanagedType.LPWStr)] string aDialogTitle, [MarshalAs(UnmanagedType.LPWStr)] string aText, uint aCount, IntPtr aSelectList, out int aOutSelection);
	}

	static class nsIPromptServiceConstants
	{
		public const int BUTTON_POS_0 = 1;
		public const int BUTTON_POS_1 = 256;
		public const int BUTTON_POS_2 = 65536;

		public const int BUTTON_TITLE_OK = 1;
		public const int BUTTON_TITLE_CANCEL = 2;
		public const int BUTTON_TITLE_YES = 3;
		public const int BUTTON_TITLE_NO = 4;
		public const int BUTTON_TITLE_SAVE = 5;
		public const int BUTTON_TITLE_DONT_SAVE = 6;
		public const int BUTTON_TITLE_REVERT = 7;
		public const int BUTTON_TITLE_IS_STRING = 127;

		public const int BUTTON_POS_0_DEFAULT = 0;
		public const int BUTTON_POS_1_DEFAULT = 16777216;
		public const int BUTTON_POS_2_DEFAULT = 33554432;

		public const int BUTTON_DELAY_ENABLE = 67108864;
		public const int STD_OK_CANCEL_BUTTONS = 513;
		public const int STD_YES_NO_BUTTONS = 1027;
	}

	[Guid("e800ef97-ae37-46b7-a46c-31fbe79657ea"), ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	interface nsINonBlockingAlertService
	{
		void ShowNonBlockingAlert(nsIDOMWindow aParent, [MarshalAs(UnmanagedType.LPWStr)] string aDialogTitle, [MarshalAs(UnmanagedType.LPWStr)] string aText);
	}

	#if GECKO_1_9
	[Guid("cf86d196-dbee-4482-9dfa-3477aa128319"), ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	interface nsIPromptService2 : nsIPromptService
	{
		// nsIPromptService:
		new void Alert(nsIDOMWindow aParent, [MarshalAs(UnmanagedType.LPWStr)] string aDialogTitle, [MarshalAs(UnmanagedType.LPWStr)] string aText);
		[PreserveSig] new void AlertCheck(nsIDOMWindow aParent, [MarshalAs(UnmanagedType.LPWStr)] string aDialogTitle, [MarshalAs(UnmanagedType.LPWStr)] string aText, [MarshalAs(UnmanagedType.LPWStr)] string aCheckMsg, ref bool aCheckState);
		new bool Confirm(nsIDOMWindow aParent, [MarshalAs(UnmanagedType.LPWStr)] string aDialogTitle, [MarshalAs(UnmanagedType.LPWStr)] string aText);
		[PreserveSig] new bool ConfirmCheck(nsIDOMWindow aParent, [MarshalAs(UnmanagedType.LPWStr)] string aDialogTitle, [MarshalAs(UnmanagedType.LPWStr)] string aText, [MarshalAs(UnmanagedType.LPWStr)] string aCheckMsg, ref bool aCheckState);
		new int ConfirmEx(nsIDOMWindow aParent, [MarshalAs(UnmanagedType.LPWStr)] string aDialogTitle, [MarshalAs(UnmanagedType.LPWStr)] string aText, uint aButtonFlags, [MarshalAs(UnmanagedType.LPWStr)] string aButton0Title, [MarshalAs(UnmanagedType.LPWStr)] string aButton1Title, [MarshalAs(UnmanagedType.LPWStr)] string aButton2Title, [MarshalAs(UnmanagedType.LPWStr)] string aCheckMsg, out bool aCheckState);
		new bool Prompt(nsIDOMWindow aParent, [MarshalAs(UnmanagedType.LPWStr)] string aDialogTitle, [MarshalAs(UnmanagedType.LPWStr)] string aText, [MarshalAs(UnmanagedType.CustomMarshaler, MarshalTypeRef=typeof(PRUnicharMarshaler))] ref string aValue, [MarshalAs(UnmanagedType.LPWStr)] string aCheckMsg, IntPtr aCheckState);
		new bool PromptUsernameAndPassword(nsIDOMWindow aParent, [MarshalAs(UnmanagedType.LPWStr)] string aDialogTitle, [MarshalAs(UnmanagedType.LPWStr)] string aText, [MarshalAs(UnmanagedType.CustomMarshaler, MarshalTypeRef=typeof(PRUnicharMarshaler))] ref string aUsername, [MarshalAs(UnmanagedType.CustomMarshaler, MarshalTypeRef=typeof(PRUnicharMarshaler))] ref string aPassword, [MarshalAs(UnmanagedType.LPWStr)] string aCheckMsg, IntPtr aCheckState);
		new bool PromptPassword(nsIDOMWindow aParent, [MarshalAs(UnmanagedType.LPWStr)] string aDialogTitle, [MarshalAs(UnmanagedType.LPWStr)] string aText, [MarshalAs(UnmanagedType.CustomMarshaler, MarshalTypeRef=typeof(PRUnicharMarshaler))] ref string aPassword, [MarshalAs(UnmanagedType.LPWStr)] string aCheckMsg, IntPtr aCheckState);
		new bool Select(nsIDOMWindow aParent, [MarshalAs(UnmanagedType.LPWStr)] string aDialogTitle, [MarshalAs(UnmanagedType.LPWStr)] string aText, uint aCount, IntPtr aSelectList, out int aOutSelection);

		// nsIPromptService2:
		bool PromptAuth(nsIDOMWindow aParent, IntPtr aChannel, int level, nsIAuthInformation authInfo, [MarshalAs(UnmanagedType.LPWStr)] string checkboxLabel, IntPtr checkValue);
		nsICancelable AsyncPromptAuth(nsIDOMWindow aParent, IntPtr aChannel, nsIAuthPromptCallback aCallback, nsISupports aContext, uint level, nsIAuthInformation authInfo, [MarshalAs(UnmanagedType.LPWStr)] string checkboxLabel, IntPtr checkValue);
	}

	[Guid("0d73639c-2a92-4518-9f92-28f71fea5f20"), ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	interface nsIAuthInformation
	{
		uint GetFlags();
		void GetRealm(nsAString aRealm);
		void GetAuthenticationScheme(nsACString aAuthenticationScheme);
		void GetUsername(nsAString aUsername);
		void SetUsername(nsAString aUsername);
		void GetPassword(nsAString aPassword);
		void SetPassword(nsAString aPassword);
		void GetDomain(nsAString aDomain);
		void SetDomain(nsAString aDomain);
	}

	[Guid("bdc387d7-2d29-4cac-92f1-dd75d786631d"), ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	interface nsIAuthPromptCallback
	{
		void OnAuthAvailable(nsISupports aContext, nsIAuthInformation aAuthInfo);
		void OnAuthCancelled(nsISupports aContext, bool userCancel);
	}

	[Guid("d94ac0a0-bb18-46b8-844e-84159064b0bd"), ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	interface nsICancelable
	{
		void Cancel(int aReason);
	}
	#endif

	#endif

	[Guid("decb9cc7-c08f-4ea5-be91-a8fc637ce2d2"), ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	interface nsIPrefService
	{
		void ReadUserPrefs(nsIFile aFile);
		void ResetPrefs();
		void ResetUserPrefs();
		void SavePrefFile(nsIFile aFile);
		nsIPrefBranch GetBranch([MarshalAs(UnmanagedType.LPStr)] string aPrefRoot);
		nsIPrefBranch GetDefaultBranch([MarshalAs(UnmanagedType.LPStr)] string aPrefRoot);
	}

	[Guid("56c35506-f14b-11d3-99d3-ddbfac2ccf65"), ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	interface nsIPrefBranch
	{
		[return: MarshalAs(UnmanagedType.LPStr)] string GetRoot();
		int GetPrefType([MarshalAs(UnmanagedType.LPStr)] string aPrefName);
		bool GetBoolPref([MarshalAs(UnmanagedType.LPStr)] string aPrefName);
		void SetBoolPref([MarshalAs(UnmanagedType.LPStr)] string aPrefName, int aValue);
		[return: MarshalAs(UnmanagedType.LPStr)] string GetCharPref([MarshalAs(UnmanagedType.LPStr)] string aPrefName);
		void SetCharPref([MarshalAs(UnmanagedType.LPStr)] string aPrefName, [MarshalAs(UnmanagedType.LPStr)] string aValue);
		int GetIntPref([MarshalAs(UnmanagedType.LPStr)] string aPrefName);
		void SetIntPref([MarshalAs(UnmanagedType.LPStr)] string aPrefName, int aValue);
		void GetComplexValue([MarshalAs(UnmanagedType.LPStr)] string aPrefName, ref Guid aType, IntPtr aValue);
		void SetComplexValue([MarshalAs(UnmanagedType.LPStr)] string aPrefName, ref Guid aType, nsISupports aValue);
		void ClearUserPref([MarshalAs(UnmanagedType.LPStr)] string aPrefName);
		void LockPref([MarshalAs(UnmanagedType.LPStr)] string aPrefName);
		bool PrefHasUserValue([MarshalAs(UnmanagedType.LPStr)] string aPrefName);
		bool PrefIsLocked([MarshalAs(UnmanagedType.LPStr)] string aPrefName);
		void UnlockPref([MarshalAs(UnmanagedType.LPStr)] string aPrefName);
		void DeleteBranch([MarshalAs(UnmanagedType.LPStr)] string aStartingAt);
		void GetChildList([MarshalAs(UnmanagedType.LPStr)] string aStartingAt, out uint aCount, [MarshalAs(UnmanagedType.LPArray, ArraySubType=UnmanagedType.LPStr)] string [] aChildArray);
		void ResetBranch([MarshalAs(UnmanagedType.LPStr)] string aStartingAt);
	}

	[Guid("00000001-0000-0000-c000-000000000046"), ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	public interface nsIFactory
	{
		void CreateInstance(nsISupports aOuter, ref Guid iid, [MarshalAs(UnmanagedType.IUnknown)] out object result);
		void LockFactory(bool @lock);
	}

	[Guid("2417cbfe-65ad-48a6-b4b6-eb84db174392"), ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	interface nsIComponentRegistrar
	{
		void AutoRegister(IntPtr aSpec); // nsIFile
		void AutoUnregister(IntPtr aSpec); // nsIFile
		void RegisterFactory(ref Guid aClass, [MarshalAs(UnmanagedType.LPStr)] string aClassName, [MarshalAs(UnmanagedType.LPStr)] string aContractID, nsIFactory aFactory);
		void UnregisterFactory(ref Guid aClass, nsIFactory aFactory);
		void RegisterFactoryLocation(ref Guid aClass, [MarshalAs(UnmanagedType.LPStr)] string aClassName, [MarshalAs(UnmanagedType.LPStr)] string aContractID, IntPtr aFile, [MarshalAs(UnmanagedType.LPStr)] string aLoaderStr, [MarshalAs(UnmanagedType.LPStr)] string aType);
		void UnregisterFactoryLocation(ref Guid aClass, IntPtr aFile);
		bool IsCIDRegistered(ref Guid aClass);
		bool IsContractIDRegistered([MarshalAs(UnmanagedType.LPStr)] string aContractID);
		IntPtr EnumerateCIDs(); // nsISimpleEnumerator
		IntPtr EnumerateContractIDs(); // nsISimpleEnumerator
		[return: MarshalAs(UnmanagedType.LPStr)] string CIDToContractID(ref Guid aClass);
		[PreserveSig] void ContractIDToCID([MarshalAs(UnmanagedType.LPStr)] string aContractID, out Guid cid);
	}

	[Guid("57a66a60-d43a-11d3-8cc2-00609792278c"), ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	interface nsIDirectoryService
	{
		void Init();
		void RegisterProvider(nsIDirectoryServiceProvider provider);
		void UnregisterProvider(nsIDirectoryServiceProvider provider);
	}

	[Guid("bbf8cab0-d43a-11d3-8cc2-00609792278c"), ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	interface nsIDirectoryServiceProvider
	{
		nsIFile GetFile([MarshalAs(UnmanagedType.LPStr)] string prop, out bool persistent);
	}

	[Guid("78650582-4e93-4b60-8e85-26ebd3eb14ca"), ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	interface nsIProperties
	{
		IntPtr Get([MarshalAs(UnmanagedType.LPStr)] string prop, ref Guid iid);
		void Set([MarshalAs(UnmanagedType.LPStr)] string prop, nsISupports value);
		bool Has([MarshalAs(UnmanagedType.LPStr)] string prop);
		void Undefine([MarshalAs(UnmanagedType.LPStr)] string prop);
		void STUB_GetKeys();
	}

	[Guid("c8c0a080-0868-11d3-915f-d9d889d48e3c"), ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	public interface nsIFile
	{
		void Append(nsAString node);
		void AppendNative(nsACString node);
		void Normalize();
		void Create(uint type, uint permissions);
		void GetLeafName(nsAString aLeafName);
		void SetLeafName(nsAString aLeafName);
		void GetNativeLeafName(nsACString aNativeLeafName);
		void SetNativeLeafName(nsACString aNativeLeafName);
		void CopyTo(nsIFile newParentDir, nsAString newName);
		void CopyToNative(nsIFile newParentDir, nsACString newName);
		void CopyToFollowingLinks(nsIFile newParentDir, nsAString newName);
		void CopyToFollowingLinksNative(nsIFile newParentDir, nsACString newName);
		void MoveTo(nsIFile newParentDir, nsAString newName);
		void MoveToNative(nsIFile newParentDir, nsACString newName);
		void Remove(bool recursive);
		uint GetPermissions();
		void SetPermissions(uint aPermissions);
		uint GetPermissionsOfLink();
		void SetPermissionsOfLink(uint aPermissionsOfLink);
		long GetLastModifiedTime();
		void SetLastModifiedTime(long aLastModifiedTime);
		long GetLastModifiedTimeOfLink();
		void SetLastModifiedTimeOfLink(long aLastModifiedTimeOfLink);
		long GetFileSize();
		void SetFileSize(long aFileSize);
		long GetFileSizeOfLink();
		void GetTarget(nsAString aTarget);
		void GetNativeTarget(nsACString aNativeTarget);
		void GetPath(nsAString aPath);
		void GetNativePath(nsACString aNativePath);
		bool Exists();
		bool IsWritable();
		bool IsReadable();
		bool IsExecutable();
		bool IsHidden();
		bool IsDirectory();
		bool IsFile();
		bool IsSymlink();
		bool IsSpecial();
		void CreateUnique(uint type, uint permissions);
		nsIFile Clone();
		bool Equals(nsIFile inFile);
		bool Contains(nsIFile inFile, bool recur);
		nsIFile GetParent();
		nsISimpleEnumerator GetDirectoryEntries();
	}

	[Guid("aa610f20-a889-11d3-8c81-000064657374"), ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	interface nsILocalFile : nsIFile
	{
		// nsIFile:
		new void Append(nsAString node);
		new void AppendNative(nsACString node);
		new void Normalize();
		new void Create(uint type, uint permissions);
		new void GetLeafName(nsAString aLeafName);
		new void SetLeafName(nsAString aLeafName);
		new void GetNativeLeafName(nsACString aNativeLeafName);
		new void SetNativeLeafName(nsACString aNativeLeafName);
		new void CopyTo(nsIFile newParentDir, nsAString newName);
		new void CopyToNative(nsIFile newParentDir, nsACString newName);
		new void CopyToFollowingLinks(nsIFile newParentDir, nsAString newName);
		new void CopyToFollowingLinksNative(nsIFile newParentDir, nsACString newName);
		new void MoveTo(nsIFile newParentDir, nsAString newName);
		new void MoveToNative(nsIFile newParentDir, nsACString newName);
		new void Remove(bool recursive);
		new uint GetPermissions();
		new void SetPermissions(uint aPermissions);
		new uint GetPermissionsOfLink();
		new void SetPermissionsOfLink(uint aPermissionsOfLink);
		new long GetLastModifiedTime();
		new void SetLastModifiedTime(long aLastModifiedTime);
		new long GetLastModifiedTimeOfLink();
		new void SetLastModifiedTimeOfLink(long aLastModifiedTimeOfLink);
		new long GetFileSize();
		new void SetFileSize(long aFileSize);
		new long GetFileSizeOfLink();
		new void GetTarget(nsAString aTarget);
		new void GetNativeTarget(nsACString aNativeTarget);
		new void GetPath(nsAString aPath);
		new void GetNativePath(nsACString aNativePath);
		new bool Exists();
		new bool IsWritable();
		new bool IsReadable();
		new bool IsExecutable();
		new bool IsHidden();
		new bool IsDirectory();
		new bool IsFile();
		new bool IsSymlink();
		new bool IsSpecial();
		new void CreateUnique(uint type, uint permissions);
		new nsIFile Clone();
		new bool Equals(nsIFile inFile);
		new bool Contains(nsIFile inFile, bool recur);
		new nsIFile GetParent();
		new nsISimpleEnumerator GetDirectoryEntries();

		// nsILocalFile:
		void InitWithPath(nsAString filePath);
		void InitWithNativePath(nsACString filePath);
		void InitWithFile(nsILocalFile aFile);
		bool GetFollowLinks();
		void SetFollowLinks(bool aFollowLinks);
		//PRFileDesc OpenNSPRFileDesc(int flags, int mode);
		//FILE OpenANSIFileDesc([MarshalAs(UnmanagedType.LPStr)] string mode);
		//PRLibrary Load();
		IntPtr OpenNSPRFileDesc(int flags, int mode);
		IntPtr OpenANSIFileDesc([MarshalAs(UnmanagedType.LPStr)] string mode);
		IntPtr Load();
		long GetDiskSpaceAvailable();
		void AppendRelativePath(nsAString relativeFilePath);
		void AppendRelativeNativePath(nsACString relativeFilePath);
		void GetPersistentDescriptor(nsACString aPersistentDescriptor);
		void SetPersistentDescriptor(nsACString aPersistentDescriptor);
		void Reveal();
		void Launch();
		void GetRelativeDescriptor(nsILocalFile fromFile, nsACString _retval);
		void SetRelativeDescriptor(nsILocalFile fromFile, nsACString relativeDesc);
	}

	[Guid("bddeda3f-9020-4d12-8c70-984ee9f7935e"), ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	public interface nsIIOService
	{
		IntPtr GetProtocolHandler([MarshalAs(UnmanagedType.LPStr)] string aScheme); // nsIProtocolHandler
		uint GetProtocolFlags([MarshalAs(UnmanagedType.LPStr)] string aScheme);
		nsIURI NewURI(nsACString aSpec, [MarshalAs(UnmanagedType.LPStr)] string aOriginCharset, nsIURI aBaseURI);
		nsIURI NewFileURI(nsIFile aFile);
		IntPtr NewChannelFromURI(nsIURI aURI); // returns: nsIChannel
		IntPtr NewChannel(nsACString aSpec, [MarshalAs(UnmanagedType.LPStr)] string aOriginCharset, nsIURI aBaseURI); // returns: nsIChannel
		bool GetOffline();
		void SetOffline(bool aOffline);
		bool AllowPort(int aPort, [MarshalAs(UnmanagedType.LPStr)] string aScheme);
		void ExtractScheme(nsACString urlString, nsACString _retval);
	}

	[Guid("b8100c90-73be-11d2-92a5-00105a1b0d64"), ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	interface nsIClipboardCommands
	{
		[PreserveSig] int CanCutSelection(out bool result);
		[PreserveSig] int CanCopySelection(out bool result);
		[PreserveSig] int CanCopyLinkLocation(out bool result);
		[PreserveSig] int CanCopyImageLocation(out bool result);
		[PreserveSig] int CanCopyImageContents(out bool result);
		[PreserveSig] int CanPaste(out bool result);
		void CutSelection();
		void CopySelection();
		void CopyLinkLocation();
		void CopyImageLocation();
		void CopyImageContents();
		void Paste();
		void SelectAll();
		void SelectNone();
	}

	[Guid("080d2001-f91e-11d4-a73c-f9242928207c"), ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	interface nsICommandManager
	{
		void AddCommandObserver(nsIObserver aCommandObserver, [MarshalAs(UnmanagedType.LPStr)] string aCommandToObserve);
		void RemoveCommandObserver(nsIObserver aCommandObserver, [MarshalAs(UnmanagedType.LPStr)] string aCommandObserved);
		bool IsCommandSupported([MarshalAs(UnmanagedType.LPStr)] string aCommandName, nsIDOMWindow aTargetWindow);
		bool IsCommandEnabled([MarshalAs(UnmanagedType.LPStr)] string aCommandName, nsIDOMWindow aTargetWindow);
		void GetCommandState([MarshalAs(UnmanagedType.LPStr)] string aCommandName, nsIDOMWindow aTargetWindow, nsICommandParams aCommandParams);
		void DoCommand([MarshalAs(UnmanagedType.LPStr)] string aCommandName, nsICommandParams aCommandParams, nsIDOMWindow aTargetWindow);
	}

	[Guid("83f892cf-7ed3-490e-967a-62640f3158e1"), ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	interface nsICommandParams
	{
		short GetValueType([MarshalAs(UnmanagedType.LPStr)] string name);
		bool GetBooleanValue([MarshalAs(UnmanagedType.LPStr)] string name);
		int GetLongValue([MarshalAs(UnmanagedType.LPStr)] string name);
		double GetDoubleValue([MarshalAs(UnmanagedType.LPStr)] string name);
		void GetStringValue([MarshalAs(UnmanagedType.LPStr)] string name, nsAString _retval);
		[return: MarshalAs(UnmanagedType.LPStr)] string GetCStringValue([MarshalAs(UnmanagedType.LPStr)] string name);
		nsISupports GetISupportsValue([MarshalAs(UnmanagedType.LPStr)] string name);
		void SetBooleanValue([MarshalAs(UnmanagedType.LPStr)] string name, bool value);
		void SetLongValue([MarshalAs(UnmanagedType.LPStr)] string name, int value);
		void SetDoubleValue([MarshalAs(UnmanagedType.LPStr)] string name, double value);
		void SetStringValue([MarshalAs(UnmanagedType.LPStr)] string name, nsAString value);
		void SetCStringValue([MarshalAs(UnmanagedType.LPStr)] string name, [MarshalAs(UnmanagedType.LPStr)] string value);
		void SetISupportsValue([MarshalAs(UnmanagedType.LPStr)] string name, nsISupports value);
		void RemoveValue([MarshalAs(UnmanagedType.LPStr)] string name);
		bool HasMoreElements();
		void First();
		[return: MarshalAs(UnmanagedType.LPStr)] string GetNext();
	}

	[Guid("44b78386-1dd2-11b2-9ad2-e4eee2ca1916"), ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	interface nsITooltipListener
	{
		void OnShowTooltip(int aXCoords, int aYCoords, [MarshalAs(UnmanagedType.LPWStr)] string aTipText);
		void OnHideTooltip();
	}

	[Guid("fa9c7f6c-61b3-11d4-9877-00c04fa0cf4a"), ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	public interface nsIInputStream
	{
		void Close();
		int Available();
		int Read(IntPtr aBuf, uint aCount);
		int ReadSegments(IntPtr aWriter, IntPtr aClosure, uint aCount);
		bool IsNonBlocking();
	}

	delegate int nsWriteSegmentFun(nsIInputStream aInStream, IntPtr aClosure, IntPtr aFromSegment, int aToOffset, int aCount, out int aWriteCount);

	//[Guid("b128a1e6-44f3-4331-8fbe-5af360ff21ee"), ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	//interface nsITooltipTextProvider
	//{
	//      bool GetNodeText(nsIDOMNode aNode, [MarshalAs(UnmanagedType.LPWStr)] out string aText);
	//}

	#if GECKO_1_9
	[Guid("533a8131-8d0c-4ebf-990b-7fad7cd514ee"), ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	interface nsIDOMNSDocument
	{
		void GetCharacterSet(nsAString aCharacterSet);
		void GetDir(nsAString aDir);
		void SetDir(nsAString aDir);
		IntPtr GetLocation();
		void GetTitle(nsAString aTitle);
		void SetTitle(nsAString aTitle);
		void GetContentType(nsAString aContentType);
		void GetLastModified(nsAString aLastModified);
		void GetReferrer(nsAString aReferrer);
		IntPtr GetBoxObjectFor(nsIDOMElement elt);
		bool HasFocus();
		nsIDOMElement GetActiveElement();
		nsIDOMNodeList GetElementsByClassName(nsAString classes);
		nsIDOMElement ElementFromPoint(int x, int y);
	}

	#if GECKO_1_9_1
	[Guid("f0aef489-18c5-4de6-99d5-58b3758b098c"), ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	#else
	[Guid("cea6f919-7fe6-4bdd-9db6-158d9283f8d3"), ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	#endif
	interface nsIDOMNSElement
	{
		nsIDOMNodeList GetElementsByClassName(nsAString classes);
		nsIDOMClientRectList GetClientRects();
		nsIDOMClientRect GetBoundingClientRect();
		#if GECKO_1_9_1
		int GetScrollTop();
		void SetScrollTop(int value);
		int GetScrollLeft();
		void SetScrollLeft(int value);
		int GetScrollHeight();
		int GetScrollWidth();
		int GetClientTop();
		int GetClientLeft();
		int GetClientHeight();
		int GetClientWidth();
		nsIDOMElement GetFirstElementChild();
		nsIDOMElement GetLastElementChild();
		nsIDOMElement GetPreviousElementSibling();
		nsIDOMElement GetNextElementSibling();
		int GetChildElementCount();
		nsIDOMNodeList GetChildren();
		#endif
	}

	#if GECKO_1_9_1
	[Guid("B2F824C4-D9D3-499B-8D3B-45C8245497C6"), ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	#else
	[Guid("f8583bbc-c6de-4646-b39f-df7e766442e9"), ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	#endif
	interface nsIDOMClientRect
	{
		float GetLeft();
		float GetTop();
		float GetRight();
		float GetBottom();
		#if GECKO_1_9_1
		float GetWidth();
		float GetHeight();
		#endif
	}

	[Guid("917da19d-62f5-441d-b47e-9e35f05639c9"), ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	interface nsIDOMClientRectList
	{
		int GetLength();
		nsIDOMClientRect Item(int index);
	}
	#endif

	[Guid("75d1553d-63bf-4b5d-a8f7-e4e4cac21ba4"), ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	interface nsIPrintingPromptService
	{
		void ShowPrintDialog(nsIDOMWindow parent, nsIWebBrowserPrint webBrowserPrint, nsIPrintSettings printSettings);
		void ShowProgress(nsIDOMWindow parent, nsIWebBrowserPrint webBrowserPrint, nsIPrintSettings printSettings, nsIObserver openDialogObserver, bool isForPrinting, out nsIWebProgressListener webProgressListener, out nsIPrintProgressParams printProgressParams, bool notifyOnOpen);
		void ShowPageSetup(nsIDOMWindow parent, nsIPrintSettings printSettings, nsIObserver aObs);
		void ShowPrinterProperties(nsIDOMWindow parent, [MarshalAs(UnmanagedType.LPWStr)] string printerName, nsIPrintSettings printSettings);
	}

	[Guid("ca89b55b-6faf-4051-9645-1c03ef5108f8"), ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	interface nsIPrintProgressParams
	{
		[return: MarshalAs(UnmanagedType.LPWStr)] string GetDocTitle();
		void SetDocTitle([MarshalAs(UnmanagedType.LPWStr)] string aDocTitle);
		[return: MarshalAs(UnmanagedType.LPWStr)] string GetDocURL();
		void SetDocURL([MarshalAs(UnmanagedType.LPWStr)] string aDocURL);

	}

	[Guid("9a7ca4b0-fbba-11d4-a869-00105a183419"), ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	interface nsIWebBrowserPrint
	{
		nsIPrintSettings GetGlobalPrintSettings();
		nsIPrintSettings GetCurrentPrintSettings();
		nsIDOMWindow GetCurrentChildDOMWindow();
		bool GetDoingPrint();
		bool GetDoingPrintPreview();
		bool GetIsFramesetDocument();
		bool GetIsFramesetFrameSelected();
		bool GetIsIFrameSelected();
		bool GetIsRangeSelection();
		int GetPrintPreviewNumPages();
		void Print(nsIPrintSettings aThePrintSettings, nsIWebProgressListener aWPListener);
		void PrintPreview(nsIPrintSettings aThePrintSettings, nsIDOMWindow aChildDOMWin, nsIWebProgressListener aWPListener);
		void PrintPreviewNavigate(short aNavType, int aPageNum);
		void Cancel();
		void EnumerateDocumentNames(out uint aCount, IntPtr aResult);
		void ExitPrintPreview();
	}

	[Guid("2f977d52-5485-11d4-87e2-0010a4e75ef2"), ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	public interface nsIPrintSession
	{
	}

	[Guid("5af07661-6477-4235-8814-4a45215855b8"), ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	public interface nsIPrintSettings
	{
		void SetPrintOptions(int aType, bool aTurnOnOff);
		bool GetPrintOptions(int aType);
		int GetPrintOptionsBits();
		void GetEffectivePageSize(out double aWidth, double aHeight);
		nsIPrintSettings Clone();
		void Assign(nsIPrintSettings aPS);
		nsIPrintSession GetPrintSession();
		void SetPrintSession(nsIPrintSession aPrintSession);
		int GetStartPageRange();
		void SetStartPageRange(int aStartPageRange);
		int GetEndPageRange();
		void SetEndPageRange(int aEndPageRange);
		double GetEdgeTop();
		void SetEdgeTop(double aEdgeTop);
		double GetEdgeLeft();
		void SetEdgeLeft(double aEdgeLeft);
		double GetEdgeBottom();
		void SetEdgeBottom(double aEdgeBottom);
		double GetEdgeRight();
		void SetEdgeRight(double aEdgeRight);
		double GetMarginTop();
		void SetMarginTop(double aMarginTop);
		double GetMarginLeft();
		void SetMarginLeft(double aMarginLeft);
		double GetMarginBottom();
		void SetMarginBottom(double aMarginBottom);
		double GetMarginRight();
		void SetMarginRight(double aMarginRight);
		double GetUnwriteableMarginTop();
		void SetUnwriteableMarginTop(double aUnwriteableMarginTop);
		double GetUnwriteableMarginLeft();
		void SetUnwriteableMarginLeft(double aUnwriteableMarginLeft);
		double GetUnwriteableMarginBottom();
		void SetUnwriteableMarginBottom(double aUnwriteableMarginBottom);
		double GetUnwriteableMarginRight();
		void SetUnwriteableMarginRight(double aUnwriteableMarginRight);
		double GetScaling();
		void SetScaling(double aScaling);
		bool GetPrintBGColors();
		void SetPrintBGColors(bool aPrintBGColors);
		bool GetPrintBGImages();
		void SetPrintBGImages(bool aPrintBGImages);
		short GetPrintRange();
		void SetPrintRange(short aPrintRange);
		string GetTitle();
		void SetTitle( string aTitle);
		 string GetDocURL();
		void SetDocURL( string aDocURL);
		 string GetHeaderStrLeft();
		void SetHeaderStrLeft( string aHeaderStrLeft);
		 string GetHeaderStrCenter();
		void SetHeaderStrCenter( string aHeaderStrCenter);
		 string GetHeaderStrRight();
		void SetHeaderStrRight( string aHeaderStrRight);
		 string GetFooterStrLeft();
		void SetFooterStrLeft( string aFooterStrLeft);
		 string GetFooterStrCenter();
		void SetFooterStrCenter( string aFooterStrCenter);
		 string GetFooterStrRight();
		void SetFooterStrRight( string aFooterStrRight);
		short GetHowToEnableFrameUI();
		void SetHowToEnableFrameUI(short aHowToEnableFrameUI);
		bool GetIsCancelled();
		void SetIsCancelled(bool aIsCancelled);
		short GetPrintFrameTypeUsage();
		void SetPrintFrameTypeUsage(short aPrintFrameTypeUsage);
		short GetPrintFrameType();
		void SetPrintFrameType(short aPrintFrameType);
		bool GetPrintSilent();
		void SetPrintSilent(bool aPrintSilent);
		bool GetShrinkToFit();
		void SetShrinkToFit(bool aShrinkToFit);
		bool GetShowPrintProgress();
		void SetShowPrintProgress(bool aShowPrintProgress);
		 string GetPaperName();
		void SetPaperName( string aPaperName);
		short GetPaperSizeType();
		void SetPaperSizeType(short aPaperSizeType);
		short GetPaperData();
		void SetPaperData(short aPaperData);
		double GetPaperWidth();
		void SetPaperWidth(double aPaperWidth);
		double GetPaperHeight();
		void SetPaperHeight(double aPaperHeight);
		short GetPaperSizeUnit();
		void SetPaperSizeUnit(short aPaperSizeUnit);
		 string GetPlexName();
		void SetPlexName( string aPlexName);
		 string GetColorspace();
		void SetColorspace( string aColorspace);
		 string GetResolutionName();
		void SetResolutionName( string aResolutionName);
		bool GetDownloadFonts();
		void SetDownloadFonts(bool aDownloadFonts);
		bool GetPrintReversed();
		void SetPrintReversed(bool aPrintReversed);
		bool GetPrintInColor();
		void SetPrintInColor(bool aPrintInColor);
		int GetOrientation();
		void SetOrientation(int aOrientation);
		 string GetPrintCommand();
		void SetPrintCommand( string aPrintCommand);
		int GetNumCopies();
		void SetNumCopies(int aNumCopies);
		 string GetPrinterName();
		void SetPrinterName( string aPrinterName);
		bool GetPrintToFile();
		void SetPrintToFile(bool aPrintToFile);
		 string GetToFileName();
		void SetToFileName( string aToFileName);
		short GetOutputFormat();
		void SetOutputFormat(short aOutputFormat);
		int GetPrintPageDelay();
		void SetPrintPageDelay(int aPrintPageDelay);
		bool GetIsInitializedFromPrinter();
		void SetIsInitializedFromPrinter(bool aIsInitializedFromPrinter);
		bool GetIsInitializedFromPrefs();
		void SetIsInitializedFromPrefs(bool aIsInitializedFromPrefs);
		void SetMarginInTwips(ref nsMargin aMargin);
		void SetEdgeInTwips(ref nsMargin aEdge);
		void GetMarginInTwips(ref nsMargin aMargin);
		void GetEdgeInTwips(ref nsMargin aEdge);
		void SetupSilentPrinting();
		void SetUnwriteableMarginInTwips(ref nsMargin aEdge);
		void GetUnwriteableMarginInTwips(ref nsMargin aEdge);
	}

	public struct nsMargin
	{
	}
}
