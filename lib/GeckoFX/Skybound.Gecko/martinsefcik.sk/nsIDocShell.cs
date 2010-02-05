using System;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System.Text;

namespace Skybound.Gecko
{
	[Guid ("dc4daea1-b43d-406f-bd62-c2ee879192ad")]
	[InterfaceType (ComInterfaceType.InterfaceIsIUnknown)]
	[ComImport ()]
	public interface nsIDocShell : nsISupports {

		[PreserveSigAttribute]
		[MethodImpl (MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
		int loadURI ([MarshalAs (UnmanagedType.Interface)]  nsIURI uri,
				[MarshalAs (UnmanagedType.Interface)]  nsIDocShellLoadInfo loadInfo,
				 uint aLoadFlags,
				 bool firstParty);

		[PreserveSigAttribute]
		[MethodImpl (MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
		int loadStream ([MarshalAs (UnmanagedType.Interface)]  nsIInputStream aStream,
				[MarshalAs (UnmanagedType.Interface)]  nsIURI aURI,
				 /*ACString*/ HandleRef aContentType,
				 /*ACString*/ HandleRef aContentCharset,
				[MarshalAs (UnmanagedType.Interface)]  nsIDocShellLoadInfo aLoadInfo);

		[PreserveSigAttribute]
		[MethodImpl (MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
		int internalLoad ([MarshalAs (UnmanagedType.Interface)]  nsIURI aURI,
				[MarshalAs (UnmanagedType.Interface)]  nsIURI aReferrer,
				[MarshalAs (UnmanagedType.Interface)]  IntPtr aOwner,
				 UInt32 aFlags,
				[MarshalAs(UnmanagedType.LPWStr)]  string aWindowTarget,
				[MarshalAs (UnmanagedType.LPStr)]  string aTypeHint,
				[MarshalAs (UnmanagedType.Interface)]  nsIInputStream aPostDataStream,
				[MarshalAs (UnmanagedType.Interface)]  nsIInputStream aHeadersStream,
				 uint aLoadFlags,
				[MarshalAs (UnmanagedType.Interface)]  nsISHEntry aSHEntry,
				 bool firstParty,
				[MarshalAs (UnmanagedType.Interface)] out nsIDocShell aDocShell,
				[MarshalAs (UnmanagedType.Interface)] out nsIRequest aRequest);

		[PreserveSigAttribute]
		[MethodImpl (MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
		int createLoadInfo ([MarshalAs (UnmanagedType.Interface)] out nsIDocShellLoadInfo loadInfo);

		[PreserveSigAttribute]
		[MethodImpl (MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
		int prepareForNewContentModel ();

		[PreserveSigAttribute]
		[MethodImpl (MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
		int setCurrentURI ([MarshalAs (UnmanagedType.Interface)]  nsIURI aURI);

		[PreserveSigAttribute]
		[MethodImpl (MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
		int firePageHideNotification ( bool isUnload);

		[PreserveSigAttribute]
		[MethodImpl (MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
		int getPresContext ([MarshalAs (UnmanagedType.Interface)] out IntPtr ret);

		[PreserveSigAttribute]
		[MethodImpl (MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
		int getPresShell (out /*nsIPresShell*/ IntPtr ret);

		[PreserveSigAttribute]
		[MethodImpl (MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
		int getEldestPresShell (out /*nsIPresShell*/ IntPtr ret);

		[PreserveSigAttribute]
		[MethodImpl (MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
		int getContentViewer ([MarshalAs (UnmanagedType.Interface)] out nsIContentViewer ret);

		[PreserveSigAttribute]
		[MethodImpl (MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
		int getChromeEventHandler ([MarshalAs (UnmanagedType.Interface)] out nsIDOMEventTarget ret);

		[PreserveSigAttribute]
		[MethodImpl (MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
		int setChromeEventHandler ([MarshalAs (UnmanagedType.Interface)] nsIDOMEventTarget value);

		[PreserveSigAttribute]
		[MethodImpl (MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
		int getDocumentCharsetInfo ([MarshalAs (UnmanagedType.Interface)] out nsIDocumentCharsetInfo ret);

		[PreserveSigAttribute]
		[MethodImpl (MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
		int setDocumentCharsetInfo ([MarshalAs (UnmanagedType.Interface)] nsIDocumentCharsetInfo value);

		[PreserveSigAttribute]
		[MethodImpl (MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
		int getAllowPlugins (out bool ret);

		[PreserveSigAttribute]
		[MethodImpl (MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
		int setAllowPlugins (bool value);

		[PreserveSigAttribute]
		[MethodImpl (MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
		int getAllowJavascript (out bool ret);

		[PreserveSigAttribute]
		[MethodImpl (MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
		int setAllowJavascript (bool value);

		[PreserveSigAttribute]
		[MethodImpl (MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
		int getAllowMetaRedirects (out bool ret);

		[PreserveSigAttribute]
		[MethodImpl (MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
		int setAllowMetaRedirects (bool value);

		[PreserveSigAttribute]
		[MethodImpl (MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
		int getAllowSubframes (out bool ret);

		[PreserveSigAttribute]
		[MethodImpl (MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
		int setAllowSubframes (bool value);

		[PreserveSigAttribute]
		[MethodImpl (MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
		int getAllowImages (out bool ret);

		[PreserveSigAttribute]
		[MethodImpl (MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
		int setAllowImages (bool value);

		[PreserveSigAttribute]
		[MethodImpl (MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
		int getDocShellEnumerator ( int aItemType,
				 int aDirection,
				[MarshalAs (UnmanagedType.Interface)] out nsISimpleEnumerator ret);

		[PreserveSigAttribute]
		[MethodImpl (MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
		int getAppType (out uint ret);

		[PreserveSigAttribute]
		[MethodImpl (MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
		int setAppType (uint value);

		[PreserveSigAttribute]
		[MethodImpl (MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
		int getAllowAuth (out bool ret);

		[PreserveSigAttribute]
		[MethodImpl (MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
		int setAllowAuth (bool value);

		[PreserveSigAttribute]
		[MethodImpl (MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
		int getZoom (out float ret);

		[PreserveSigAttribute]
		[MethodImpl (MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
		int setZoom (float value);

		[PreserveSigAttribute]
		[MethodImpl (MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
		int getMarginWidth (out int ret);

		[PreserveSigAttribute]
		[MethodImpl (MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
		int setMarginWidth (int value);

		[PreserveSigAttribute]
		[MethodImpl (MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
		int getMarginHeight (out int ret);

		[PreserveSigAttribute]
		[MethodImpl (MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
		int setMarginHeight (int value);

		[PreserveSigAttribute]
		[MethodImpl (MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
		int getHasFocus (out bool ret);

		[PreserveSigAttribute]
		[MethodImpl (MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
		int setHasFocus (bool value);

		[PreserveSigAttribute]
		[MethodImpl (MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
		int getCanvasHasFocus (out bool ret);

		[PreserveSigAttribute]
		[MethodImpl (MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
		int setCanvasHasFocus (bool value);

		[PreserveSigAttribute]
		[MethodImpl (MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
		int tabToTreeOwner ( bool forward,
				out bool tookFocus);

		[PreserveSigAttribute]
		[MethodImpl (MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
		int getBusyFlags (out uint ret);

		[PreserveSigAttribute]
		[MethodImpl (MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
		int getLoadType (out uint ret);

		[PreserveSigAttribute]
		[MethodImpl (MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
		int setLoadType (uint value);

		[PreserveSigAttribute]
		[MethodImpl (MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
		int isBeingDestroyed (out bool ret);

		[PreserveSigAttribute]
		[MethodImpl (MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
		int getIsExecutingOnLoadHandler (out bool ret);

		[PreserveSigAttribute]
		[MethodImpl (MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
		int getLayoutHistoryState ([MarshalAs (UnmanagedType.Interface)] out IntPtr ret);

		[PreserveSigAttribute]
		[MethodImpl (MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
		int setLayoutHistoryState ([MarshalAs (UnmanagedType.Interface)] IntPtr value);

		[PreserveSigAttribute]
		[MethodImpl (MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
		int getShouldSaveLayoutState (out bool ret);

		[PreserveSigAttribute]
		[MethodImpl (MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
		int getSecurityUI ([MarshalAs (UnmanagedType.Interface)] out nsISecureBrowserUI ret);

		[PreserveSigAttribute]
		[MethodImpl (MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
		int setSecurityUI ([MarshalAs (UnmanagedType.Interface)] nsISecureBrowserUI value);

		[PreserveSigAttribute]
		[MethodImpl (MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
		int suspendRefreshURIs ();

		[PreserveSigAttribute]
		[MethodImpl (MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
		int resumeRefreshURIs ();

		[PreserveSigAttribute]
		[MethodImpl (MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
		int beginRestore ([MarshalAs (UnmanagedType.Interface)]  nsIContentViewer viewer,
				 bool top);

		[PreserveSigAttribute]
		[MethodImpl (MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
		int finishRestore ();

		[PreserveSigAttribute]
		[MethodImpl (MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
		int getPreviousTransIndex (out int ret);

		[PreserveSigAttribute]
		[MethodImpl (MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
		int getLoadedTransIndex (out int ret);

		[PreserveSigAttribute]
		[MethodImpl (MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
		int historyPurged ( int numEntries);

		[PreserveSigAttribute]
		[MethodImpl (MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
		int getSessionStorageForURI ([MarshalAs (UnmanagedType.Interface)]  nsIURI uri,
				[MarshalAs (UnmanagedType.Interface)] out nsIDOMStorage ret);

		[PreserveSigAttribute]
		[MethodImpl (MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
		int addSessionStorage ( /*ACString*/ HandleRef aDomain,
				[MarshalAs (UnmanagedType.Interface)]  nsIDOMStorage storage);

		[PreserveSigAttribute]
		[MethodImpl (MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
		int getCurrentDocumentChannel ([MarshalAs (UnmanagedType.Interface)] out IntPtr ret);

		[PreserveSigAttribute]
		[MethodImpl (MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
		int setChildOffset ( uint offset);

		[PreserveSigAttribute]
		[MethodImpl (MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
		int getIsInUnload (out bool ret);

		[PreserveSigAttribute]
		[MethodImpl (MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
		int getChannelIsUnsafe (out bool ret);

		[PreserveSigAttribute]
		[MethodImpl (MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
		int DetachEditorFromWindow ();

		[PreserveSigAttribute]
		[MethodImpl (MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
		int getIsOffScreenBrowser (out bool ret);

		[PreserveSigAttribute]
		[MethodImpl (MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
		int setIsOffScreenBrowser (bool value);

	}
}
