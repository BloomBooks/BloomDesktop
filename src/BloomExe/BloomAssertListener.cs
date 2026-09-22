using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using SIL.Reporting;

namespace Bloom
{
    /// <summary>
    /// Decides what a failed Debug.Assert or Debug.Fail does in a Debug build.
    ///
    /// On .NET (unlike .NET Framework) there is no assertion dialog. The runtime's default listener
    /// breaks into the debugger if one is attached, and otherwise calls Environment.FailFast, which
    /// kills Bloom on the spot with no dialog, no exception handler, and no report. An assert is meant
    /// to stop the developer in their tracks, not the program, so this listener replaces the default
    /// one and restores the old behaviour: write the failure to Bloom's log, break into a debugger if
    /// there is one, and otherwise put up a dialog offering to quit, attach a debugger, or carry on.
    /// E2e runs have nobody to answer a dialog, so they log and carry on. Other automated modes
    /// (harvester, console verbs) run Release builds, where this listener does not exist; a Debug
    /// build in one of those modes has a developer behind it who wants to see the assert.
    ///
    /// This exists only in Debug builds. Debug.Assert itself compiles out of Release, and Install is
    /// [Conditional("DEBUG")] as well, so a Release Bloom never has this listener: whatever the runtime
    /// would do with a Trace.Fail from some library, it still does.
    /// </summary>
    internal class BloomAssertListener : DefaultTraceListener
    {
        /// <summary>
        /// Replace the runtime's default listener with this one. Call once, early in Main, before
        /// anything that might assert. The call is compiled away outside Debug builds.
        /// </summary>
        [Conditional("DEBUG")]
        public static void Install()
        {
            foreach (var listener in Trace.Listeners.OfType<DefaultTraceListener>().ToList())
                Trace.Listeners.Remove(listener);
            Trace.Listeners.Add(new BloomAssertListener());
        }

        /// <summary>Debug.Assert(false, message) and Debug.Fail(message) land here.</summary>
        public override void Fail(string message) => Fail(message, null);

        /// <summary>Every failed assert ends up here; see the class comment for what we do with it.</summary>
        public override void Fail(string message, string detailMessage)
        {
            var text = string.IsNullOrEmpty(detailMessage)
                ? message
                : message + Environment.NewLine + detailMessage;
            var stack = CallerStack();
            Logger.WriteEvent("Debug.Assert failed: " + text + Environment.NewLine + stack);
            // A command-line verb is attached to the console it was run from, so say there why the
            // command is about to stop and wait: the dialog alone would look like a hang.
            if (Program.RunningInConsoleMode)
                Console.Error.WriteLine(
                    "Debug.Assert failed: " + text + Environment.NewLine + stack
                );

            if (Debugger.IsAttached)
            {
                Debugger.Break();
                return;
            }
            // The flags are set during argument parsing, some way into Main; the command line itself
            // is there from the first instruction, so an e2e run is recognized even for an assert
            // that fires during early startup.
            if (Program.RunningE2eTests || Environment.GetCommandLineArgs().Contains("--e2e"))
                return;

            // ServiceNotification: no owner window, so this works from the server worker threads
            // where most asserts fire, and it comes up in front of everything.
            var result = MessageBox.Show(
                text
                    + Environment.NewLine
                    + Environment.NewLine
                    + stack
                    + Environment.NewLine
                    + "Abort quits Bloom. Retry attaches a debugger. Ignore carries on.",
                "Debug.Assert failed",
                MessageBoxButtons.AbortRetryIgnore,
                MessageBoxIcon.Error,
                MessageBoxDefaultButton.Button3,
                MessageBoxOptions.ServiceNotification
            );
            switch (result)
            {
                case DialogResult.Abort:
                    ProgramExit.Exit();
                    break;
                case DialogResult.Retry:
                    Debugger.Launch();
                    if (Debugger.IsAttached)
                        Debugger.Break();
                    break;
            }
        }

        /// <summary>
        /// The stack from the code that asserted downwards. The frames above it are plumbing (this
        /// class, Debug, Trace) that would only push the interesting line off the dialog.
        /// </summary>
        private static string CallerStack()
        {
            var frames = new StackTrace(true)
                .GetFrames()
                .SkipWhile(f =>
                {
                    var type = f.GetMethod()?.DeclaringType;
                    return type == null
                        || type == typeof(BloomAssertListener)
                        || (type.Namespace ?? "").StartsWith("System.Diagnostics");
                })
                .Take(15);
            var sb = new StringBuilder();
            foreach (var frame in frames)
            {
                var method = frame.GetMethod();
                sb.Append("   at ")
                    .Append(method?.DeclaringType?.FullName)
                    .Append('.')
                    .Append(method?.Name);
                if (frame.GetFileName() != null)
                    sb.Append(" in ")
                        .Append(Path.GetFileName(frame.GetFileName()))
                        .Append(":line ")
                        .Append(frame.GetFileLineNumber());
                sb.AppendLine();
            }
            return sb.ToString();
        }
    }
}
