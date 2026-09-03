using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace Bloom
{
    /// <summary>
    /// What an automation run (--automation, e.g. the Playwright suites) does with the windows it
    /// opens. One environment variable decides it, BLOOM_AUTOMATION_MONITOR:
    ///
    ///   "2" (any 1-based index into Screen.AllScreens)  every window opens on that monitor
    ///   "headless"                                      every window opens off every monitor
    ///   absent, empty, or anything else                 Bloom places its windows as it always does
    ///
    /// The variable applies ONLY under --automation. A developer who leaves it set in their shell
    /// therefore still gets an ordinary, visible Bloom when they start one themselves; only a run
    /// that already declared itself automation obeys it.
    ///
    /// Nothing here changes whether an automation window takes the keyboard focus. It never does,
    /// wherever it is (see Shell.ShowWithoutActivation and Shell.ReallyComeToFront).
    ///
    /// The variable is read on each call rather than cached. It costs nothing worth saving, and
    /// nothing in Bloom writes it: the run's parent, normally the Playwright fixture, sets it
    /// before Bloom starts, so every caller gets the same answer. That matters because the callers
    /// have to agree. Shell places the window, and WebView2Browser turns off the occlusion check
    /// for an off-screen one; if those two disagreed, every screenshot would come back blank.
    /// </summary>
    public static class AutomationWindowPlacement
    {
        /// <summary>The value of BLOOM_AUTOMATION_MONITOR that asks for off-screen windows.</summary>
        public const string HeadlessSetting = "headless";

        public const string VariableName = "BLOOM_AUTOMATION_MONITOR";

        /// <summary>Where an automation run puts its windows.</summary>
        public enum Choice
        {
            /// <summary>
            /// Bloom places its windows the way it normally does, so a run appears on the
            /// developer's desktop. This is what an absent or unrecognized value means: nobody
            /// asked for anything, so nothing is imposed.
            /// </summary>
            AsBloomNormallyWould,

            /// <summary>The variable named a monitor that exists. Every window goes on it.</summary>
            OnTheChosenMonitor,

            /// <summary>The variable said "headless". Every window goes off every monitor.</summary>
            OffEveryMonitor,
        }

        /// <summary>
        /// Read BLOOM_AUTOMATION_MONITOR and say what this run should do. Answers
        /// AsBloomNormallyWould for a run that is not automation, whatever the variable says.
        /// </summary>
        public static Choice GetChoice()
        {
            if (!Program.StartupAutomation)
                return Choice.AsBloomNormallyWould;
            return Parse(
                Environment.GetEnvironmentVariable(VariableName),
                Screen.AllScreens.Length,
                out _
            );
        }

        /// <summary>
        /// Turn one raw value of BLOOM_AUTOMATION_MONITOR into a choice. Split out from GetChoice
        /// so it can be tested without an environment variable or a real set of monitors.
        ///
        /// A value naming a monitor this machine does not have counts as unrecognized, and so does
        /// a typo: both mean Bloom places its windows normally. That is deliberate. The developer
        /// then SEES a Bloom window, which is the outcome that tells them the variable did not take
        /// effect; a typo that silently hid the window would leave them with no way to notice.
        /// </summary>
        /// <param name="setting">The raw value, or null when the variable is not set.</param>
        /// <param name="screenCount">How many monitors this machine has.</param>
        /// <param name="oneBasedMonitor">
        /// The monitor asked for, when the answer is OnTheChosenMonitor; 0 otherwise.
        /// </param>
        public static Choice Parse(string setting, int screenCount, out int oneBasedMonitor)
        {
            oneBasedMonitor = 0;
            var trimmed = setting?.Trim();
            if (string.IsNullOrEmpty(trimmed))
                return Choice.AsBloomNormallyWould;
            if (trimmed.Equals(HeadlessSetting, StringComparison.OrdinalIgnoreCase))
                return Choice.OffEveryMonitor;
            if (int.TryParse(trimmed, out var index) && index >= 1 && index <= screenCount)
            {
                oneBasedMonitor = index;
                return Choice.OnTheChosenMonitor;
            }
            return Choice.AsBloomNormallyWould;
        }

        /// <summary>
        /// True when this run keeps its windows off every monitor. Read this rather than the
        /// variable: it is also false for a run that is not automation.
        /// </summary>
        public static bool IsOffEveryMonitor => GetChoice() == Choice.OffEveryMonitor;

        /// <summary>
        /// The monitor an automation run opens its windows on. Only meaningful when the choice is
        /// OnTheChosenMonitor; falls back to the primary screen otherwise, so that callers wanting
        /// a size rather than a position have one.
        /// </summary>
        public static Screen GetChosenMonitor()
        {
            if (
                Program.StartupAutomation
                && Parse(
                    Environment.GetEnvironmentVariable(VariableName),
                    Screen.AllScreens.Length,
                    out var oneBasedMonitor
                ) == Choice.OnTheChosenMonitor
            )
            {
                return Screen.AllScreens[oneBasedMonitor - 1];
            }
            return Screen.PrimaryScreen;
        }

        /// <summary>
        /// Where an off-every-monitor run puts a window: the size of the primary screen's working
        /// area, positioned to the left of every monitor, so that not one pixel of it is on any
        /// screen.
        ///
        /// The window is moved rather than minimized or hidden because a minimized WebView2 stops
        /// painting: screenshots come back blank and the layout is the wrong size. An off-screen
        /// window of the normal size keeps painting, so a test sees exactly what a user would.
        /// </summary>
        public static Rectangle GetBoundsOffEveryMonitor()
        {
            var size = Screen.PrimaryScreen.WorkingArea.Size;
            // Two bounds, and the window has to respect both.
            //
            // The first is the leftmost monitor: the window's right edge has to be left of it, or
            // part of the window shows. Both the position and the width above are in the
            // coordinates this process sees, and the desktop does not always agree with them: on
            // a machine whose primary monitor is scaled to 160%, Bloom asked for a window 1587
            // pixels wide and Windows made one 2560 pixels wide, which ate all but 27 pixels of a
            // 1000-pixel cushion. So leave room for the whole error rather than a fixed number of
            // pixels: Windows scales a monitor by at most 400%, so a window this process believes
            // is W wide covers at most 4W pixels of the desktop. Four widths to the left of the
            // leftmost monitor therefore clears it whatever the scale factors are, and the
            // 1000 pixels on top of that keep the two edges from meeting exactly.
            //
            // The second is -32000, as far left as a window may go: Windows still places a window
            // there, and anything beyond about -32768 runs into the 16-bit coordinates that some
            // of the older window messages still carry.
            //
            // On any real layout the first bound gives several thousand pixels to the left, well
            // inside the second. A leftward run of monitors more than about 30000 pixels wide
            // would need a position that satisfies neither, and then the limit wins: a window at
            // a coordinate Windows will not honour is worse than one that overlaps a monitor.
            const int farLeftWindowsAllows = -32000;
            const int largestScaleFactorWindowsAllows = 4;
            var leftmostX = Screen.AllScreens.Min(screen => screen.Bounds.Left);
            var x = Math.Max(
                farLeftWindowsAllows,
                leftmostX - (size.Width * largestScaleFactorWindowsAllows) - 1000
            );
            return new Rectangle(x, 0, size.Width, size.Height);
        }
    }
}
