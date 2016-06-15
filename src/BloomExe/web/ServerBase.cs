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
using L10NSharp;
using SIL.Code;
using SIL.Reporting;
using ThreadState = System.Threading.ThreadState;


namespace Bloom.web
{
	public abstract class ServerBase : IDisposable
	{
		/// <summary>
		/// Prefix we add to http://localhost:8089 in all our urls. I'm not sure why...possibly to guard against
		/// some other program trying to use this port?
		/// </summary>
		internal const string BloomUrlPrefix = "/bloom/";

		/// <summary>
		/// Listens for requests on "http://localhost:8089/bloom/"
		/// </summary>
		private HttpListener _listener;

		/// <summary>
		/// Requests that come into the _listener are placed in the _queue so they can be processed
		/// </summary>
		private readonly Queue<HttpListenerContext> _queue;

		/// <summary>
		/// Some requests which may be made to the server require other requests to be initiated
		/// and completed before the original request can be completed. Currently there is one
		/// example of this kind of request, when the server is asked for a thumbnail (image) and needs
		/// to create a new thumbnail. Creating the thumbnail involves a browser navigating to
		/// the HTML that represents the page. That html contains requests to the server.
		///
		/// If multiple thumbnails are requested as a group (currently likely in the Add Page dialog),
		/// there is a danger of getting in a situation where all the threads are busy trying to
		/// retrieve (and hence create) thumbnails, so no threads are available to service the requests
		/// of the browser that is trying to navigate to the appropriate page to create the thumbnail.
		/// This is effectively a deadlock; the thumbnail-creation-navigation times-out and we
		/// don't get a thumbnail.
		///
		/// I have chosen to designate such requests as 'recursive' in the sense that a recursive
		/// request is one that initiates other requests to the server in the course of producing
		/// its result. We keep track of the number of recursive requests that are under way,
		/// and spin up additional threads if we don't have at least a couple that are not tied up
		/// with recursive requests.
		///
		/// This variable should only be accessed or modified inside a lock of _queue. It is the actual
		/// count of threads currently performing recursive requests (that is, it counts the threads
		/// that are processing contexts for which IsRecursiveRequestContext() returns true).
		/// </summary>
		private int _threadsDoingRecursiveRequests;

		/// <summary>
		/// Gets requests from _listener and puts them in the _queue to be processed
		/// </summary>
		private readonly Thread _listenerThread;

		/// <summary>
		/// Pool of threads that pull a request from the _queue and processes it
		/// </summary>
		private readonly List<Thread> _workers = new List<Thread>();

		/// <summary>
		/// Notifies threads that they should stop because the ServerBase object is being disposed
		/// </summary>
		private readonly ManualResetEvent _stop;

		/// <summary>
		/// Notifies threads in the _workers pool that there is a request in the _queue
		/// </summary>
		private readonly ManualResetEvent _ready;
		

		protected ServerBase()
		{
			// limit the number of worker threads to the number of processor cores
			// (but at least 2, since we sometimes need one thread to be free to help
			// complete the request that another one is executing...see EnhancedImageServer.FindOrGenerateImage)
			//_workers = new Thread[Math.Max(Environment.ProcessorCount, 2)];
			_queue = new Queue<HttpListenerContext>();
			_stop = new ManualResetEvent(false);
			_ready = new ManualResetEvent(false);
			_listenerThread = new Thread(HandleRequests);
		}

#if DEBUG
		/// <summary/>
		~ServerBase()
		{
			Dispose(false);
		}
#endif

		#region Startuup

		public void StartListening()
		{
			StartWithSetupIfNeeded();
		}

