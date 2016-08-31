using System;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using Bloom.Collection;
using Newtonsoft.Json;

namespace Bloom.Api
{
	public delegate void EndpointHandler(ApiRequest request);

	public class EndpointRegistration
	{
		public bool HandleOnUIThread = true;
		public EndpointHandler Handler;
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
			return ServerBase.GetLocalPathWithoutQuery(this._requestInfo);
		}

	
		public HttpMethods HttpMethod
		{
			get
			{
				return _requestInfo.HttpMethod;
			}
		}

		public void Succeeded()
		{
			_requestInfo.ContentType = "text/plain";
			_requestInfo.WriteCompleteOutput("OK");
		}

		//Used when an anchor has given us info, but we don't actually want the browser to navigate
		//For example, anchors that lead to help lead to an api handler that opens help but then
		//calls this so that the browser just stays where it was.
		public void SucceededDoNotNavigate()
		{
			_requestInfo.SucceededDoNotNavigate();
		}

		public void ReplyWithText(string text)
		{
			//Debug.WriteLine(this.Requestinfo.LocalPathWithoutQuery + ": " + text);
			_requestInfo.ContentType = "text/plain";
			_requestInfo.WriteCompleteOutput(text);
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
		public void ReplyWithImage(string localPath)
		{
			_requestInfo.ReplyWithImage(localPath);
		}

		public void Failed(string text)
		{
			//Debug.WriteLine(this.Requestinfo.LocalPathWithoutQuery+": "+text);
			_requestInfo.ContentType = "text/plain";
			_requestInfo.WriteError(503,text);
		}

		public static bool Handle(EndpointRegistration endpointRegistration, IRequestInfo info, CollectionSettings collectionSettings, Book.Book currentBook)
		{
			var request = new ApiRequest(info, collectionSettings, currentBook);
			try
			{
				if(Program.RunningUnitTests) 
				{
					endpointRegistration.Handler(request);
				}
				else
				{
					var formForSynchronizing = Application.OpenForms.Cast<Form>().Last();
					if (endpointRegistration.HandleOnUIThread && formForSynchronizing.InvokeRequired)
					{
						formForSynchronizing.Invoke(endpointRegistration.Handler, request);
					}
					else
					{
						endpointRegistration.Handler(request);
					}
				}
				if(!info.HaveOutput)
				{
					throw new ApplicationException(string.Format("The EndpointHandler for {0} never called a Succeeded(), Failed(), or ReplyWith() Function.", info.RawUrl.ToString()));
				}
			}
			catch (Exception e)
			{
				SIL.Reporting.ErrorReport.ReportNonFatalExceptionWithMessage(e, info.RawUrl);
				return false;
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
			Debug.Assert(_requestInfo.HttpMethod==HttpMethods.Post);
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
			if(!string.IsNullOrWhiteSpace(s))
			{
				return s;
			}
			throw new ApplicationException("The query " + _requestInfo.RawUrl + " should have post string");
		}

		public string RequiredPostValue(string key)
		{
			Debug.Assert(_requestInfo.HttpMethod == HttpMethods.Post);
			var values = _requestInfo.GetPostDataWhenFormEncoded().GetValues(key);
			if(values == null || values.Length != 1)
				throw new ApplicationException("The query " + _requestInfo.RawUrl + " should have 1 value for "+key);
			return values[0];
		}
	}
}