using System;
using System.IO;
using System.Net;
using System.Windows.Forms;
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
	///
	///Hints
	/// To check what's in the url access control list on Vista and up: netsh http show urlacl
	///on XP: httpcfg query urlacl
	///
	///nb: had trouble with 8080. Remember to enable this with (windows 7 up): netsh http add urlacl url=http://localhost:8089/bloom user=everyone
	///on Windows XP, use httpcfg. I haven't tested this, but I think it may be: HTTPCFG set urlacl -u http://+:8089/bloom/ /a D:(A;;GX;;;WD)
	/// </summary>
	public class ImageServer : IDisposable
	{
		public static bool IsAbleToUsePort;

		private HttpListener _listener;
		private LowResImageCache _cache;
		private bool _isDisposing;

		public ImageServer()
		{
			_cache = new LowResImageCache();
		}

		public void StartWithSetupIfNeeded()
		{
			bool didStart = false;
			try
			{
				didStart = TryStart();
			}
			catch (Exception)
			{
			}
			if (didStart)
				return;

			AddUrlAccessControlEntry();
			try
			{
				didStart = TryStart();
			}
			catch (Exception)
			{
			}

			if(!didStart)
			{
				var e = new ApplicationException("Could not start ImageServer");//passing this in will enable the details button
				ErrorReport.NotifyUserOfProblem(e, "What Happened\r\nBloom could not start its image server, which keeps hi-res images from chewing up memory. You will still be able to work, but Bloom will take more memory, and hi-res images may not always show.\r\n\r\nWhat caused this?\r\nProbably Bloom does not know how to get your specific Windows operating system to allow its image server to run. \r\n\r\n What can you do?\r\nClick 'Details' and report the problem to the developers.");
			}
		}


		private bool TryStart()
		{
			_listener = new HttpListener();
			_listener.AuthenticationSchemes = AuthenticationSchemes.Anonymous;
			_listener.Prefixes.Add(GetPathEndingInSlash());
			_listener.Start();
			_listener.BeginGetContext(new AsyncCallback(GetContextCallback), _listener);
			return GetIsAbleToUsePort();
		}


		private bool GetIsAbleToUsePort()
		{
			IsAbleToUsePort = false;
			try
			{
				var x = new WebClientWithTimeout { Timeout = 3000 };

				IsAbleToUsePort = ("OK" == x.DownloadString(GetPathEndingInSlash() + "testconnection"));
			}
			catch (Exception)
			{
				IsAbleToUsePort = false;
			}
			return IsAbleToUsePort;
		}

		/// <summary>
		/// TODO: Note: doing this at runtim isn't as good as doing it in the installer, because we have no way of
		/// removing these entries on uninstall (but the installer does).
		/// </summary>
		private static void AddUrlAccessControlEntry()
		{
			MessageBox.Show(
				"We need to do one more thing before Bloom is ready. Bloom needs temporary administrator privileges to set up part of its communication with the embedded web browser.\r\n\r\nAfter you click 'OK', you may be asked to authorize this step.",
				"Almost there!", MessageBoxButtons.OK);

			var startInfo = new System.Diagnostics.ProcessStartInfo();
			startInfo.UseShellExecute = true;
			startInfo.Verb = "runas"; //makes it as for elevation to admin rights

			if (Environment.OSVersion.Version.Major == 5 /*win xp*/)
			{
				startInfo.FileName = "httpcfg";
				startInfo.Arguments = "set urlacl -u http://localhost:8089/bloom/ /a \"D:(A;;GX;;;WD)\"";
			}
			else
			{
				startInfo.FileName = "netsh";
				startInfo.Arguments = "http add urlacl url=http://localhost:8089/bloom user=everyone";
			}
			System.Diagnostics.Process.Start(startInfo);
		}


		private void GetContextCallback(IAsyncResult ar)
		{
			if (_isDisposing || _listener == null || !_listener.IsListening)
				return; //strangely, this callback is fired when we close down the listener
			string rawurl="unknown";
			try
			{
				HttpListenerContext context = _listener.EndGetContext(ar);
				rawurl = context.Request.RawUrl;
				HttpListenerRequest request = context.Request;
				MakeReply(new RequestInfo(context));
				_listener.BeginGetContext(new AsyncCallback(GetContextCallback), _listener);
			}
			catch(HttpListenerException e)
			{
				//http://stackoverflow.com/questions/4801868/c-sharp-problem-with-httplistener
				Logger.WriteEvent("At ImageServer: GetContextCallback(): HttpListenerException, which may indicate that the caller closed the connection before we could reply. msg=" + e.Message);
				Logger.WriteEvent("At ImageServer: GetContextCallback(): url=" + rawurl);
			}
			catch (Exception error)
			{
				Logger.WriteEvent("At ImageServer: GetContextCallback(): msg="+ error.Message);
				Logger.WriteEvent("At ImageServer: GetContextCallback(): url="+rawurl);
				Logger.WriteEvent("At ImageServer: GetContextCallback(): stack=");
				Logger.WriteEvent(error.StackTrace);
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
				info.ContentType = r.EndsWith(".png") ? "image/png" : "image/jpeg";

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
			_isDisposing = true;
			_cache.Dispose();
			_cache = null;

			if (_listener != null)
			{
				_listener.Close();
			}
			_listener = null;
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
