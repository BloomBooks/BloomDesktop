using System;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Windows.Forms;
using Bloom.Collection;
using Newtonsoft.Json;
using SIL.Code;

namespace Bloom.Api
{
	public delegate void EndpointHandler(ApiRequest request);

	public class EndpointRegistration
	{
		public bool HandleOnUIThread = true;
		public EndpointHandler Handler;
		public bool RequiresSync = true; // set false if handler does its own thread-handling.
	}


	/// <summary>
	/// When the Bloom UI makes an API call, a method that has been registered to handle that
	/// endpoint is called and given one of these. That method uses this class to get information
	/// on the request, and also to reply to the caller.
	/// </summary>
	/// <remarks>The goal here is to reduce code while increasing clarity and error catching.</remarks>
	public class ApiRequest
	{
		private readonly IRequestInfo _requestInfo;
		public readonly CollectionSettings CurrentCollectionSettings;
		public readonly Book.Book CurrentBook;
		public NameValueCollection Parameters;

		public ApiRequest(IRequestInfo requestinfo, CollectionSettings currentCollectionSettings, Book.Book currentBook)
		{
			_requestInfo = requestinfo;
			CurrentCollectionSettings = currentCollectionSettings;
			CurrentBook = currentBook;
			Parameters = requestinfo.GetQueryParameters();
		}

		/// <summary>
		/// Get the actual local path that the server would retrieve given a Bloom URL
		/// that ends up at a local file. For now it is mainly useful for things in the book folder; it doesn't have
		/// all the smarts to locate files shipped with the application, it is just concerned with reversing
		/// the various tricks we use to encode paths as URLs.
		/// </summary>
		public string LocalPath()
		{
			return BloomServer.GetLocalPathWithoutQuery(this._requestInfo);
		}


		public HttpMethods HttpMethod
		{
			get
			{
				return _requestInfo.HttpMethod;
			}
		}

		/// <summary>
		/// This is safe to use with axios.Post. See BL-4901. There, not returning any text at all
		/// caused some kind of problem in axios.post(), after the screen had been shut down.
		/// </summary>
		public void PostSucceeded()
		{
			_requestInfo.ContentType = "text/plain";
			_requestInfo.WriteCompleteOutput("OK");
		}

		//Used when an anchor has given us info, but we don't actually want the browser to navigate
		//For example, anchors that lead to help lead to an api handler that opens help but then
		//calls this so that the browser just stays where it was.
		public void ExternalLinkSucceeded()
		{
			_requestInfo.ExternalLinkSucceeded();
		}

		public void ReplyWithText(string text)
		{
			//Debug.WriteLine(this.Requestinfo.LocalPathWithoutQuery + ": " + text);
			_requestInfo.ContentType = "text/plain";
			_requestInfo.WriteCompleteOutput(text);
		}

		public void ReplyWithAudioFileContents(string path)
		{
			_requestInfo.ContentType = path.EndsWith(".mp3") ? "audio/mpeg" : "audio/wav";
			_requestInfo.ReplyWithFileContent(path);
		}

		public void ReplyWithHtml(string html)
		{
			_requestInfo.ContentType = "text/html";
			_requestInfo.WriteCompleteOutput(html);
		}

		public void ReplyWithJson(string json)
		{
			//Debug.WriteLine(this.Requestinfo.LocalPathWithoutQuery + ": " + json);
			_requestInfo.ContentType = "application/json";
			_requestInfo.WriteCompleteOutput(json);
		}

		public void ReplyWithJson(object objectToMakeJson)
		{
			//Debug.WriteLine(this.Requestinfo.LocalPathWithoutQuery + ": " + json);
			_requestInfo.ContentType = "application/json";
			_requestInfo.WriteCompleteOutput(JsonConvert.SerializeObject(objectToMakeJson));
		}

		public void ReplyWithImage(string imagePath)
		{
			_requestInfo.ReplyWithImage(imagePath);
		}

		/// <summary>
		/// Use this one in cases where the error has already been output to a progress box,
		/// and repeating the error is just noise.
		/// </summary>
		public void Failed()
		{
			_requestInfo.ContentType = "text/plain";
			_requestInfo.WriteError(503);
		}

		public void Failed(string text)
		{
			_requestInfo.ContentType = "text/plain";
			_requestInfo.WriteError(503, text);
		}

		public static bool Handle(EndpointRegistration endpointRegistration, IRequestInfo info, CollectionSettings collectionSettings, Book.Book currentBook)
		{
			var request = new ApiRequest(info, collectionSettings, currentBook);
			try
			{
				if (Program.RunningUnitTests)
				{
					endpointRegistration.Handler(request);
				}
				else
				{
					var formForSynchronizing = Application.OpenForms.Cast<Form>().Last();
					if (endpointRegistration.HandleOnUIThread && formForSynchronizing.InvokeRequired)
					{
						InvokeWithErrorHandling(endpointRegistration, formForSynchronizing, request);
					}
					else
					{
						endpointRegistration.Handler(request);
					}
				}
				if (!info.HaveOutput)
				{
					throw new ApplicationException(string.Format("The EndpointHandler for {0} never called a Succeeded(), Failed(), or ReplyWith() Function.", info.RawUrl.ToString()));
				}
			}
			catch (System.IO.IOException e)
			{
				var shortMsg = String.Format(L10NSharp.LocalizationManager.GetDynamicString("Bloom", "Errors.CannotAccessFile", "Cannot access {0}"), info.RawUrl);
				var longMsg = String.Format("Bloom could not access {0}.  The file may be open in another program.", info.RawUrl);
				NonFatalProblem.Report(ModalIf.None, PassiveIf.All, shortMsg, longMsg, e);
				return false;
			}
			catch (Exception e)
			{
				//Hard to reproduce, but I got one of these supertooltip disposal errors in a yellow box
				//while switching between publish tabs (e.g. /bloom/api/publish/android/cleanup).
				//I don't think these are worth alarming the user about, so let's be sensitive to what channel we're on.
				NonFatalProblem.Report(ModalIf.Alpha, PassiveIf.All, "Error in " + info.RawUrl, exception: e);
				return false;
			}
			return true;
		}

