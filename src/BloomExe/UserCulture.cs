using System;
using System.Globalization;

namespace Bloom
{
    /// <summary>
    /// Bloom runs with CultureInfo.CurrentCulture set to the invariant culture, so that code which
    /// does not think about culture formats and parses machine data (CSS measurements, version
    /// numbers, file names, logs, tool arguments) the same way on every machine. The user's own
    /// regional format is kept here, and is used only where we show or read numbers and dates, or
    /// sort lists, for the user. See docs/invariant-culture/PLAN.md and BL-16981.
    ///
    /// CurrentUICulture, which L10NSharp uses to choose translations, is not touched.
    ///
    /// This is the only file allowed to read or set CultureInfo.CurrentCulture; BannedSymbols.txt
    /// makes everywhere else a build error.
    /// </summary>
    public static class UserCulture
    {
        /// <summary>
        /// The user's regional format, as the process started with it. Use it to format a number
        /// or date the user will see, to parse one the user typed, and to sort a list the user
        /// will read. Until UseInvariantCulture runs this is the invariant culture.
        /// </summary>
        public static CultureInfo Current { get; private set; } = CultureInfo.InvariantCulture;

        /// <summary>
        /// Remembers the user's regional format in Current, then makes the invariant culture the
        /// culture of this thread and of every thread that has not set its own. Call it first thing
        /// in Main, before anything can save the culture in a static field. Calling it again does
        /// not lose the user's culture.
        /// </summary>
        public static void UseInvariantCulture()
        {
#pragma warning disable RS0030 // the one place allowed to touch CurrentCulture
            if (!CultureInfo.CurrentCulture.Equals(CultureInfo.InvariantCulture))
                Current = CultureInfo.CurrentCulture;
            // DefaultThreadCurrentCulture covers every thread that has not set its own culture,
            // including ones already running; CurrentCulture covers this thread and flows into
            // async continuations started from it.
            CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
#pragma warning restore RS0030
        }

        /// <summary>
        /// For tests: run as a user whose regional format is the given culture. The process
        /// culture stays invariant, as it is in Bloom.
        /// </summary>
        internal static void SetForTests(CultureInfo culture)
        {
            Current = culture;
        }

        private static bool s_reportedCultureChange;

        /// <summary>
        /// Reports it if something (most likely a library) has moved the process off the invariant
        /// culture since UseInvariantCulture ran. Cheap enough to call on every API request. In
        /// tests it throws, so the test that did it fails; for users it is reported once, quietly.
        /// </summary>
        public static void CheckProcessCultureIsInvariant()
        {
#pragma warning disable RS0030 // the one place allowed to touch CurrentCulture
            var current = CultureInfo.CurrentCulture;
            var threadDefault = CultureInfo.DefaultThreadCurrentCulture;
#pragma warning restore RS0030
            if (
                current.Equals(CultureInfo.InvariantCulture)
                && CultureInfo.InvariantCulture.Equals(threadDefault)
            )
                return;
            var message =
                "Bloom should run in the invariant culture, but CurrentCulture is "
                + $"'{current.Name}' and DefaultThreadCurrentCulture is '{threadDefault?.Name}'. "
                + "Something changed them after startup (see UserCulture.UseInvariantCulture).";
            if (Program.RunningUnitTests)
                throw new InvalidOperationException(message);
            if (s_reportedCultureChange)
                return;
            s_reportedCultureChange = true;
            NonFatalProblem.Report(
                ModalIf.None,
                PassiveIf.Alpha,
                "Bloom's culture was changed",
                message
            );
        }
    }
}
