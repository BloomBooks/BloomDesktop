using System.Net;

namespace Bloom.web
{
	/// <summary>
	/// At this point, the only point of these classes is so we can write tests without having
	/// to try to spin up a real HttpListener which causes problems on TeamCity.
	///
	/// The Bloom... concrete classes are simply wrappers for the real objects.
	/// </summary>
	public abstract class HttpListenerContextBase
	{
		public virtual HttpListenerRequestBase Request { get; private set; }
		public virtual HttpListenerResponse Response { get; private set; }
	}

	public class BloomHttpListenerContext : HttpListenerContextBase
	{
		private readonly HttpListenerContext _actualContext;

		public BloomHttpListenerContext(HttpListenerContext context)
		{
			_actualContext = context;
		}

		public override HttpListenerRequestBase Request
		{
			get { return new BloomHttpListenerRequest(_actualContext.Request); }
		}

		public override HttpListenerResponse Response
		{
			get { return _actualContext.Response; }
		}
	}

	public abstract class HttpListenerRequestBase
	{
		public virtual System.Text.Encoding ContentEncoding { get; private set; }
		public virtual string ContentType { get; private set; }
		public virtual bool HasEntityBody { get; private set; }
		public virtual string HttpMethod { get; private set; }
		public virtual System.IO.Stream InputStream { get; private set; }
		public virtual string RawUrl { get; private set; }
		public virtual System.Uri Url { get; private set; }
	}

	public class BloomHttpListenerRequest : HttpListenerRequestBase
	{
		private readonly HttpListenerRequest _actualRequest;

		public BloomHttpListenerRequest(HttpListenerRequest request)
		{
			_actualRequest = request;
		}

		public override System.Text.Encoding ContentEncoding
		{
			get { return _actualRequest.ContentEncoding; }
		}

		public override string ContentType
		{
			get { return _actualRequest.ContentType; }
		}

		public override bool HasEntityBody
		{
			get { return _actualRequest.HasEntityBody; }
		}

		public override string HttpMethod
		{
			get { return _actualRequest.HttpMethod; }
		}

		public override System.IO.Stream InputStream
		{
			get { return _actualRequest.InputStream; }
		}

		public override string RawUrl
		{
			get { return _actualRequest.RawUrl; }
		}

		public override System.Uri Url
		{
			get { return _actualRequest.Url; }
		}
	}
}