		protected virtual void StartWithSetupIfNeeded()
		{
			try
			{
				TryStart();
			}
			catch (Exception)
			{
				if (!SIL.PlatformUtilities.Platform.IsWindows) throw;

				AddUrlAccessControlEntry();
				try
				{
					TryStart();
				}
				catch(Exception e)
				{
					ErrorReport.NotifyUserOfProblem(GetServerStartFailureMessage());
					Application.Exit();
				}
			}
		}
		private static string GetServerStartFailureMessage()
		{
			var zoneAlarm = false;
			if (SIL.PlatformUtilities.Platform.IsWindows)
			{
				zoneAlarm =
					Directory.Exists(Path.Combine(Environment.GetFolderPath(System.Environment.SpecialFolder.ProgramFilesX86),
						"CheckPoint/ZoneAlarm")) ||
					Directory.Exists(Path.Combine(Environment.GetFolderPath(System.Environment.SpecialFolder.ProgramFiles),
						"CheckPoint/ZoneAlarm"));

				if (!zoneAlarm)
				{
					zoneAlarm = Process.GetProcesses().Any(p => p.Modules.Cast<ProcessModule>().Any(m => m.ModuleName.Contains("ZoneAlarm")));
				}
			}
			if (zoneAlarm)
			{
				return LocalizationManager.GetString("Errors.ZoneAlarm",
					"Bloom cannot start properly, and this symptom has been observed on machines with ZoneAlarm installed. Note: disabling ZoneAlarm does not help. Nor does restarting with it turned off. Something about the installation of ZoneAlarm causes the problem, and so far only uninstalling ZoneAlarm has been shown to fix the problem.");
			}

			return LocalizationManager.GetString("Errors.CannotConnectToBloomServer",
				"Bloom was unable to start its own HTTP listener that it uses to talk to its embedded Firefox browser. If this happens even if you just restarted your computer, then ask someone to investigate if you have an aggressive firewall product installed, which may need to be uninstalled before you can use Bloom.");
		}
		private void TryStart()
		{
			_listener = new HttpListener {AuthenticationSchemes = AuthenticationSchemes.Anonymous};
			_listener.Prefixes.Add(PathEndingInSlash);
			_listener.Start();

			_listenerThread.Start();

			for (var i = 0; i < Math.Max(Environment.ProcessorCount, 2); i++)
			{
				SpinUpAWorker();
			}

			GetIsAbleToUsePort();
		}

		// After the initial startup, this should only be called inside a lock(_queue),
		// to avoid race conditions modifying the _workers collection.
		private void SpinUpAWorker()
		{
			_workers.Add(new Thread(RequestProcessorLoop));
			_workers.Last().Name = "Server thread " + _workers.Count;
			_workers.Last().Start();
		}

		#endregion

		/// <summary>
		/// The _listenerThread runs this method, and exits when the _stop event is raised
		/// </summary>
		private void HandleRequests()
		{
			while (_listener.IsListening)
			{
				var context = _listener.BeginGetContext(QueueRequest, null);

				if (0 == WaitHandle.WaitAny(new[] { _stop, context.AsyncWaitHandle }))
					return;
			}
		}

		/// <summary>
		/// This method is called in the _listenerThread when we obtain an HTTP request from
		/// the _listener, and queues it for processing by a worker.
		/// </summary>
		/// <param name="ar"></param>
		private void QueueRequest(IAsyncResult ar)
		{
			// this can happen when shutting down
			// BL-2207 indicates it may be possible for the thread to be alive and the listener closed,
			// although the only way I know it gets closed happens after joining with that thread.
			// Still, one more check seems worthwhile...if we're far enough along in shutting down
			// to have closed the listener we certainly can't respond to any more requests.
			if (!_listenerThread.IsAlive || !_listener.IsListening)
				return;

			lock (_queue)
			{
				_queue.Enqueue(_listener.EndGetContext(ar));
				_ready.Set();
			}
		}

		/// <summary>
		/// Return true if producing the result requested by the context may involve
		/// making additional requests to the server. (See the fuller discussion on
		/// the declaration of _threadsDoingRecursiveRequests.)
		/// </summary>
		/// <param name="context"></param>
		/// <returns></returns>
		protected virtual bool IsRecursiveRequestContext(HttpListenerContext context)
		{
			return false;
		}

