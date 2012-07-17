using System;
using System.IO;
using System.Net;
using Bloom.web;
using Palaso.IO;
using Palaso.Reporting;

namespace Bloom.ImageProcessing
{
	/// <summary>
	/// This is a local http server which just serves up images. Its job is to take the original image
	/// and lower it to screen resolution, because gecko was having so much trouble dealing with hi-res
	/// images intended for print publications. While this could have been accomplished just making the
	/// img src attribute point to an alternate destination on disk, I did it this way so that we can
	/// generate lo-res images in an asynchronous fashion, which will degrade nicely on slower machines.
	/// That is, the browser is happy to show the picture later, when it is ready, if it is comming from
	/// an http request. In constrast, a file:// is just there or not there... no async about it.
	/// </summary>
	public class ImageServer : IDisposable
	{
		public static bool CommunicationTestFailed;

		private HttpListener _listener;
		private LowResImageCache _cache;
		public ImageServer()
		{
			_cache = new LowResImageCache();
		}

		public void Start()
		{
			_listener = new HttpListener();
			_listener.AuthenticationSchemes = AuthenticationSchemes.Anonymous;
			_listener.Prefixes.Add(GetPathEndingInSlash());
			//nb: had trouble with 8080. Remember to enable this with (windows 7 up): netsh http add urlacl url=http://localhost:8089/bloom user=everyone
			//on Windows XP, use httpcfg. I haven't tested this, but I think it may be: HTTPCFG set urlacl -u http://+:8089/bloom/ /a D:(A;;GX;;;WD)
			_listener.Start();
			_listener.BeginGetContext(new AsyncCallback(GetContextCallback), _listener);
		}


		private void GetContextCallback(IAsyncResult ar)
		{
			if (_listener == null || !_listener.IsListening)
				return; //strangely, this callback is fired when we close downn the listener

			try
			{
				HttpListenerContext context = _listener.EndGetContext(ar);
				HttpListenerRequest request = context.Request;
				MakeReply(new RequestInfo(context));
				_listener.BeginGetContext(new AsyncCallback(GetContextCallback), _listener);
			}
			catch (Exception error)
			{
				Logger.WriteEvent(error.Message);
#if DEBUG
				throw;
#endif
			}
		}

		/// <summary>
		/// This is designed to be easily unit testable by not taking actual HttpContext, but doing everything through this IRequestInfo object
		/// </summary>
		/// <param name="info"></param>
		public void MakeReply(IRequestInfo info)
		{
			if(info.RawUrl.EndsWith("testconnection"))
			{
				info.WriteCompleteOutput("OK");
				return;
			}
			var r = info.RawUrl.Replace("/bloom/", "");
			r = r.Replace("%3A", ":");
			r = r.Replace("%20", " ");
			r = r.Replace("%27", "'");
			if (r.EndsWith(".png") || r.EndsWith(".jpg"))
			{
				info.ContentType = "image/png";

				r = r.Replace("thumbnail", "");
				//if (r.Contains("thumb"))
				{
					if (File.Exists(r))
					{
						info.ReplyWithImage(_cache.GetPathToResizedImage(r));
					}
					else
					{
						Logger.WriteEvent("**ImageServer: File Missing: "+r);
						info.WriteError(404);
					}
				}

			}
		}

		public void Dispose()
		{
			_cache.Dispose();
			_cache = null;

			if (_listener != null)
			{
				_listener.Close();
			}
			_listener = null;
		}

		public static void TestAndThrowIfCommunicationIsBlocked()
		{
			try
			{
				var x = new WebClientWithTimeout {Timeout = 3000};

				if ("OK" != x.DownloadString(GetPathEndingInSlash()+"testconnection"))
				{
					CommunicationTestFailed = true;
					throw new ApplicationException("Bloom server communication Test Failed");
				}

			}
			catch (Exception)
			{
				CommunicationTestFailed = true;
				throw new ApplicationException("Bloom server communication Test Failed");
			}
		}

		public static string GetPathEndingInSlash()
		{
			return "http://localhost:8089/bloom/";
		}
	}


	/// <summary>
	/// the base class waits for 30 seconds, which is too long for local thing like we are doing
	/// </summary>
	public class WebClientWithTimeout : WebClient
	{
		private int _timeout;
		/// <summary>
		/// Time in milliseconds
		/// </summary>
		public int Timeout
		{
			get
			{
				return _timeout;
			}
			set
			{
				_timeout = value;
			}
		}

		public WebClientWithTimeout()
		{
			this._timeout = 60000;
		}

		public WebClientWithTimeout(int timeout)
		{
			this._timeout = timeout;
		}

		protected override WebRequest GetWebRequest(Uri address)
		{
			var result = base.GetWebRequest(address);
			result.Timeout = this._timeout;
			return result;
		}
	}

}
