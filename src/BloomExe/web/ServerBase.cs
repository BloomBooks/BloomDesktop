// Copyright (c) 2015 SIL International
// This software is licensed under the MIT License (http://opensource.org/licenses/MIT)
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Windows.Forms;
using Palaso.Code;
using Palaso.Reporting;
using ThreadState = System.Threading.ThreadState;


namespace Bloom.web
{
	public abstract class ServerBase : IDisposable
	{
		private readonly HttpListener _listener;
		private readonly Thread _listenerThread;
		private readonly Thread[] _workers;
		private readonly ManualResetEvent _stop, _ready;
		private readonly Queue<HttpListenerContext> _queue;
		private bool _isDisposing;

		protected ServerBase()
		{
			// limit the number of worker threads to the number of processor cores
			_workers = new Thread[Environment.ProcessorCount];
			_queue = new Queue<HttpListenerContext>();
			_stop = new ManualResetEvent(false);
			_ready = new ManualResetEvent(false);
			_listener = new HttpListener();
			_listenerThread = new Thread(HandleRequests);
		}

#if DEBUG
		/// <summary/>
		~ServerBase()
		{
			Dispose(false);
		}
#endif

		public void StartWithSetupIfNeeded()
		{
			Exception error;
			StartWithSetupIfNeeded(out error);
		}

		protected virtual bool StartWithSetupIfNeeded(out Exception error)
		{
			var didStart = StartWithExceptionHandling(out error);
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
			_listener.AuthenticationSchemes = AuthenticationSchemes.Anonymous;
			_listener.Prefixes.Add(PathEndingInSlash);
			_listener.Start();

			_listenerThread.Start();

			for (var i = 0; i < _workers.Length; i++)
			{
				_workers[i] = new Thread(WaitForRequests);
				_workers[i].Start();
			}

			return GetIsAbleToUsePort();
		}

		private void HandleRequests()
		{
			while (_listener.IsListening)
			{
				var context = _listener.BeginGetContext(ContextReady, null);

				if (0 == WaitHandle.WaitAny(new[] { _stop, context.AsyncWaitHandle }))
					return;
			}
		}

		private void ContextReady(IAsyncResult ar)
		{
			// this can happen when shutting down
			if (!_listenerThread.IsAlive) return;

			lock (_queue)
			{
				_queue.Enqueue(_listener.EndGetContext(ar));
				_ready.Set();
			}
		}

		/// <summary>
		/// The worker threads run this function
		/// </summary>
		private void WaitForRequests()
		{
			// _ready: indicates that there are requests in the queue that should be processed.
			// _stop:  indicates that the class is being disposed and the thread should terminate.
			WaitHandle[] wait = { _ready, _stop };

			// WaitHandle.WaitAny(wait) returns the array index of the handle that satisfied the wait. 
			while (WaitHandle.WaitAny(wait) == 0)
			{
				HttpListenerContext context;
				lock (_queue)
				{
					if (_queue.Count > 0)
						context = _queue.Dequeue();
					else
					{
						_ready.Reset();
						continue;
					}
				}

				var rawurl = "unknown";
				try
				{
					rawurl = context.Request.RawUrl;
					MakeReply(new RequestInfo(context));
				}
				catch (HttpListenerException e)
				{
					// http://stackoverflow.com/questions/4801868/c-sharp-problem-with-httplistener
					Logger.WriteEvent("At ServerBase: ListenerCallback(): HttpListenerException, which may indicate that the caller closed the connection before we could reply. msg=" + e.Message);
					Logger.WriteEvent("At ServerBase: ListenerCallback(): url=" + rawurl);
				}
				catch (Exception error)
				{
					Logger.WriteEvent("At ServerBase: ListenerCallback(): msg=" + error.Message);
					Logger.WriteEvent("At ServerBase: ListenerCallback(): url=" + rawurl);
					Logger.WriteEvent("At ServerBase: ListenerCallback(): stack=");
					Logger.WriteEvent(error.StackTrace);
#if DEBUG
					throw;
#endif
				}
			}
		}

