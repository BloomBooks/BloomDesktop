using Bloom.web.controllers;
using System;
using SIL.Reporting;

namespace Bloom
{
	internal class FatalExceptionHandler : SIL.Reporting.ExceptionHandler
	{
		/// ------------------------------------------------------------------------------------
		/// <summary>
		/// Set exception handler. Needs to be done before we create splash screen (don't
		/// understand why, but otherwise some exceptions don't get caught).
		/// </summary>
		/// ------------------------------------------------------------------------------------
		public FatalExceptionHandler()
		{
			// We also want to catch the UnhandledExceptions for all the cases that
			// ThreadException don't catch, e.g. in the startup.
			AppDomain.CurrentDomain.UnhandledException += HandleUnhandledException;
		}

		/// ------------------------------------------------------------------------------------
		/// <summary>
		/// Catches and displays otherwise unhandled exception, especially those that happen
		/// during startup of the application before we show our main window.
		/// </summary>
		/// ------------------------------------------------------------------------------------
		protected void HandleUnhandledException(object sender, UnhandledExceptionEventArgs e)
		{
			// We're already handling an unhandled exception, let's not handle another while we are handling this one.
			AppDomain.CurrentDomain.UnhandledException -= HandleUnhandledException;

			if (!GetShouldHandleException(sender, e.ExceptionObject as Exception))
				return;

			if (e.ExceptionObject is Exception)
				DisplayError(e.ExceptionObject as Exception);
			else
				DisplayError(new ApplicationException("Got unknown exception"));

			// Reinstate, just in case. (Bloom should be closing now.)
			AppDomain.CurrentDomain.UnhandledException += HandleUnhandledException;
		}

		protected override bool ShowUI
		{
			get { return false; }
		}

		protected override bool DisplayError(Exception exception)
		{
			// Review: Do we need to add any other code from WinFormsExceptionHandler?

			// If there is no ActiveForm, SafeInvoke will hit a "Guard against null".
			ProblemReportApi.ShowProblemDialog(System.Windows.Forms.Form.ActiveForm, exception);

			return true;
		}
	}
}
