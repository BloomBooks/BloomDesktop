using System;
using System.Diagnostics;
using System.Windows.Forms;
using SIL.Code;

namespace Bloom.ToPalaso
{
	class SafeInvoke
	{
		/// <summary>
		/// Invoke and action safely even if called from the background thread.
		/// </summary>
		/// <remarks>
		/// Invoking on the ui thread from background threads works *most* of the time, with occasional crash.
		/// Stackoverflow has a good collection of people trying to deal with these corner cases, where
		/// InvokeRequired(), for example, is unreliable (it doesn't tell you if the control hasn't even
		/// got a handle yet).
		/// SIL.Core (lipalaso) has a SafeInvoke on its LogBox control, which sees heavy background/foreground interaction
		/// and seems to work well over the course of years.
		/// I think I (JH) wrote that, but it relies on a couple of odd things:
		/// 1) it calls IsHandleCreated() (which can reportedly create the handle on the wrong thread?)
		/// and 2) uses SynchronizationContext which works for that single control case but I'm not seeing how
		/// to generalize that.
		/// So now I'm trying something more mainstream here, from a highly voted SO answer.
		/// </remarks>
		public static void Invoke(string nameForErrorReporting, Control control, bool forceSynchronous, bool throwIfAnythingGoesWrong, Action action)
		{
			Guard.AgainstNull(control, nameForErrorReporting); // throw this one regardless of the throwIfAnythingGoesWrong

			//mostly following http://stackoverflow.com/a/809186/723299
			try
			{
				if (control.IsDisposed)
				{
					throw new ObjectDisposedException("Control is already disposed. (" + nameForErrorReporting + ")");
				}
				if (control.InvokeRequired)
				{
					var delgate = (Action)delegate { Invoke(nameForErrorReporting, control, forceSynchronous, throwIfAnythingGoesWrong, action); };
					if (forceSynchronous)
					{
						control.Invoke(delgate);
					}
					else
					{
						control.BeginInvoke(delgate);
					}
				}
				else
				{
					// InvokeRequired will return false if the control isn't set up yet
					if (!control.IsHandleCreated)
					{
						//This situation happened in BL-2918, prompting the introduction of this safeinvoke
						throw new ApplicationException("SafeInvoke.Invoke apparently called before control created ("+ nameForErrorReporting+")");

						//note, resist the temptation to work around this by just making the handle be created with something like
						//var unused = control.Handle
						//I've read a rumour that this can create the handle "on the wrong thread".
						//At a minimum, we would need to investigate the truth of that before using it here.
					}
					action();
				}
			}
			catch (Exception error)
			{
				if (throwIfAnythingGoesWrong)
					throw;
				else
				{
					Debug.Fail("This error would be swallowed in release version: " + error.Message);
					SIL.Reporting.Logger.WriteEvent("**** "+error.Message);
				}
			}
		}

		/// <summary>
		/// This version just makes it clear that the call is permissive, won't bother the user
		/// if something goes wrong, which is appropriate for
		/// many background-->foreground ui tasks, like refreshing.
		/// </summary>
		public static void InvokeIfPossible(string nameForErrorReporting, Control control, bool forceSynchronous, Action action)
		{
			Invoke(nameForErrorReporting, control, forceSynchronous, false, action);
		}
	}
}
