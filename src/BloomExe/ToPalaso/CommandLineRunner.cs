using System;
using System.Globalization;
using System.Text;
using SIL.CommandLineProcessing;
using SIL.Progress;

namespace Bloom.ToPalaso
{
	public class CommandLineRunner
	{
		// This one doesn't attempt to influence the encoding used
		public static ExecutionResult RunWithInvariantCulture(string exePath, string arguments, string fromDirectory, int secondsBeforeTimeOut, IProgress progress)
		{
			return RunWithInvariantCulture(exePath, arguments, null, fromDirectory, secondsBeforeTimeOut, progress);
		}

		public static ExecutionResult RunWithInvariantCulture(string exePath, string arguments, Encoding encoding,
			string fromDirectory, int secondsBeforeTimeOut, IProgress progress,
			Action<string> actionForReportingProgress = null, string standardInputPath = null, bool isManagedProcess = false)
		{
			var currentCulture = CultureInfo.CurrentCulture;
			var currentUICulture = CultureInfo.CurrentUICulture;
			try
			{
				CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
				CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
				return SIL.CommandLineProcessing.CommandLineRunner.Run(exePath, arguments, encoding,
					fromDirectory, secondsBeforeTimeOut, progress, actionForReportingProgress, standardInputPath, isManagedProcess);
			}
			finally
			{
				CultureInfo.CurrentCulture = currentCulture;
				CultureInfo.CurrentUICulture = currentUICulture;
			}
		}
	}
}