		// If you just Invoke(), the stack trace of any generated exception gets lost.
		// The stacktrace instead just ends with the invoke(), which isn't useful. So here we wrap
		// the call to the handler in a delegate that catches the exception and saves it
		// in our local scope, where we can then use it for error reporting.
		private static bool InvokeWithErrorHandling(EndpointRegistration endpointRegistration,
			Form formForSynchronizing, ApiRequest request)
		{
			Exception handlerException = null;
			formForSynchronizing.Invoke(new Action<ApiRequest>((req) =>
			{
				try
				{
					endpointRegistration.Handler(req);
				}
				catch (Exception error)
				{
					handlerException = error;
				}
			}), request);
			if (handlerException != null)
			{
				ExceptionDispatchInfo.Capture(handlerException).Throw();
			}
			return true;
		}

		public UrlPathString RequiredFileNameOrPath(string name)
		{
			if (Parameters.AllKeys.Contains(name))
				return UrlPathString.CreateFromUnencodedString(Parameters[name]);
			throw new ApplicationException("The query " + _requestInfo.RawUrl + " should have parameter " + name);
		}

		public string RequiredParam(string name)
		{
			if (Parameters.AllKeys.Contains(name))
				return Parameters[name];
			throw new ApplicationException("The query " + _requestInfo.RawUrl + " should have parameter " + name);
		}
		public string RequiredPostJson()
		{
			Debug.Assert(_requestInfo.HttpMethod == HttpMethods.Post);
			var json = _requestInfo.GetPostJson();
			if (!string.IsNullOrWhiteSpace(json))
			{
				return json;
			}
			throw new ApplicationException("The query " + _requestInfo.RawUrl + " should have post json");
		}
		public string RequiredPostString()
		{
			Debug.Assert(_requestInfo.HttpMethod == HttpMethods.Post);
			var s = _requestInfo.GetPostString();
			if (!string.IsNullOrWhiteSpace(s))
			{
				return s;
			}
			throw new ApplicationException("The query " + _requestInfo.RawUrl + " should have post string");
		}

		/// <summary>
		/// Get an enum value of type T that was passed as application/json
		/// </summary>
		/// <typeparam name="T"></typeparam>
		/// <remarks>requires something like this:
		///    axios.post("api/bloom/foo", myEnum, {
		///       headers: { "Content-Type": "application/json" }});
		/// </remarks>
		/// <returns>An enum value</returns>
		internal T RequiredPostEnumAsJson<T>()
		{
			Debug.Assert(typeof(T).IsEnum, "Type passed to RequiredPostEnumAsJson() is not an Enum.");
			return (T)Enum.Parse(typeof(T), RequiredPostJson());
		}

		/// <summary>
		/// Get a boolean value that was passed as application/json
		/// </summary>
		/// <remarks>
		/// Used by BloomServer.RegisterBooleanEndpointHandler() and requires something like this:
		///    axios.post("api/bloom/foo", myBool, {
		///       headers: { "Content-Type": "application/json" }});
		/// </remarks>
		/// <returns></returns>
		internal bool RequiredPostBooleanAsJson()
		{
			// There isn't an obvious choice for passing a simple true/false, but a plain true/false counts as json:
			// https://tools.ietf.org/html/rfc7493#section-4.1  Note we don't have to be compatible with old parsers. so we can just return true or false
			return RequiredPostJson() == "true";
		}

		public string RequiredPostValue(string key)
		{
			Debug.Assert(_requestInfo.HttpMethod == HttpMethods.Post);
			var values = _requestInfo.GetPostDataWhenFormEncoded().GetValues(key);
			if (values == null || values.Length != 1)
				throw new ApplicationException("The query " + _requestInfo.RawUrl + " should have 1 value for " + key);
			return values[0];
		}

		public byte[] RawPostData => _requestInfo.GetRawPostData();
		public Stream RawPostStream => _requestInfo.GetRawPostStream();

		public NameValueCollection GetPostDataWhenFormEncoded()
		{
			return _requestInfo.GetPostDataWhenFormEncoded();
		}

		public void ReplyWithBoolean(bool value)
		{
			// https://tools.ietf.org/html/rfc7493#section-4.1  Note we don't have to be compatible with old parsers. so we can just return true or false
			ReplyWithJson(value);
		}

		public void ReplyWithEnum<T>(T value)
		{
			ReplyWithJson(Enum.GetName(typeof(T), value));
		}
	}
}
