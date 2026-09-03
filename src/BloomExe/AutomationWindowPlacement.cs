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
        /// area, positioned below every monitor and in line with the primary, so that not one
        /// pixel of it is on any screen and it still gets the primary's scale factor.
        ///
        /// The window is moved rather than minimized or hidden because a minimized WebView2 stops
        /// painting: screenshots come back blank and the layout is the wrong size. An off-screen
        /// window of the normal size keeps painting, so a test sees exactly what a user would.
        /// </summary>
        public static Rectangle GetBoundsOffEveryMonitor()
        {
            var workingArea = Screen.PrimaryScreen.WorkingArea;
            var size = workingArea.Size;

            // The window goes straight DOWN from the primary monitor, not off to the left, and the
            // reason is the DPI of the monitor Windows thinks the window is on.
            //
            // Windows gives a window the scale factor of the monitor nearest to it, and this
            // process asked for a size in the primary monitor's own scaled pixels. Put the window
            // out to the left and the nearest monitor is the leftmost one, whose scale factor is
            // very likely not the primary's: on a machine whose primary runs at 150% and whose
            // left monitor runs at 100%, a window meant to match the primary's 3840x2100 came out
            // 3840x2100 REAL pixels, taller than any monitor on the machine, and the page inside
            // it laid out at a viewport height no user could ever have. Keeping the window
            // directly under the primary, aligned with its left edge, keeps the primary the
            // nearest monitor on the layouts we have, so an off-screen window paints at the size
            // a visible one would.
            //
            // "On the layouts we have" is the real limit here, and it is worth stating plainly.
            // The primary is nearest only while no monitor sits below the primary in the same
            // band of x. A machine with one monitor stacked under another, at a different scale
            // factor, puts that lower monitor nearest instead, and the size comes out wrong in
            // exactly the way described above. Getting it right for every layout means asking
            // Windows for the scale factor of whichever monitor ends up nearest and scaling the
            // requested size by the ratio, which is more than this code does today. See "No way
            // to run the suite at a chosen monitor resolution and scale factor" in
            // src/BloomE2E/AUTOMATION-DEBT.md.
            //
            // How far down: far enough that the window clears every monitor even after Windows
            // scales it. This process's idea of the height can be out by the ratio of two scale
            // factors, and Windows scales a monitor by at most 400%, so a window this process
            // believes is H high covers at most 4H pixels of the desktop. Four heights below the
            // lowest monitor therefore clears them all, and the 1000 pixels on top of that keep
            // the two edges from meeting exactly. (An earlier version of this went left and left
            // one window width of room; on a 160% primary that left 27 pixels of a 1000-pixel
            // cushion, which is how the ratio came to be measured rather than guessed.)
            //
            // 32000 is as far down as a window may go: Windows still places a window there, and
            // anything past about 32768 runs into the 16-bit coordinates that some of the older
            // window messages still carry. On any real layout four heights is several thousand
            // pixels, well inside that. A downward run of monitors more than about 30000 pixels
            // tall would need a position that satisfies neither, and then the limit wins: a
            // window at a coordinate Windows will not honour is worse than one that overlaps a
            // monitor.
            const int farDownWindowsAllows = 32000;
            const int largestScaleFactorWindowsAllows = 4;
            var lowestY = Screen.AllScreens.Max(screen => screen.Bounds.Bottom);
            var y = Math.Min(
                farDownWindowsAllows,
                lowestY + (size.Height * largestScaleFactorWindowsAllows) + 1000
            );
            return new Rectangle(workingArea.X, y, size.Width, size.Height);
        }
    }
}
