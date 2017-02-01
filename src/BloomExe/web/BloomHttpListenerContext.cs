using System.Net;

namespace Bloom.web
{
	/// <summary>
	/// At this point, the only point of these classes is so we can write tests without having
	/// to try to spin up a real HttpListener which causes problems on TeamCity.
	///
	/// The Bloom... concrete classes are simply wrappers for the real objects.
	/// </summary>
	public interface IHttpListenerContext
	{
		IHttpListenerRequest Request { get; }
		HttpListenerResponse Response { get; }
	}

	public class BloomHttpListenerContext : IHttpListenerContext
	{
		private readonly HttpListenerContext _actualContext;

		public BloomHttpListenerContext(HttpListenerContext context)
		{
			_actualContext = context;
		}

		public IHttpListenerRequest Request
		{
			get { return new BloomHttpListenerRequest(_actualContext.Request); }
		}

		public HttpListenerResponse Response
		{
			get { return _actualContext.Response; }
		}
	}

	public interface IHttpListenerRequest
	{
		System.Text.Encoding ContentEncoding { get; }
		string ContentType { get; }
		bool HasEntityBody { get; }
		string HttpMethod { get; }
		System.IO.Stream InputStream { get; }
		string RawUrl { get; }
		System.Uri Url { get; }
	}

	public class BloomHttpListenerRequest : IHttpListenerRequest
	{
		private readonly HttpListenerRequest _actualRequest;

		public BloomHttpListenerRequest(HttpListenerRequest request)
		{
			_actualRequest = request;
		}

		public System.Text.Encoding ContentEncoding
		{
			get { return _actualRequest.ContentEncoding; }
		}

		public string ContentType
		{
			get { return _actualRequest.ContentType; }
		}

		public bool HasEntityBody
		{
			get { return _actualRequest.HasEntityBody; }
		}

		public string HttpMethod
		{
			get { return _actualRequest.HttpMethod; }
		}

		public System.IO.Stream InputStream
		{
			get { return _actualRequest.InputStream; }
		}

		public string RawUrl
		{
			get { return _actualRequest.RawUrl; }
		}

		public System.Uri Url
		{
			get { return _actualRequest.Url; }
		}
	}
}
