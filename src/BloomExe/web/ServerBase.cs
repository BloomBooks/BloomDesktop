// Copyright (c) 2014 SIL International
// This software is licensed under the MIT License (http://opensource.org/licenses/MIT)
using System;
using System.Net;
using System.Threading;
using System.Windows.Forms;
using Palaso.Reporting;
using Palaso.Code;
using System.Net.Sockets;

namespace Bloom.web
{
	public abstract class ServerBase: IDisposable
	{
		public static bool IsAbleToUsePort;

		private HttpListener _listener;
		private bool _isDisposing;
		private Thread _listenerThread;
		private readonly ManualResetEvent _stop;

		public ServerBase()
		{
			_stop = new ManualResetEvent(false);
		}

		#region Disposable stuff

		#if DEBUG
		/// <summary/>
		~ServerBase()
		{
			Dispose(false);
		}
		#endif

		/// <summary/>
		public bool IsDisposed { get; private set; }

		/// <summary/>
		public void Dispose()
		{
			Dispose(true);
			GC.SuppressFinalize(this);
		}

		/// <summary/>
		protected virtual void Dispose(bool fDisposing)
		{
			System.Diagnostics.Debug.WriteLineIf(!fDisposing, "****** Missing Dispose() call for " + GetType() + ". *******");
			if (fDisposing && !IsDisposed)
			{
				// dispose managed and unmanaged objects
				try
				{
					_isDisposing = true;

					if (_listener != null)
					{
						//prompted by the mysterious BL 273, Crash while closing down the imageserver
						Guard.AgainstNull(_listenerThread, "_listenerThread");
						//prompted by the mysterious BL 273, Crash while closing down the imageserver
						Guard.AgainstNull(_stop, "_stop"); 

						_stop.Set();
						_listenerThread.Join();
						_listener.Stop();

						_listener.Close();
					}
					_listener = null;
				}
				catch (Exception e)
				{
					//prompted by the mysterious BL 273, Crash while closing down the imageserver
					#if DEBUG
					throw;
					#else       //just quitely report this
					DesktopAnalytics.Analytics.ReportException(e);
					#endif
				}
			}
			IsDisposed = true;
		}

		#endregion

		public void StartWithSetupIfNeeded()
		{
			Exception error = null;
			StartWithSetupIfNeeded(out error);
		}

		protected virtual bool StartWithSetupIfNeeded(out Exception error)
		{
			bool didStart = StartWithExceptionHandling(out error);
			if (didStart)
				return true;

			// REVIEW Linux: do we need something similar on Linux?
			if (Palaso.PlatformUtilities.Platform.IsWindows)
			{
				AddUrlAccessControlEntry();
				didStart = StartWithExceptionHandling(out error);
			}
			return didStart;
		}

		private bool StartWithExceptionHandling(out Exception error)
		{
			error = null;
			try
			{
				return TryStart();
			}
			catch (Exception e)
			{
				error = e;
				return false;
			}
		}

		private bool TryStart()
		{
			_listener = new HttpListener();
			_listener.AuthenticationSchemes = AuthenticationSchemes.Anonymous;
			_listener.Prefixes.Add(PathEndingInSlash);
			_listener.Start();
			//			_listener.BeginGetContext(new AsyncCallback(ListenerCallback), _listener);

			_listenerThread = new Thread(HandleRequests);
			_listenerThread.Start();

			return GetIsAbleToUsePort();
		}

		private bool GetIsAbleToUsePort()
		{
			IsAbleToUsePort = false;
			try
			{
				var x = new WebClientWithTimeout { Timeout = 3000 };

				IsAbleToUsePort = ("OK" == x.DownloadString(PathEndingInSlash + "testconnection"));
			}
			catch (Exception e)
			{
				IsAbleToUsePort = false;
			}
			return IsAbleToUsePort;
		}

		private void HandleRequests()
		{
			while (_listener.IsListening)
			{
				var context = _listener.BeginGetContext(new AsyncCallback(ListenerCallback), _listener);
				if (0 == WaitHandle.WaitAny(new[] { _stop, context.AsyncWaitHandle }))
					return;
			}
		}
			
		/// <summary>
		/// TODO: Note: doing this at runtime isn't as good as doing it in the installer, because we have no way of 
		/// removing these entries on uninstall (but the installer does).
		/// </summary>
		private static void AddUrlAccessControlEntry()
		{
			MessageBox.Show(
				string.Format("We need to do one more thing before Bloom is ready. Bloom needs temporary administrator privileges to set up part of its communication with the embedded web browser.{0}{0}After you click 'OK', you may be asked to authorize this step.", Environment.NewLine),
				"Almost there!", MessageBoxButtons.OK);

			var startInfo = new System.Diagnostics.ProcessStartInfo();
			startInfo.UseShellExecute = true;
			startInfo.Verb = "runas"; //makes it as for elevation to admin rights

			if (Environment.OSVersion.Version.Major == 5 /*win xp*/)
			{
				startInfo.FileName = "httpcfg";
				startInfo.Arguments = string.Format("set urlacl -u {0} /a \"D:(A;;GX;;;WD)\"", PathEndingInSlash);
			}
			else
			{
				startInfo.FileName = "netsh";
				startInfo.Arguments = string.Format("http add urlacl url={0} user=everyone", PathEndingInSlash.TrimEnd('/'));
			}
			System.Diagnostics.Process.Start(startInfo);
		}

		private void ListenerCallback(IAsyncResult ar)
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
			}
			catch(HttpListenerException e)
			{
				//http://stackoverflow.com/questions/4801868/c-sharp-problem-with-httplistener
				Logger.WriteEvent("At ServerBase: ListenerCallback(): HttpListenerException, which may indicate that the caller closed the connection before we could reply. msg=" + e.Message);
				Logger.WriteEvent("At ServerBase: ListenerCallback(): url=" + rawurl);
			}
			catch (Exception error)
			{
				Logger.WriteEvent("At ServerBase: ListenerCallback(): msg="+ error.Message);
				Logger.WriteEvent("At ServerBase: ListenerCallback(): url="+rawurl);
				Logger.WriteEvent("At ServerBase: ListenerCallback(): stack=");
				Logger.WriteEvent(error.StackTrace);
#if DEBUG
				throw;
#endif
			}
			finally
			{
				//	_listener.BeginGetContext(new AsyncCallback(ListenerCallback), _listener);
			}
		}

		/// <summary>
		/// This is designed to be easily unit testable by not taking actual HttpContext, but doing everything through this IRequestInfo object
		/// </summary>
		/// <param name="info"></param>
		internal abstract void MakeReply(IRequestInfo info);

		public static string PathEndingInSlash
		{
			get { return "http://localhost:8089/bloom/"; }
		}

		protected static string GetContentType(string extension)
		{
			switch (extension)
			{
				case ".css":  return "text/css";
				case ".gif":  return "image/gif";
				case ".htm":
				case ".html": return "text/html";
				case ".jpg":
				case ".jpeg": return "image/jpeg";
				case ".js":   return "application/x-javascript";
				case ".png":  return "image/png";
				case ".pdf":  return "application/pdf";
				case ".txt":  return "text/plain";
				default:      return "application/octet-stream";
			}
		}
	}
}

