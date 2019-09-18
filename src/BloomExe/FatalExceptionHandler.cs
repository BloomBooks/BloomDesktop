using Bloom.web.controllers;
using System;

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
			if (!GetShouldHandleException(sender, e.ExceptionObject as Exception))
				return;

			if (e.ExceptionObject is Exception)
				DisplayError(e.ExceptionObject as Exception);
			else
				DisplayError(new ApplicationException("Got unknown exception"));
		}

		protected override bool ShowUI
		{
			get { return false; }
		}

		protected override bool DisplayError(Exception exception)
		{

			//TODO need ot look at WinFormsExceptionHandler and copy a bunch of that

			//review: what if there is none, will that still work? Could that happen?
				ProblemReportApi.ShowProblemDialog(System.Windows.Forms.Form.ActiveForm);

			return true;
		}
	}
}
