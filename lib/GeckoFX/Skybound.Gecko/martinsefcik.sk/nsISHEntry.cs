using System;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System.Text;

namespace Skybound.Gecko
{
	[Guid ("c16fde76-3108-450e-8c8c-ae8286f286ed")]
	[InterfaceType (ComInterfaceType.InterfaceIsIUnknown)]
	[ComImport ()]
	public interface nsISHEntry : nsIHistoryEntry {

		[PreserveSigAttribute]
		[MethodImpl (MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
		int getViewerBounds ([MarshalAs (UnmanagedType.Interface)]  IntPtr bounds);

		[PreserveSigAttribute]
		[MethodImpl (MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
		int setViewerBounds ([MarshalAs (UnmanagedType.Interface)]  IntPtr bounds);

		[PreserveSigAttribute]
		[MethodImpl (MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
		int childShellAt ( int index,
				[MarshalAs (UnmanagedType.Interface)] out nsIDocShellTreeItem ret);

		[PreserveSigAttribute]
		[MethodImpl (MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
		int clearChildShells ();

		[PreserveSigAttribute]
		[MethodImpl (MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
		int syncPresentationState ();

		[PreserveSigAttribute]
		[MethodImpl (MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
		int getLoadType (out uint ret);

		[PreserveSigAttribute]
		[MethodImpl (MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
		int setLoadType (uint value);

		[PreserveSigAttribute]
		[MethodImpl (MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
		int getID (out uint ret);

		[PreserveSigAttribute]
		[MethodImpl (MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
		int setID (uint value);

		[PreserveSigAttribute]
		[MethodImpl (MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
		int getPageIdentifier (out uint ret);

		[PreserveSigAttribute]
		[MethodImpl (MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
		int setPageIdentifier (uint value);

		[PreserveSigAttribute]
		[MethodImpl (MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
		int getContentType ( /*ACString*/ HandleRef ret);

		[PreserveSigAttribute]
		[MethodImpl (MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
		int setContentType (/*ACString*/ HandleRef value);

		[PreserveSigAttribute]
		[MethodImpl (MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
		int getAnyContentViewer ([MarshalAs (UnmanagedType.Interface)] out nsISHEntry ownerEntry,
				[MarshalAs (UnmanagedType.Interface)] out nsIContentViewer ret);

		[PreserveSigAttribute]
		[MethodImpl (MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
		int getOwner ([MarshalAs (UnmanagedType.Interface)] out IntPtr ret);

		[PreserveSigAttribute]
		[MethodImpl (MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
		int setOwner ([MarshalAs (UnmanagedType.Interface)] IntPtr value);

		[PreserveSigAttribute]
		[MethodImpl (MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
		int forgetEditorData ([MarshalAs (UnmanagedType.Interface)] out IntPtr ret);

		[PreserveSigAttribute]
		[MethodImpl (MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
		int setEditorData ([MarshalAs (UnmanagedType.Interface)]  IntPtr aData);

	}
}
