using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using SIL.IO;

namespace Bloom.MiscUI
{
	/// <summary>
	/// This class is responsible for various dialogs we sometimes show at startup
	/// and other tasks we do early on but after the system is at least briefly idle.
	/// Some of these are also done when switching collections. In fact, it can be used
	/// any time we want several things to be done at idle time in a specified order.
	/// Currently manages
	/// - Splash screen
	/// - Notifying the 'fast splash screen' program typically used to launch Bloom
	///   that it is no longer needed.
	/// - Dialog asking user to register
	/// - Dialog asking whether to auto-update
	/// - TeamCollection sync progress dialog (partly)
	/// - Several stages of 'idle' work to initialize LibraryListView (the book icons
	///   in the collection tab)
	/// Complications:
	/// - TC progress dialog can't be postponed (at least without major rework after
	/// it completes)
	/// - Splash screen must be hidden before any other dialog shows (typically under it).
	/// The splash screen is shown as early as possible and until we are done with
	/// these idle tasks or a minimum time elapses (whichever comes second) or
	/// until we want to show some other UI that it would interfere with.
	/// 
	/// </summary>
	public class StartupScreenManager
	{
		// The main collection of actions, to be done one at a time roughly in priority order
		// when the system is idle. (Currently, we only distinguish high and low priorities.)
		private static List<StartupAction> _startupActions = new List<StartupAction>();
		// The one, if any, that we are doing now. (Its action is currently invoked but
		// has not yet returned.)
		private static StartupAction _current;

		private static DateTime _earliestWeShouldCloseTheSplashScreen;
		private static SplashScreen _splashForm;

		private static Action _doWhenSplashScreenShouldClose;

		public static void StartManaging()
		{
			_earliestWeShouldCloseTheSplashScreen = DateTime.Now.AddSeconds(3);
			_splashForm = SplashScreen.CreateAndShow();//warning: this does an ApplicationEvents()

			// Bloom is usually launched by a tiny EXE that displays a splash screen faster
			// than Bloom itself possibly could. As soon as we have our real splash screen up,
			// we want to take the next opportunity to get rid of it.
			AddStartupAction( () => CloseFastSplashScreen());
			Application.Idle += DoStartupAction;
		}

		/// <summary>
		/// The specified action should be done when the system is idle and any previous
		/// tasks added have completed (subject to priority)
		/// </summary>
		/// <param name="task">The thing to do</param>
		/// <param name="shouldHideSplashScreen">If true, the splash screen will be hidden
		/// before starting the task, typically because it runs a dialog itself</param>
		/// <param name="lowPriority">Tasks where this is true are postponed until all
		/// tasks where it is false have completed, even if the higher priority tasks
		/// were added later.</param>
		/// <param name="needsToRun"> If not null, the task will run from zero to many times,
		/// until needsToRun returns false, before any subsequent tasks.</param>
		/// <returns></returns>
		public static IStartupAction AddStartupAction(Action task, bool shouldHideSplashScreen = false, bool lowPriority = false, Func<bool> needsToRun = null)
		{
			var startupAction = new StartupAction()
				{ Priority = lowPriority ? StartupActionPriority.low : StartupActionPriority.high,
					ShouldHideSplashScreen = shouldHideSplashScreen, Task = task, NeedsToRun = needsToRun};
			_startupActions.Add(startupAction);
			EnableProcessing();
			return startupAction;
		}

		/// <summary>
		/// The specified action (typically, closing a progress dialog) should be done
		/// when the system would otherwise have closed the splash screen (which
		/// typically was closed to make way for the progress dialog).
		/// This action may therefore be performed when all startup tasks complete
		/// and the standard delay has passed, or it may be performed sooner if
		/// we are about to start another task that requires the splash screen to
		/// be closed.
		/// </summary>
		public static void DoWhenSplashScreenShouldClose(Action doWhat)
		{
			if (_startupActions.FirstOrDefault(a => a.Enabled) == null
			    && DateTime.Now > _earliestWeShouldCloseTheSplashScreen)
			{
				doWhat(); // already at the point where we would normally close it, just do immediately
			}
			else
			{
				Debug.Assert(_doWhenSplashScreenShouldClose == null,
					"can't handle more than one action to do when splash screen would close");
				_doWhenSplashScreenShouldClose = doWhat; // save to do when the time for closing comes.
			}
		}

		// This is for one thing the Program's Main method wants to do when the splash
		// screen is closed. I'd rather it was just one of the actions, but we don't
		// normally close the splash screen until there are no actions left, and it
		// would take another priority setting to make it the very last...this special
		// case is less trouble.
		// It is NOT done when the splash screen is forcibly hidden to make way
		// for another window (since the current usage is to try to get Bloom's main
		// window front and focused).
		public static Action DoLastOfAllAfterClosingSplashScreen;

