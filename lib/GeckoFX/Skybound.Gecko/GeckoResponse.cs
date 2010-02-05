using System;
using System.Collections.Generic;
using System.Text;
using System.Runtime.InteropServices;

namespace Skybound.Gecko
{
	/// <summary>
	/// Represents a response to a Gecko web request.
	/// </summary>
	public class GeckoResponse
	{
		#region Gecko Interfaces
		[Guid("c63a055a-a676-4e71-bf3c-6cfa11082018"), ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
		interface nsIChannel : nsIRequest
		{
			// nsIRequest:
			new void GetName(nsACString aName);
			new bool IsPending();
			new int GetStatus();
			new void Cancel(int aStatus);
			new void Suspend();
			new void Resume();
			new IntPtr GetLoadGroup(); // nsILoadGroup
			new void SetLoadGroup(IntPtr aLoadGroup);
			new int GetLoadFlags();
			new void SetLoadFlags(int aLoadFlags);
			
			// nsIChannel:
			nsIURI GetOriginalURI();
			void SetOriginalURI(nsIURI aOriginalURI);
			nsIURI GetURI();
			nsISupports GetOwner();
			void SetOwner(nsISupports aOwner);
			nsIInterfaceRequestor GetNotificationCallbacks();
			void SetNotificationCallbacks(nsIInterfaceRequestor aNotificationCallbacks);
			nsISupports GetSecurityInfo();
			void GetContentType(nsACString aContentType);
			void SetContentType(nsACString aContentType);
			void GetContentCharset(nsACString aContentCharset);
			void SetContentCharset(nsACString aContentCharset);
			int GetContentLength();
			void SetContentLength(int aContentLength);
			IntPtr Open(); // nsIInputStream
			void AsyncOpen(IntPtr aListener, nsISupports aContext); // aListener=nsIStreamListener
		}
		
		[Guid("9277fe09-f0cc-4cd9-bbce-581dd94b0260"), ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
		interface nsIHttpChannel : nsIChannel
		{
			// nsIRequest:
			new void GetName(nsACString aName);
			new bool IsPending();
			new int GetStatus();
			new void Cancel(int aStatus);
			new void Suspend();
			new void Resume();
			new IntPtr GetLoadGroup(); // nsILoadGroup
			new void SetLoadGroup(IntPtr aLoadGroup);
			new int GetLoadFlags();
			new void SetLoadFlags(int aLoadFlags);
			
			// nsIChannel:
			new nsIURI GetOriginalURI();
			new void SetOriginalURI(nsIURI aOriginalURI);
			new nsIURI GetURI();
			new nsISupports GetOwner();
			new void SetOwner(nsISupports aOwner);
			new nsIInterfaceRequestor GetNotificationCallbacks();
			new void SetNotificationCallbacks(nsIInterfaceRequestor aNotificationCallbacks);
			new nsISupports GetSecurityInfo();
			new void GetContentType(nsACString aContentType);
			new void SetContentType(nsACString aContentType);
			new void GetContentCharset(nsACString aContentCharset);
			new void SetContentCharset(nsACString aContentCharset);
			new int GetContentLength();
			new void SetContentLength(int aContentLength);
			new IntPtr Open(); // nsIInputStream
			new void AsyncOpen(IntPtr aListener, nsISupports aContext); // aListener=nsIStreamListener

			// nsIHttpChannel:
			void GetRequestMethod(nsACString aRequestMethod);
			void SetRequestMethod(nsACString aRequestMethod);
			nsIURI GetReferrer();
			void SetReferrer(nsIURI aReferrer);
			void GetRequestHeader(nsACString aHeader, nsACString _retval);
			void SetRequestHeader(nsACString aHeader, nsACString aValue, bool aMerge);
			void VisitRequestHeaders(IntPtr aVisitor); // nsIHttpHeaderVisitor
			bool GetAllowPipelining();
			void SetAllowPipelining(bool aAllowPipelining);
			uint GetRedirectionLimit();
			void SetRedirectionLimit(uint aRedirectionLimit);
			int GetResponseStatus();
			void GetResponseStatusText(nsACString aResponseStatusText);
			bool GetRequestSucceeded();
			void GetResponseHeader(nsACString header, nsACString _retval);
			void SetResponseHeader(nsACString header, nsACString value, bool merge);
			void VisitResponseHeaders(IntPtr aVisitor); // nsIHttpHeaderVisitor
			bool IsNoStoreResponse();
			bool IsNoCacheResponse();
		}
		
		[Guid("0cf40717-d7c1-4a94-8c1e-d6c9734101bb"), ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
		interface nsIHttpHeaderVisitor
		{
			void VisitHeader(nsACString aHeader, nsACString aValue);
		}
		
		#endregion
		
		internal GeckoResponse(nsIRequest request)
		{
			Channel = Xpcom.QueryInterface<nsIChannel>(request);
			HttpChannel = Xpcom.QueryInterface<nsIHttpChannel>(request);
		}
		
		nsIChannel Channel;
		nsIHttpChannel HttpChannel;
		
		/// <summary>
		/// Gets the MIME type of the channel's content if available.
		/// </summary>
		public string ContentType
		{
			get { return nsString.Get(Channel.GetContentType); }
		}
		
		/// <summary>
		/// Gets the character set of the channel's content if available and if applicable.
		/// </summary>
		public string ContentCharset
		{
			get { return nsString.Get(Channel.GetContentCharset); }
		}
		
		/// <summary>
		/// Gets the length of the data associated with the channel if available. A value of -1 indicates that the content length is unknown.
		/// </summary>
		public int ContentLength
		{
			get { return Channel.GetContentLength(); }
		}
		
		/// <summary>
		/// Gets the HTTP request method.
		/// </summary>
		public string HttpRequestMethod
		{
			get { return (HttpChannel == null) ? null : nsString.Get(HttpChannel.GetRequestMethod); }
		}
		
		/// <summary>
		/// Returns true if the HTTP response code indicates success. This value will be true even when processing a 404 response because a 404 response
		/// may include a message body that (in some cases) should be shown to the user.
		/// </summary>
		public bool HttpRequestSucceeded
		{
			get { return (HttpChannel == null) ? true : HttpChannel.GetRequestSucceeded(); }
		}
		
		/// <summary>
		/// Gets the HTTP response code (a value of 200 indicates success).
		/// </summary>
		public int HttpResponseStatus
		{
			get { return (HttpChannel == null) ? 0 :HttpChannel.GetResponseStatus(); }
		}
		
		/// <summary>
		/// Gets the HTTP response status text.
		/// </summary>
		public string HttpResponseStatusText
		{
			get { return (HttpChannel == null) ? null : nsString.Get(HttpChannel.GetResponseStatusText); }
		}
	}
}
