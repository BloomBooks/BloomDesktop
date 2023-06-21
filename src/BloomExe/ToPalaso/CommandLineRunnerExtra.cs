using System;
using System.Globalization;
using System.Text;
using SIL.CommandLineProcessing;
using SIL.Progress;

namespace Bloom.ToPalaso
{
	/// <summary>
	/// Extra static methods for the CommandLineRunner class.
	/// </summary>
	/// <remarks>
	/// Note that static methods can't be added as extension methods, and it gets confusing
	/// to have two classes with the same name in different namespaces when classes from both
	/// namespaces are used in the source file.
	/// </remarks>
	public static class CommandLineRunnerExtra
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
				return CommandLineRunner.Run(exePath, arguments, encoding,
					fromDirectory, secondsBeforeTimeOut, progress, actionForReportingProgress, standardInputPath, isManagedProcess);
			}
			finally
			{
				CultureInfo.CurrentCulture = currentCulture;
				CultureInfo.CurrentUICulture = currentUICulture;
			}
		}
	}

	/// <summary>
	/// Extra methods for the CommandLineRunner class implemented as extension methods.
	/// </summary>
	public static class CommandLineRunnerExtensions
	{
		public static ExecutionResult StartWithInvariantCulture(this CommandLineRunner runner,
			string exePath, string arguments, Encoding encoding,
			string fromDirectory, int secondsBeforeTimeOut, IProgress progress,
			Action<string> actionForReportingProgress, string standardInputPath = null,
			bool isManagedProcess = false)
		{
			var currentCulture = CultureInfo.CurrentCulture;
			var currentUICulture = CultureInfo.CurrentUICulture;
			try
			{
				CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
				CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
				return runner.Start(exePath, arguments, encoding,
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
