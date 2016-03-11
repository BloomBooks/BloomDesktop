using System;
using System.Collections.Generic;
using System.Linq;
using SIL.Reporting;

namespace Bloom
{
	public enum ThrowIf { Alpha, Beta, Release }
	public enum InformIf { Alpha, Beta, Release }

	/// <summary>
	/// Provides a way to note a problem in the log and, depending on channel, notify the user.
	/// Enhance: wire up to a passive notification "toast" in the UI
	/// </summary>
	public class NonFatalProblem
	{
		/// <summary>
		/// Always log, possibly inform the user, possibly throw the exception
		/// </summary>
		/// <param name="whenToThrow">Will throw if the channel is this or lower</param>
		/// <param name="whenToPassivelyInform">Ignored for now</param>
		/// <param name="shortUserLevelMessage">Should make sense in a small toast notification</param>
		/// <param name="exception"></param>
		public static void Handle(ThrowIf whenToThrow, InformIf whenToPassivelyInform, string shortUserLevelMessage = null,
			Exception exception = null)
		{
			shortUserLevelMessage = shortUserLevelMessage == null ? "" : shortUserLevelMessage;
			Logger.WriteError("NonFatalProblem: " + shortUserLevelMessage, exception);

			var channel = ApplicationUpdateSupport.ChannelName.ToLower();

			if(exception != null && Matches(whenToThrow).Any(s => channel.Contains(s)))
			{
				throw exception;
			}

			if (!string.IsNullOrEmpty(shortUserLevelMessage)  && Matches(whenToPassivelyInform).Any(s => channel.Contains(s)))
			{
				//Future
			}
		}

		private static string[] Matches(ThrowIf t)
		{
			switch (t)
			{
				case ThrowIf.Release:
					return new string[] {"developer", "alpha", "beta", "release"};
				case ThrowIf.Beta:
					return new string[] { "developer", "alpha", "beta" };
				case ThrowIf.Alpha:
					return new string[] { "developer", "alpha" };

				default:
					return new string[] {};
			}
		}

		private static string[] Matches(InformIf t)
		{
			switch (t)
			{
				case InformIf.Release:
					return new string[] { "developer", "alpha", "beta", "release" };
				case InformIf.Beta:
					return new string[] { "developer", "alpha", "beta" };
				case InformIf.Alpha:
					return new string[] { "developer", "alpha" };
				default:
					return new string[] { };
			}
		}
	}
}