		/// <summary>
		/// The worker threads run this function
		/// </summary>
		private void RequestProcessorLoop()
		{
			// _ready: indicates that there are requests in the queue that should be processed.
			// _stop:  indicates that the class is being disposed and the thread should terminate.
			WaitHandle[] wait = { _ready, _stop };

			// Wait until a request is ready or the thread is being stopped. The WaitAny will return 0 (the index of
			// _ready in the wait array) if a request is ready, and 1 when _stop is signaled, breaking us out of the loop.
			while (WaitHandle.WaitAny(wait) == 0)
			{
				HttpListenerContext context;
				bool isRecursiveRequestContext; // needs to be declared outside the lock but initialized afte we have the context.
				lock (_queue)
				{
					if (_queue.Count > 0)
					{
						context = _queue.Dequeue();
					}
					else
					{
						_ready.Reset();
						continue;
					}
					isRecursiveRequestContext = IsRecursiveRequestContext(context);
					if (isRecursiveRequestContext)
					{
						_threadsDoingRecursiveRequests++;
						// We've got to have some threads not doing recursive tasks.
						// One non-recursive thread is probably enough to prevent deadlock but some of those
						// threads are probably reading files so having a few of them
						// is likely to speed up the recursive task.
						if (_threadsDoingRecursiveRequests > _workers.Count - 3)
							SpinUpAWorker();
					}
				}

				var rawurl = "unknown";
				try
				{
					rawurl = context.Request.RawUrl;

					// set lower priority for thumbnails in order to have less impact on the UI thread
					if (rawurl.Contains("thumbnail=1"))
						Thread.CurrentThread.Priority = ThreadPriority.BelowNormal;

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
#if __MonoCS__
					// Something keeps closing the socket connection prematurely on Linux/Mono.  But I'm not sure
					// it's an important failure since the program appears to work okay, so we'll ignore the error.
					if (error is IOException && error.InnerException != null && error.InnerException is System.Net.Sockets.SocketException)
					{
						Logger.WriteEvent("At ServerBase: ListenerCallback(): IOException/SocketException, which may indicate that the caller closed the connection before we could reply. msg=" + error.Message + " / " + error.InnerException.Message);
						Logger.WriteEvent("At ServerBase: ListenerCallback(): url=" + rawurl);
					}
					else
#endif
					{
						Logger.WriteEvent("At ServerBase: ListenerCallback(): msg=" + error.Message);
						Logger.WriteEvent("At ServerBase: ListenerCallback(): url=" + rawurl);
						Logger.WriteEvent("At ServerBase: ListenerCallback(): stack=");
						Logger.WriteEvent(error.StackTrace);
#if DEBUG
						//NB: "throw" here makes it impossible for even the programmer to continue and try to see how it happens
						Debug.Fail("(Debug Only) "+error.Message);
#endif
					}
				}
				finally
				{
					Thread.CurrentThread.Priority = ThreadPriority.Normal;
					if (isRecursiveRequestContext)
					{
						lock (_queue)
						{
							_threadsDoingRecursiveRequests--;
						}
					}
				}
			}
		}