		protected virtual bool ProcessRequest(IRequestInfo info)
		{
			// process request for directory index
			var requestedPath = info.LocalPathWithoutQuery.Substring(7);
			if (info.RawUrl.EndsWith("/") && (Directory.Exists(requestedPath)))
			{
				info.WriteError(403, "Directory listing denied");
				return true;
			}

			if (requestedPath.EndsWith("testconnection"))
			{
				info.WriteCompleteOutput("OK");
				return true;
			}
			return false;
		}

		/// <summary>
		/// This is designed to be easily unit testable by not taking actual HttpContext, but doing everything through this IRequestInfo object
		/// </summary>
		/// <param name="info"></param>
		internal void MakeReply(IRequestInfo info)
		{
			if (!ProcessRequest(info))
			{
				ReportMissingFile(info);
			}
		}

		private void ReportMissingFile(IRequestInfo info)
		{
			Logger.WriteEvent("**{0}: File Missing: {1}", GetType().Name, GetLocalPathWithoutQuery(info));
			info.WriteError(404);
		}

		protected static string GetLocalPathWithoutQuery(IRequestInfo info)
		{
			var r = info.LocalPathWithoutQuery;
			const string slashBloomSlash = "/bloom/";
			if (r.StartsWith(slashBloomSlash))
				r = r.Substring(slashBloomSlash.Length);
			r = r.Replace("%3A", ":");
			r = r.Replace("%20", " ");
			r = r.Replace("%27", "'");
			return r;
		}

		public static string PathEndingInSlash
		{
			get { return "http://localhost:8089/bloom/"; }
		}

		public static string GetContentType(string extension)
		{
			switch (extension)
			{
				case ".css": return "text/css";
				case ".gif": return "image/gif";
				case ".htm":
				case ".html": return "text/html";
				case ".jpg":
				case ".jpeg": return "image/jpeg";
				case ".js": return "application/x-javascript";
				case ".png": return "image/png";
				case ".pdf": return "application/pdf";
				case ".txt": return "text/plain";
				case ".svg": return "image/svg+xml";
				default: return "application/octet-stream";
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
					@"Almost there!", MessageBoxButtons.OK);

			var startInfo = new ProcessStartInfo
			{
				UseShellExecute = true,
				Verb = "runas",
				FileName = "netsh",
				Arguments = string.Format("http add urlacl url={0} user=everyone", PathEndingInSlash.TrimEnd('/'))
			};

			Process.Start(startInfo);
		}

		private static bool GetIsAbleToUsePort()
		{
			try
			{
				var x = new WebClientWithTimeout { Timeout = 3000 };
				return ("OK" == x.DownloadString(PathEndingInSlash + "testconnection"));
			}
			catch (Exception e)
			{
				return false;
			}
		}

		#region Disposable stuff

		private bool IsDisposed { get; set; }

		public void Dispose()
		{
			//Stop();
			Dispose(true);
			GC.SuppressFinalize(this);
		}

		protected virtual void Dispose(bool fDisposing)
		{
			Debug.WriteLineIf(!fDisposing, "****** Missing Dispose() call for " + GetType() + ". *******");
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

						if (_listenerThread.ThreadState != ThreadState.Unstarted)
							_listenerThread.Join();

						foreach (var worker in _workers.Where(worker => (worker != null) && (worker.ThreadState != ThreadState.Unstarted)))
						{
							worker.Join();
						}

						_listener.Stop();
						_listener.Close();
					}
				}
				// ReSharper disable once RedundantCatchClause
				catch (Exception e)
				{
					//prompted by the mysterious BL 273, Crash while closing down the imageserver
#if DEBUG
					throw;
#else				//just quitely report this
					DesktopAnalytics.Analytics.ReportException(e);
#endif
				}
			}
			IsDisposed = true;
		}

		#endregion
	}
}
