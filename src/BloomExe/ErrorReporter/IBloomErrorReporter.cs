using SIL.Reporting;
using System;

namespace Bloom.ErrorReporter
{
	/// <summary>
	/// An IErrorReporter which is also able to handle additional error reporting dialog scenarios used in Bloom 
	/// </summary>
	internal interface IBloomErrorReporter : IErrorReporter
	{
		/// <summary>
		/// Notifies the user of problem, with an optional Report button and a optional customizable extra action in addition to the normal Close button.
		/// </summary>
		/// <param name="message">The message to show the user</param>
		/// <param name="exception">Any accompanying exception</param>
		/// <param name="policy">The policy to use to decide whether to show the notification.</param>
		/// 
		/// <param name="allowSendReport">Whether or not to show the Report button.</param>
		/// 
		/// <param name="extraButtonLabel">The label of the extra button (this is separate from the report button). "" means disabled. null means default, which is disabled.</param>
		/// <param name="onExtraButtonClicked">The action to take when the extra button is clicked.</param>
		void NotifyUserOfProblem(string message, Exception exception, IRepeatNoticePolicy policy,
			AllowSendReport allowSendReport,
			string extraButtonLabel, Action<string, Exception> onExtraButtonClicked);
	}
}
