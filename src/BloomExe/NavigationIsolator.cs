using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using Bloom.Book;
using Gecko;
using Timer = System.Windows.Forms.Timer;
using Gecko.Events;
using SIL.IO;

namespace Bloom
{
	/// <summary>
	/// This class is used to ensure that only one instance of GeckoWebBrowser is allowed to navigate at a time.
	/// We don't know for sure that this constraint is (or always will be) necessary, but at least as long as we're
	/// doing Application.DoEvents() in various places it's probably a good idea. See BL-77.
	/// It is designed for dependency-injection as an application-wide singleton.
	/// To achieve the purpose, all users of GeckoWebBrowser.Navigate must make use of the Navigate() or NavigateIfIdle()
	/// methods of this class to perform navigation.
	/// This class will serialize the navigations. Each navigation should occur eventually, except
	/// that if navigation is already in progress, NavigateIfIdle will just return false. This is intended for
	/// Idle loop tasks which should simply be attempted again later if the system is not really idle (since a
	/// navigation is in progress).
	///
	/// Doing all this is challenging because the various possible events that might signal that navigation
	/// is complete are not reliable. Moreover it seems that the IsBusy flag (ReadyState, in later versions of Gecko)
	/// is also not reliable, and sometimes indicates that a browser is busy long after it has finished doing
	/// anything we recognize. We therefore use a timer to make sure we notice pretty soon if IsBusy becomes false
	/// without any of the events being raised. We will also notice at once that IsBusy has become false (or the target
	/// browser has been disposed) if a new request is received. As a last resort, rather than freeze the program
	/// or even the thumbnailing forever, if two seconds goes by and the browser is still busy doing one navigation
	/// we give up and forget that one and allow others to proceed.
	/// </summary>
	public class NavigationIsolator
	{
		List<NavigationTask> _pending = new List<NavigationTask>();
		private NavigationTask _current;
		private Timer _timer;
		private DateTime _startNavigation;

		/// <summary>
		/// Navigate the specified browser to the specified url as soon as it is safe to do so (that is,
		/// immediately or when all other navigations we know about that were started sooner have completed).
		/// Must be called on UI thread.
		/// </summary>
		/// <param name="browser"></param>
		/// <param name="url"></param>
		public void Navigate(GeckoWebBrowser browser, string url)
		{
			if (browser.InvokeRequired)
				throw new Exception("Navigation should only be done on the main UI thread");
			Navigate(new IsolatedBrowser(browser), url);
		}

		internal void Navigate(IIsolatedBrowser browser, string url)
		{
			var task = new NavigationTask() { Browser = browser, Url = url };
			if (_current == null)
			{
				StartTask(task);
			}
			else if (!_current.Browser.IsBusy)
			{
				Cleanup();
				StartTask(task);
			}
			else
			{
				// Check whether we already have a pending navigation for this browser.
				// If so, discard it since it's now irrelevant.  (It may also be impossible
				// if it was for a temporary file: see https://jira.sil.org/browse/BL-863.)
				for (int i = 0; i < _pending.Count; ++i)
				{
					if (browser.Equals(_pending[i].Browser))
					{
						_pending.RemoveAt(i);
						break;
					}
				}
				_pending.Add(task);
			}
		}

		/// <summary>
		/// If no browser is navigating, navigate normally to the specified address and return true.
		/// (Normal isolation will happen for any subsequent non-idle task started.)
		/// If some navigation is already happening, just return false. The navigation will not be queued to do later.
		/// Must be called on UI thread.
		/// </summary>
		/// <param name="browser"></param>
		/// <param name="url"></param>
		/// <returns></returns>
		public bool NavigateIfIdle(GeckoWebBrowser browser, string url)
		{
			if (browser.InvokeRequired)
				throw new Exception("Navigation should only be done on the main UI thread");
			return NavigateIfIdle(new IsolatedBrowser(browser), url);
		}

		internal bool NavigateIfIdle(IIsolatedBrowser browser, string url)
		{
			if (_current == null)
			{
				Navigate(browser, url);
				return true;
			}
			return false;
		}

		private void CleanupTimer()
		{
			if (_timer == null)
				return;
			_timer.Stop();
			_timer.Dispose();
			_timer = null;
		}

