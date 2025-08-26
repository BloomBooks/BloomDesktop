using System;
using SIL.Reporting;

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
        /// <param name="settings">The customization settings controlling which/how extra buttons show up</param>
        /// <param name="policy">The policy to use to decide whether to show the notification.</param>
        void NotifyUserOfProblem(
            string message,
            NotifyUserOfProblemSettings settings,
            IRepeatNoticePolicy policy
        );

        /// <summary>
        /// Notifies the user of problem, with an optional Report button and a optional customizable extra action in addition to the normal Close button.
        /// </summary>
        /// <param name="message">The message to show the user</param>
        /// <param name="exception">Any accompanying exception</param>
        /// <param name="settings">The customization settings controlling which/how extra buttons show up</param>
        /// <param name="policy">The policy to use to decide whether to show the notification.</param>
        void NotifyUserOfProblem(
            string message,
            Exception exception,
            NotifyUserOfProblemSettings settings,
            IRepeatNoticePolicy policy
        );
    }
}
