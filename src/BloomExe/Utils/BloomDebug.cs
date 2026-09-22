using System;
using System.Diagnostics;
using System.Windows.Forms;

namespace Bloom.Utils
{
    /// <summary>
    /// Debug-only assertions that make a developer mistake impossible to miss, without the one
    /// failure mode that makes System.Diagnostics.Debug.Fail unusable here: with no debugger
    /// attached, Debug.Fail becomes Environment.FailFast and kills the process. That takes Bloom
    /// down for a developer who was doing something else, and mid-test-run it kills the test host,
    /// which VSTest then reports with a cheerful "Passed!" for however many tests had run (the
    /// reason build/test-abort-markers.txt exists).
    ///
    /// So this picks the loudest thing that suits where it is running: break into the debugger if
    /// there is one, fail the test if we are under test, and otherwise show the developer a dialog
    /// they cannot miss and offer to attach a debugger from it.
    /// </summary>
    public static class BloomDebug
    {
        /// <summary>
        /// Report a definite programming error. Does nothing in a release build.
        ///
        /// [Conditional("DEBUG")] is the same mechanism Debug.Fail uses, and it is stronger than
        /// wrapping the body in #if DEBUG: the compiler removes the CALL, so in a release build the
        /// arguments are not evaluated either and an expensive interpolated message costs nothing.
        /// (It works that way because every caller is compiled in this same assembly.)
        /// </summary>
        /// <param name="message">What went wrong. Optional, but a message is what makes the dialog
        /// or the failed test worth reading.</param>
        [Conditional("DEBUG")]
        public static void Fail(string message = null)
        {
            var text = string.IsNullOrEmpty(message) ? "A Bloom debug assertion failed." : message;

            // A debugger is watching, so hand it the problem exactly as Debug.Fail would.
            if (Debugger.IsAttached)
            {
                Debug.Fail(text);
                return;
            }

            // Under test: throw, so the offending test fails and says why. NUnit's own Assert.Fail
            // would read a little better, but BloomExe deliberately does not reference a test
            // framework, and an exception is how NUnit reports a failure anyway. The important part
            // is that we do NOT reach the dialog below: a modal MessageBox in an automated run
            // blocks until something kills the suite.
            if (Program.RunningUnitTests)
                throw new ApplicationException("BloomDebug.Fail: " + text);

            // A developer running Bloom with no debugger. Stop them: this is a definite mistake,
            // and the alternative is a log line nobody reads until much later.
            var answer = MessageBox.Show(
                text
                    + Environment.NewLine
                    + Environment.NewLine
                    + new StackTrace(1, true)
                    + Environment.NewLine
                    + "Attach a debugger and break here?",
                "Bloom developer error (DEBUG builds only)",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Error
            );
            if (answer != DialogResult.Yes)
                return;

            // Debugger.Launch() is what gives us the old Debug.Fail "Retry" behaviour for free: it
            // brings up the just-in-time debugger picker and attaches. Break() then stops on the
            // line that called us rather than somewhere inside this method's caller chain.
            if (Debugger.Launch())
                Debugger.Break();
        }
    }
}
