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
    /// All analytics traffic should route through this class; build/check-csharp-analytics.sh
    /// attempts to enforce that at commit time.
    ///
    /// Every event is logged here as well as sent. DEBUG builds construct DesktopAnalytics with
    /// allowTracking:false, so on a developer machine the log line is what tells you an event
    /// fired; it says so explicitly, and also goes to standard error, so someone running ./go.sh
    /// watches events scroll past as they exercise the feature. In a release run the line still
    /// reaches the Bloom log, so a support log shows what analytics reported at the time.
    ///
    /// The log is only worth reading if it is complete, which is the reason for the commit-time
    /// check: a missing line would mean "not instrumented" as readily as "did not happen".
    /// </summary>
    public static class BloomAnalytics
    {
        /// <summary>
        /// Report an event, with optional properties. The signature deliberately mirrors
        /// DesktopAnalytics.Analytics.Track so that converting a call site is only a name change.
        /// </summary>
        public static void Track(string eventName, Dictionary<string, string> properties = null)
        {
            // Recording an event must never be able to affect what it is observing. Callers sit
            // inside try/catch blocks that mean something else entirely -- one of them turns any
            // exception into "the app build failed" on the user's screen -- so an exception raised
            // here would be attributed to their operation, not to analytics.
            //
            // Send FIRST, then log, each guarded separately. Logging first would let a failure in
            // our own logging stop the event ever reaching Segment, which is the wrong way round.
            // Separate guards also keep the two reports honest: a single try/catch around both
            // would announce a logging failure as a failure to send.
            try
            {
                if (properties == null)
                    Analytics.Track(eventName);
                else
                    Analytics.Track(eventName, properties);
            }
            catch (Exception e)
            {
                Logger.WriteEvent($"[analytics] FAILED to send \"{eventName}\": {e.Message}");
            }
            try
            {
                Log(FormatForLog(eventName, properties, Analytics.AllowTracking));
            }
            catch (Exception e)
            {
                // Deliberately swallowed, and the only place in this file that is: the channel we
                // would report a logging failure on is the one that just failed.
                Console.Error.WriteLine($"[analytics] could not log \"{eventName}\": {e.Message}");
            }
        }

        /// <summary>
        /// Report an exception to analytics (Segment), which is a different thing from reporting a
        /// problem to Sentry -- see NonFatalProblem, which decides between them. Logged here for
        /// the same reason as Track: so that "was this reported?" has an answer you can read.
        /// </summary>
        public static void ReportException(Exception exception)
        {
            // Guarded for the same reason as Track, and more urgently: one caller is Program's
            // global unhandled-exception handler, so a throw from in here would be a failure while
            // reporting a failure. Sent before it is logged, for the reason given in Track.
            try
            {
                Analytics.ReportException(exception);
            }
            catch (Exception e)
            {
                Logger.WriteEvent($"[analytics] FAILED to report an exception: {e.Message}");
            }
            try
            {
                Log(
                    FormatForLog(
                        $"(exception) {exception?.GetType().Name}",
                        null,
                        Analytics.AllowTracking
                    )
                );
            }
            catch (Exception e)
            {
                Console.Error.WriteLine(
                    $"[analytics] could not log an exception report: {e.Message}"
                );
            }
        }

        /// <summary>
        /// One line describing the event, and whether it is actually going anywhere. Properties are
        /// ordered by name so that the same event reads the same way from one run to the next.
        ///
        /// Note that Log passes this to Logger.WriteEvent as the whole message and never as a
        /// format string with arguments. That matters: Logger.WriteEvent runs string.Format over
        /// its message when (and only when) it is given arguments, so a brace arriving inside an
        /// event property -- from a search provider's error message, say, which we pass along as
        /// we received it -- is harmless here. Keep it that way; adding arguments would turn a
        /// stray brace in someone else's text into a FormatException.
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
