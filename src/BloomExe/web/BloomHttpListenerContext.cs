using System.IO;
using System.Net;
using EmbedIO;

namespace Bloom.web
{
    /// <summary>
    /// At this point, the only point of these classes is so we can write tests without having
    /// to try to spin up a real HTTP server which causes problems on TeamCity.
    ///
    /// The Bloom... concrete classes are wrappers for EmbedIO's IHttpContext.
    /// </summary>
    public interface IHttpListenerContext
    {
        IHttpListenerRequest Request { get; }
        IHttpListenerResponse Response { get; }
    }

    public class BloomHttpListenerContext : IHttpListenerContext
    {
        private readonly IHttpContext _actualContext;

        public BloomHttpListenerContext(IHttpContext context)
        {
            _actualContext = context;
        }

        public IHttpListenerRequest Request
        {
            get { return new BloomHttpListenerRequest(_actualContext.Request); }
        }

        public IHttpListenerResponse Response
        {
            get { return new BloomHttpListenerResponse(_actualContext.Response); }
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
        private readonly IHttpRequest _actualRequest;

        public BloomHttpListenerRequest(IHttpRequest request)
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
            get { return _actualRequest.HttpVerb.ToString(); }
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

    public interface IHttpListenerResponse
    {
        string ContentType { get; set; }
        long ContentLength64 { get; set; }
        int StatusCode { get; set; }
        string StatusDescription { get; set; }
        Stream OutputStream { get; }
        void AppendHeader(string name, string value);
        void Close(byte[] buffer, bool willBlock);
        void Close();
    }

    public class BloomHttpListenerResponse : IHttpListenerResponse
    {
        private readonly IHttpResponse _actualResponse;
        private long _contentLength;

        public BloomHttpListenerResponse(IHttpResponse response)
        {
            _actualResponse = response;
        }

        public string ContentType
        {
            get { return _actualResponse.ContentType; }
            set { _actualResponse.ContentType = value; }
        }

        public long ContentLength64
        {
            get { return _contentLength; }
            set
            {
                _contentLength = value;
                _actualResponse.ContentLength64 = value;
            }
        }

        public int StatusCode
        {
            get { return _actualResponse.StatusCode; }
            set { _actualResponse.StatusCode = value; }
        }

        public string StatusDescription
        {
            get { return _actualResponse.StatusDescription; }
            set { _actualResponse.StatusDescription = value; }
        }

        public Stream OutputStream
        {
            get { return _actualResponse.OutputStream; }
        }

        public void AppendHeader(string name, string value)
        {
            _actualResponse.Headers.Add(name, value);
        }

        public void Close(byte[] buffer, bool willBlock)
        {
            if (buffer != null && buffer.Length > 0)
            {
                _actualResponse.OutputStream.Write(buffer, 0, buffer.Length);
            }
            _actualResponse.Close();
        }

        public void Close()
        {
            _actualResponse.Close();
        }
    }
}
