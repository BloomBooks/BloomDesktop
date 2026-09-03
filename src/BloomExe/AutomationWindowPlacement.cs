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
    ///   "2" (a 1-based monitor number, counted left to right)  every window opens on that monitor
    ///   "headless", or "0" for no monitor at all                every window opens off every monitor
    ///   absent, empty, or anything else                         Bloom places windows as it always does
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

        /// <summary>The number that says the same thing as HeadlessSetting: no monitor at all.</summary>
        public const string NoMonitorSetting = "0";

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
            // "0" says the same thing as "headless": no monitor at all. It reads as "none" beside
            // the numbers that name a monitor, and it is quicker to type.
            if (trimmed == NoMonitorSetting)
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
        /// This machine's monitors in the order the variable numbers them: left to right, so
        /// monitor 1 is the leftmost. Two monitors at the same horizontal position, one above the
        /// other, come out top first.
        ///
        /// The order is by position rather than the order Screen.AllScreens happens to return,
        /// which is the order of the display drivers and means nothing a developer can see. A
        /// developer reads a number off the picture of their monitors, so the number has to follow
        /// the picture.
        ///
        /// This is still NOT the number Windows Settings prints on each monitor. Windows does not
        /// document how the Settings app makes those labels, and neither the AllScreens order nor
        /// the \\.\DISPLAY&lt;n&gt; device name nor the display-config path order reproduces them: on
        /// one three-monitor machine Windows Settings said 1 (primary, centre), 2 (right) and
        /// 3 (left), while left to right is 1 (left), 2 (primary, centre) and 3 (right). So Bloom
        /// counts left to right, which a developer can work out from the arrangement they see,
        /// and DescribeChoice writes the whole mapping to the log.
        /// </summary>
        public static Screen[] MonitorsLeftToRight()
        {
            return Screen
                .AllScreens.OrderBy(screen => screen.Bounds.Left)
                .ThenBy(screen => screen.Bounds.Top)
                .ToArray();
        }

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
                return MonitorsLeftToRight()[oneBasedMonitor - 1];
            }
            return Screen.PrimaryScreen;
        }

        /// <summary>
        /// One line for the log saying what the variable said and what this run did with it,
        /// naming every monitor by position and size.
        ///
        /// This exists because the number in the variable counts left to right, which is NOT the
        /// number Windows Settings prints beside each display; see MonitorsLeftToRight. A developer
        /// who reads a number off Windows Settings therefore gets a different monitor, and nothing
        /// on screen says so. The log line is what lets them see which monitor Bloom actually
        /// chose, and work out the number they want.
        /// </summary>
        public static string DescribeChoice()
        {
            var raw = Environment.GetEnvironmentVariable(VariableName);
            var monitors = string.Join(
                ", ",
                MonitorsLeftToRight()
                    .Select(
                        (screen, zeroBased) =>
                            $"{zeroBased + 1}=({screen.Bounds.X},{screen.Bounds.Y}) "
                            + $"{screen.Bounds.Width}x{screen.Bounds.Height}"
                            + (screen.Primary ? " primary" : "")
                    )
            );
            var what = GetChoice() switch
            {
                Choice.OffEveryMonitor => "every window goes off every monitor",
                Choice.OnTheChosenMonitor =>
                    $"every window goes on the monitor at {GetChosenMonitor().Bounds}",
                _ => "Bloom places its windows as it always does",
            };
            return $"{VariableName}={(raw == null ? "(not set)" : $"'{raw}'")}: {what}. "
                + $"The monitors this process sees, numbered left to right as this variable "
                + $"numbers them (which is NOT how Windows Settings numbers them): {monitors}.";
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
