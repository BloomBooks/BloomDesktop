using System;
using System.Collections.Generic;
using System.Linq;
using SIL.Reporting;

namespace Bloom.ErrorReporter
{
    /// <summary>
    /// An error reporter that combines the actions of multiple error reporters.
    /// </summary>
    class CompositeErrorReporter : IBloomErrorReporter
    {
        // The reporters whose actions should be combined. It should be ordered in the order that the actions should execute.
        private IList<IBloomErrorReporter> Reporters { get; set; }

        // For methods that return a value, the result of the CompositeErrorReporter will be the value from PrimaryReporter
        private IErrorReporter PrimaryReporter { get; set; }

        /// <summary>
        /// Creates a composite error reporter consisting of one primary reporter and any number of secondary reporters.
        /// </summary>
        /// <param name="reporters">1 or more error reporters, ordered in the order you want them to run.</param>
        /// <param name="primaryReporter"If an interface method has a return value, the composite will return the value that the {primaryReporter} returns.
        /// Note: The primaryReporter should also be included in {reporters}</param>
        public CompositeErrorReporter(
            IList<IBloomErrorReporter> reporters,
            IErrorReporter primaryReporter
        )
        {
            if (reporters == null)
            {
                throw new ArgumentNullException("reporters should not be null");
            }

            this.Reporters = reporters;
            this.PrimaryReporter = primaryReporter ?? reporters.First();
        }

        public void NotifyUserOfProblem(
            IRepeatNoticePolicy policy,
            Exception exception,
            string message
        )
        {
            foreach (var reporter in Reporters)
            {
                reporter.NotifyUserOfProblem(policy, exception, message);
            }
        }

        public ErrorResult NotifyUserOfProblem(
            IRepeatNoticePolicy policy,
            string alternateButton1Label,
            ErrorResult resultIfAlternateButtonPressed,
            string message
        )
        {
            ErrorResult? primaryResult = null;
            foreach (var reporter in Reporters)
            {
                var currResult = reporter.NotifyUserOfProblem(
                    policy,
                    alternateButton1Label,
                    resultIfAlternateButtonPressed,
                    message
                );

                if (reporter == PrimaryReporter)
                {
                    primaryResult = currResult;
                }
            }

            return primaryResult.Value;
        }

        public void NotifyUserOfProblem(
            string message,
            NotifyUserOfProblemSettings settings,
            IRepeatNoticePolicy policy
        ) => NotifyUserOfProblem(message, null, settings, policy);

        public void NotifyUserOfProblem(
            string message,
            Exception exception,
            NotifyUserOfProblemSettings settings,
            IRepeatNoticePolicy policy
        )
        {
            foreach (var reporter in Reporters)
            {
                reporter.NotifyUserOfProblem(message, exception, settings, policy);
            }
        }

        public void ReportFatalException(Exception e)
        {
            foreach (var reporter in Reporters)
            {
                reporter.ReportFatalException(e);
            }
        }

        public void ReportFatalMessageWithStackTrace(string message, object[] args)
        {
            foreach (var reporter in Reporters)
            {
                reporter.ReportFatalMessageWithStackTrace(message, args);
            }
        }

        public void ReportNonFatalException(Exception exception, IRepeatNoticePolicy policy)
        {
            foreach (var reporter in Reporters)
            {
                reporter.ReportNonFatalException(exception, policy);
            }
        }

        public void ReportNonFatalExceptionWithMessage(
            Exception error,
            string message,
            params object[] args
        )
        {
            foreach (var reporter in Reporters)
            {
                reporter.ReportNonFatalExceptionWithMessage(error, message, args);
            }
        }

        public void ReportNonFatalMessageWithStackTrace(string message, params object[] args)
        {
            foreach (var reporter in Reporters)
            {
                reporter.ReportNonFatalMessageWithStackTrace(message, args);
            }
        }
    }
}