		/// <summary>
		/// This method is overridden in classes inheriting from this class to handle specific request types
		/// </summary>
		/// <param name="info"></param>
		/// <returns></returns>
		protected virtual bool ProcessRequest(IRequestInfo info)
		{
#if MEMORYCHECK
			// Check memory for the benefit of developers.  (Also see all requests as a side benefit.)
			var debugMsg = String.Format("ServerBase.ProcessRequest(\"{0}\"", info.RawUrl);
			SIL.Windows.Forms.Reporting.MemoryManagement.CheckMemory(true, debugMsg, false);
#endif
			// process request for directory index
			var requestedPath = GetLocalPathWithoutQuery(info);
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

		protected internal static string CorrectedLocalPath(IRequestInfo info)
		{
			// Note that LocalPathWithoutQuery removes all % escaping from the URL.
			return CorrectedLocalPath(info.LocalPathWithoutQuery, info.RawUrl);
		}

		internal static string CorrectedLocalPath (string localPathWithoutQuery, string rawUrl)
		{

			// for some reason Url.LocalPath strips out two of the three slashes that we get
			// with network drive paths when we stick /bloom/ (or /bloom/something/) in front
			// of a path like //mydrive/myfolder/...
			var idx = rawUrl.IndexOf("///");
			if (idx > 0 && localPathWithoutQuery.StartsWith(BloomUrlPrefix))
				return localPathWithoutQuery.Substring(0, idx) + "//" + localPathWithoutQuery.Substring(idx);
			return localPathWithoutQuery;
		}

		/// <summary>
		/// This method allows getting the actual local path that the image server would retrieve given a Bloom URL
		/// that ends up at a local file. For now it is mainly useful for things in the book folder; it doesn't have
		/// all the smarts to locate files shipped with the application, it is just concerned with reversing
		/// the various tricks we use to encode paths as URLs.
		/// </summary>
		/// <param name="url"></param>
		/// <returns></returns>
		public static string GetLocalPathWithoutQuery(string url)
		{
			var uri = new Uri(url);
			return GetLocalPathWithoutQuery(RequestInfo.GetLocalPathWithoutQuery(uri), url);
		}

		protected internal static string GetLocalPathWithoutQuery(IRequestInfo info)
		{
			return GetLocalPathWithoutQuery(info.LocalPathWithoutQuery, info.RawUrl);
		}

		internal static string GetLocalPathWithoutQuery(string localPathWithoutQuery, string rawUrl)
		{
			var localPath = CorrectedLocalPath(localPathWithoutQuery, rawUrl);
			if (localPath.StartsWith(BloomUrlPrefix))
				localPath = localPath.Substring(BloomUrlPrefix.Length);
			if (localPath.Contains("?") && !File.Exists(localPath))
			{
				var idx = localPath.LastIndexOf("?", StringComparison.Ordinal);
				var temp = localPath.Substring(0, idx);
				if (localPath.EndsWith("?thumbnail=1") || File.Exists(localPath))
					return temp;
			}
			return localPath;
		}

		public static string PathEndingInSlash
		{
			get { return "http://localhost:8089" + BloomUrlPrefix; }
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

		private static void GetIsAbleToUsePort()
		{
			var x = new WebClientWithTimeout { Timeout = 3000 };
			if ("OK" != x.DownloadString(PathEndingInSlash + "testconnection"))
			{
				ErrorReport.NotifyUserOfProblem(GetServerStartFailureMessage());
				Application.Exit();;
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
					if (_listener != null)
					{
						//prompted by the mysterious BL 273, Crash while closing down the imageserver
						Guard.AgainstNull(_listenerThread, "_listenerThread");
						//prompted by the mysterious BL 273, Crash while closing down the imageserver
						Guard.AgainstNull(_stop, "_stop");

						// tell _listenerThread and the worker threads they should stop
						_stop.Set();
						
						// wait for _listenerThread to stop
						if (_listenerThread.ThreadState != ThreadState.Unstarted)
							_listenerThread.Join();

						// wait for each worker thread to stop
						foreach (var worker in _workers.Where(worker => (worker != null) && (worker.ThreadState != ThreadState.Unstarted)))
						{
							worker.Join();
						}

						// stop listening for incoming http requests
						Debug.Assert(_listener.IsListening);
						if(_listener.IsListening)
						{
							//In BL-3290, a user quitely failed here each time he exited Bloom, with a Cannot access a disposed object.
							//according to http://stackoverflow.com/questions/11164919/why-httplistener-start-method-dispose-stuff-on-exception,
							//it's actually just responding to being closed, not disposed.
							//I don't know *why* for that user the listener was already stopped.
							_listener.Stop();
						}
						//if we keep getting that exception, we could move the Close() into the previous block
						_listener.Close();
						_listener = null;
					}
				}
				// ReSharper disable once RedundantCatchClause
				catch (Exception e)
				{
					//prompted by the mysterious BL 273, Crash while closing down the imageserver
#if DEBUG
					throw;
#else             //just quietly report this
					DesktopAnalytics.Analytics.ReportException(e); 
#endif
				}
			}
			IsDisposed = true;
		}

		#endregion
	}
}