		private void StartTask(NavigationTask task)
		{
			_current = task;
			_startNavigation = DateTime.Now;
			_current.Browser.InstallEventHandlers();
			_current.Browser.Navigated += BrowserOnDocumentCompleted;
			Debug.Assert(_timer == null, "We should have cleaned up any old timer before starting a new task");
			_timer = new Timer();
			_timer.Tick += (sender, args) =>
			{
				if (_current == null)
				{
					// We somehow got another tick after the need for the timer has passed. Get rid of it.
					CleanupTimer();
					return;
				}
				if (!_current.Browser.IsBusy)
				{
					// browser stopped being busy without notifying us! Pretend it did.
					BrowserOnDocumentCompleted(null, null);
				}
				if (DateTime.Now - _startNavigation > new TimeSpan(0, 0, 0, 2))
				{
					// Navigation is taking too long. Sometimes the main window stays busy forerver. Give up.
					// Enhance: may want to wait a bit longer if nothing is waiting...but remember idle tasks can still be frozen out.
					ForceDocumentCompleted();
				}
			};
			_timer.Interval = 100;
			_timer.Start();
			_current.Browser.Navigate(_current.Url);
		}

		private void BrowserOnDocumentCompleted(object sender, EventArgs eventArgs)
		{
			if (_current.Browser.IsBusy)
				return; // spurious notification
			ForceDocumentCompleted();
		}

		private void ForceDocumentCompleted()
		{
			Cleanup();
			while (_pending.Count > 0)
			{
				var task = _pending[0];
				_pending.RemoveAt(0);
				// If the file doesn't exist, calling _current.Browser.Navigate() results in the program
				// silently stopping on Linux.  This should never happen with the check made above for
				// superceded navigation requests before posting a pending navigation, but a race could
				// occur between the old file being deleted, the next pending request being handled, and
				// the new file being sent as a navigation requesting.  So we have this check as a backstop.
				// (I always believe in both belts and suspenders, don't you?)  This is part of the fix
				// for https://jira.sil.org/browse/BL-863.
				if (!task.Url.StartsWith("http://") && !RobustFile.Exists(task.Url))
				{
					Debug.Assert(false, String.Format(@"DEBUG: NavigationIsolator.ForceDocumentCompleted(): new file to display (""{0}"") does not exist!??", task.Url));
					continue;
				}
				StartTask(task);
				return;
			}
		}

		private void Cleanup()
		{
			if (_current == null) return;

			CleanupTimer();
			_current.Browser.RemoveEventHandlers();
			_current.Browser.Navigated -= BrowserOnDocumentCompleted;
			_current = null;
		}
	}

	internal class NavigationTask
	{
		public IIsolatedBrowser Browser;
		public string Url;
	}

	/// <summary>
	/// This interface wraps the GeckoWebBrowser functionality that NavigationIsolator cares about
	/// (primarily so that tests can stub it).
	/// </summary>
	internal interface IIsolatedBrowser
	{
		void Navigate(string url);
		// This summarizes the events that best help us detect navigation is complete.
		// We anticipate that they may be issued spuriously (e.g., when navigation is complete in an iframe,
		// or when the original document is loaded but JavaScript is still manipulating the DOM.
		event EventHandler Navigated;
		// We believe this (hopefully reliably) tells us that the browser is really ready for new navigation.
		bool IsBusy { get; }
		void InstallEventHandlers();
		void RemoveEventHandlers();
	}

	/// <summary>
	/// This class is the real implementation of IIsolatedBrowser.
	/// Keep this code as simple as possible since it can't adequately be tested.
	/// </summary>
	class IsolatedBrowser : IIsolatedBrowser
	{
		private GeckoWebBrowser _browser;
		public IsolatedBrowser(GeckoWebBrowser browser)
		{
			_browser = browser;
		}
		public void Navigate(string url)
		{
			_browser.Navigate(url);
		}

		private void RaiseNavigated(object sender, GeckoNavigatedEventArgs args)
		{
			if (Navigated != null)
				Navigated(this, new EventArgs());
		}

		private void RaiseDocumentCompleted(object sender, GeckoDocumentCompletedEventArgs args)
		{
			if (Navigated != null)
				Navigated(this, new EventArgs());
		}

		public event EventHandler Navigated;
		public bool IsBusy
		{
			// Todo: May need changes for Gecko29
			get { return !_browser.IsDisposed && _browser.IsBusy; }
		}

		public void InstallEventHandlers()
		{
			// Todo: May need changes for Gecko29
			_browser.Navigated += RaiseNavigated;
			_browser.DocumentCompleted += RaiseDocumentCompleted;
		}

		public void RemoveEventHandlers()
		{
			if (_browser.IsDisposed)
				return; // don't try to do anything to it.
			_browser.Navigated -= RaiseNavigated;
			_browser.DocumentCompleted -= RaiseDocumentCompleted;
		}

		/// <summary>
		/// Check whether the specified object is an IsolatedBrowser that points to the same GeckoWebBrowser underneath.
		/// </summary>
		public override bool Equals(object obj)
		{
			if (obj is IsolatedBrowser)
				return _browser == (obj as IsolatedBrowser)._browser;
			return false;
		}
	}
}
