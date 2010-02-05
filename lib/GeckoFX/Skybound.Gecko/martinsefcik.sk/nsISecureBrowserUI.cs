using System;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System.Text;

namespace Skybound.Gecko
{
	[Guid ("081e31e0-a144-11d3-8c7c-00609792278c")]
	[InterfaceType (ComInterfaceType.InterfaceIsIUnknown)]
	[ComImport ()]
	public interface nsISecureBrowserUI : nsISupports {

		[PreserveSigAttribute]
		[MethodImpl (MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
		int init ([MarshalAs (UnmanagedType.Interface)]  nsIDOMWindow window);

		[PreserveSigAttribute]
		[MethodImpl (MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
		int getState (out uint ret);

		[PreserveSigAttribute]
		[MethodImpl (MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
		int getTooltipText ( /*AString*/ HandleRef ret);

	}
}
