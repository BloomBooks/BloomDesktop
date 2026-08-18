using System;
using System.Collections.Generic;
using System.Linq;
using DesktopAnalytics;
using SIL.Reporting;

namespace Bloom
{
    /// <summary>
    /// The single place Bloom hands an analytics event to DesktopAnalytics, and so to Segment.
    ///
    /// It exists to close a hole that makes new events unverifiable on a developer machine.
    /// <see cref="Program.InitializeAnalytics"/> constructs DesktopAnalytics with
    /// allowTracking:false in DEBUG, and DesktopAnalytics makes that decision *inside*
    /// Analytics.Track. So a new event is easy to write, easy to believe in, and impossible to
    /// see: nothing is sent, and nothing says that nothing was sent. The only way to confirm one
    /// was to ship it to alpha and wait.
    ///
    /// So every event is logged here, on our side of that call, before it is offered to
    /// DesktopAnalytics. When tracking is off the log line says so, and also goes to standard
    /// error, so a developer running ./go.sh watches events scroll past as they exercise the
    /// feature. In a release run the line still reaches the Bloom log, which means a support log
    /// now shows what analytics reported at the time -- worth having in its own right.
    ///
    /// Call this rather than DesktopAnalytics.Analytics.Track:
    /// build/check-csharp-analytics.sh fails the commit otherwise. An event that bypasses this is
    /// invisible again, and a log the reader cannot trust to be complete is worse than no log --
    /// a missing line would mean "not instrumented" as readily as "did not happen".
    /// </summary>
    public static class BloomAnalytics
    {
        /// <summary>
        /// Report an event, with optional properties. The signature deliberately mirrors
        /// DesktopAnalytics.Analytics.Track so that converting a call site is only a name change.
        /// </summary>
        public static void Track(string eventName, Dictionary<string, string> properties = null)
        {
            Log(FormatForLog(eventName, properties, Analytics.AllowTracking));
            if (properties == null)
                Analytics.Track(eventName);
            else
                Analytics.Track(eventName, properties);
        }

        /// <summary>
        /// Report an exception to analytics (Segment), which is a different thing from reporting a
        /// problem to Sentry -- see NonFatalProblem, which decides between them. Logged here for
        /// the same reason as Track: so that "was this reported?" has an answer you can read.
        /// </summary>
        public static void ReportException(Exception exception)
        {
            Log(
                FormatForLog(
                    $"(exception) {exception?.GetType().Name}",
                    null,
                    Analytics.AllowTracking
                )
            );
            Analytics.ReportException(exception);
        }

        /// <summary>
        /// One line describing the event, and whether it is actually going anywhere. Properties are
        /// ordered by name so that the same event reads the same way from one run to the next.
        ///
        /// Note that Log passes this to Logger.WriteEvent as the whole message and never as a
        /// format string with arguments. That matters: Logger.WriteEvent runs string.Format over
        /// its message when (and only when) it is given arguments, so a brace arriving inside an
        /// event property -- a search term, say -- is harmless here. Keep it that way; adding
        /// arguments would turn one user's typing into a FormatException.
        /// </summary>
        /// <param name="trackingAllowed">Normally DesktopAnalytics.Analytics.AllowTracking. Passed
        /// in rather than read here so that this stays a pure function and both of its answers can
        /// be tested -- that flag is read-only and false in a test run, so a version that consulted
        /// it directly could only ever be tested one way round.</param>
        internal static string FormatForLog(
            string eventName,
            Dictionary<string, string> properties,
            bool trackingAllowed
        )
        {
            var prefix = trackingAllowed
                ? "[analytics]"
                : "[analytics NOT SENT: tracking is off in this build]";
            if (properties == null || properties.Count == 0)
                return $"{prefix} {eventName}";
            var pairs = string.Join(
                ", ",
                properties.OrderBy(p => p.Key).Select(p => $"{p.Key}={p.Value}")
            );
            return $"{prefix} {eventName} -- {pairs}";
        }

        private static void Log(string message)
        {
            Logger.WriteEvent(message);
            // The log file is the wrong place to look when you are sitting in front of the running
            // program, so when nothing is being sent -- a developer or alpha-tester build -- say it
            // where they will see it. Not in a release run, which would pay a console write per
            // event for nobody's benefit; and not under test, where it would be pure noise, since
            // AllowTracking is false there simply because analytics was never initialized.
            if (!Analytics.AllowTracking && !Program.RunningUnitTests)
                Console.Error.WriteLine(message);
        }
    }
}