		/// <summary>
		/// Where it all happens. This method, invoked as a handler of Application.Idle,
		/// picks one of _startupActions to run next, if one is not already running.
		/// They are done in the order added, except that high priority ones go first.
		/// If there are none, and the minimum time has expired, it closes the splash screen.
		/// </summary>
		private static void DoStartupAction(object sender, EventArgs e)
		{
			if (_current != null)
			{
				// got Idle event while startup action in progress. Wait till it finishes.
				return;
			}
			_current = _startupActions.FirstOrDefault(a => a.Enabled && a.Priority == StartupActionPriority.high);
			if (_current == null)
				_current = _startupActions.FirstOrDefault(a => a.Enabled);
			if (_current == null)
			{
				if (DateTime.Now > _earliestWeShouldCloseTheSplashScreen)
				{
					CloseSplashScreen();
					// We can stop running altogether until some new action gets added or enabled.
					Application.Idle -= DoStartupAction;
				}
				// The only way we could want it again is if some task that ShouldHideSplashScreen
				// is still in the list, and later gets enabled. But currently no such tasks ever
				// get disabled. So if there are no current tasks, we no longer need it.
				_splashForm = null;
				return;
			}

			if (_current.ShouldHideSplashScreen && _splashForm != null)
			{
				_splashForm.Hide();
				_splashForm = null; // it's gone, not needed again
			}

			if (_current.NeedsToRun != null)
			{
				if (_current.NeedsToRun())
				{
					_current.Task();
					_current = null;  // NOT done, but will be found again.
					return;
				}
				else
				{
					ConsiderCurrentTaskDone();
					return;
				}
			}

			_current.Task();
			ConsiderCurrentTaskDone();
		}

		public static void PutSplashAbove(Form aboveThis)
		{
			if (_splashForm != null)
			{
				_splashForm.StayAboveThisWindow(aboveThis);
			}
		}

		/// <summary>
		/// Treat the current task as done, so we can start another. Typically this is
		/// called when its task() returns, but in one case we want to keep a dialog
		/// launched by the task showing (the Team Collection sync progress dialog)
		/// while other tasks continue. Since idle events happen while running the
		/// dialog, we can go on with the next task as long as we know the one for which
		/// the progress dialog was launched is complete.
		/// </summary>
		public static void ConsiderCurrentTaskDone()
		{
			if (_current == null)
				return;
			_startupActions.Remove(_current);
			_current = null;
		}

		/// <summary>
		/// If Bloom was launched by a fast splash screen program, signal it to close.
		/// </summary>
		private static void CloseFastSplashScreen()
		{
			if (SIL.PlatformUtilities.Platform.IsLinux)
			{
				RobustFile.Delete("/tmp/BloomLaunching.now"); // (okay if file doesn't exist)
			}
			else if (SIL.PlatformUtilities.Platform.IsWindows)
			{
				// signal the native process (that launched us) to close the splash screen
				// (okay if there's nobody there to receive the signal)
				using (var closeSplashEvent = new EventWaitHandle(false,
					EventResetMode.ManualReset, "CloseSquirrelSplashScreenEvent"))
				{
					closeSplashEvent.Set();
				}
			}
		}

		public static void CloseSplashScreen()
		{
			if (_splashForm != null)
			{
				_splashForm.FadeAndClose(); //it's going to hang around while it fades,
			}
			_doWhenSplashScreenShouldClose?.Invoke();
			DoLastOfAllAfterClosingSplashScreen?.Invoke();
		}

		public static void EnableProcessing()
		{
			Application.Idle += DoStartupAction;
		}

		enum StartupActionPriority
		{
			high,
			low
		}

		/// <summary>
		/// One item in our queue. (Since the class is private, the only members
		/// available outside StartupScreenManager are those in IStartupAction.)
		/// </summary>
		private class StartupAction : IStartupAction
		{
			public Action Task;

			// when non-null, Task needs to run, possibly repeatedly, as long as
			// NeedsToRun returns true.
			public Func<bool> NeedsToRun;

			public bool Enabled
			{
				get => _enabled;
				set
				{
					_enabled = value;
					// In case we ran out of tasks and shut down, we need to get going again.
					// (It's OK if this task is already done. We'll just run one cycle, find no
					// tasks, and shut down again.)
					StartupScreenManager.EnableProcessing();
				}
			}

			public StartupActionPriority Priority;
			public bool ShouldHideSplashScreen;
			private bool _enabled = true;
		}
	}

	public interface IStartupAction
	{
		bool Enabled { get; set; }
	}
}
