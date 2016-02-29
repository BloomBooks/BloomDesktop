using System;
using System.Collections.Specialized;
using System.Linq;
using Bloom.Collection;

namespace Bloom.web
{

	public delegate void SimpleHandler(SimpleHandlerRequest request);

	/// <summary>
	/// A wrapper around IRequestInfo for use by simple http handlers
	/// </summary>
	public class SimpleHandlerRequest
	{
		private readonly IRequestInfo Requestinfo;
		public readonly CollectionSettings CurrentCollectionSettings;
		public NameValueCollection Parameters;

		public SimpleHandlerRequest(IRequestInfo requestinfo, CollectionSettings currentCollectionSettings)
		{
			Requestinfo = requestinfo;
			CurrentCollectionSettings = currentCollectionSettings;
			Parameters = requestinfo.GetQueryString();
		}

		public string RequiredParam(string name)
		{
			if(Parameters.AllKeys.Contains(name))
				return Parameters[name];
			throw new ApplicationException("The query "+Requestinfo.RawUrl+" should have parameter "+name);
		}

		public void Succeeded()
		{
			//review: not sure what is proper if we have nothing to say. This will work.
			Requestinfo.ContentType = "text/plain";
			Requestinfo.WriteCompleteOutput("OK");
		}
		public void Succeeded(string text)
		{
			Requestinfo.ContentType = "text/plain";
			Requestinfo.WriteCompleteOutput(text);
		}
		public void Failed(string text)
		{
			Requestinfo.ContentType = "text/plain";
			Requestinfo.WriteError(503,text);
		}
	}
}
