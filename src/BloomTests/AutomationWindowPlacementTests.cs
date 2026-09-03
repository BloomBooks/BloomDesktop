using System;
using System.Linq;
using Bloom;
using NUnit.Framework;
using Choice = Bloom.AutomationWindowPlacement.Choice;

namespace BloomTests
{
    /// <summary>
    /// Covers how BLOOM_AUTOMATION_MONITOR is read. These call
    /// AutomationWindowPlacement.Parse directly, so they need neither the environment variable
    /// nor a machine with a particular set of monitors: the number of monitors is an argument.
    /// </summary>
    [TestFixture]
    public class AutomationWindowPlacementTests
    {
        /// <summary>
        /// ParseStartupPortArguments writes into Program statics that live for the rest of the
        /// test run; re-parse empty args after each test to restore the defaults (the method
        /// resets them all on entry). Same rationale as the ProgramTests TearDown.
        /// </summary>
        [TearDown]
        public void TearDown()
        {
            Program.ParseStartupPortArguments(Array.Empty<string>(), out _);
            Environment.SetEnvironmentVariable(AutomationWindowPlacement.VariableName, null);
        }

        [TestCase(null, TestName = "Parse_VariableNotSet_PlacesWindowsNormally")]
        [TestCase("", TestName = "Parse_VariableEmpty_PlacesWindowsNormally")]
        [TestCase("   ", TestName = "Parse_VariableAllSpaces_PlacesWindowsNormally")]
        public void Parse_NothingAsked_PlacesWindowsNormally(string setting)
        {
            Assert.That(
                AutomationWindowPlacement.Parse(setting, 3, out var monitor),
                Is.EqualTo(Choice.AsBloomNormallyWould)
            );
            Assert.That(monitor, Is.EqualTo(0));
        }

        [TestCase("headless")]
        [TestCase("HEADLESS")]
        [TestCase("Headless")]
        [TestCase("  headless  ")]
        public void Parse_Headless_GoesOffEveryMonitor(string setting)
        {
            Assert.That(
                AutomationWindowPlacement.Parse(setting, 3, out var monitor),
                Is.EqualTo(Choice.OffEveryMonitor)
            );
            Assert.That(monitor, Is.EqualTo(0), "No monitor was chosen, so none is reported.");
        }

        [TestCase("1", 1)]
        [TestCase("2", 2)]
        [TestCase("3", 3)]
        [TestCase(" 2 ", 2)]
        public void Parse_MonitorThatExists_GoesOnThatMonitor(string setting, int expected)
        {
            Assert.That(
                AutomationWindowPlacement.Parse(setting, 3, out var monitor),
                Is.EqualTo(Choice.OnTheChosenMonitor)
            );
            Assert.That(monitor, Is.EqualTo(expected));
        }

        /// <summary>
        /// A monitor this machine does not have, a zero or negative index, and a typo all mean the
        /// same thing: nobody asked for anything Bloom can honour, so it places its windows
        /// normally and the developer sees the window. See the remarks on Parse.
        /// </summary>
        [TestCase("4", TestName = "Parse_MonitorBeyondTheLast_PlacesWindowsNormally")]
        [TestCase("0", TestName = "Parse_ZeroMonitor_PlacesWindowsNormally")]
        [TestCase("-1", TestName = "Parse_NegativeMonitor_PlacesWindowsNormally")]
        [TestCase("headles", TestName = "Parse_HeadlessMisspelt_PlacesWindowsNormally")]
        [TestCase("true", TestName = "Parse_Nonsense_PlacesWindowsNormally")]
        [TestCase("2.5", TestName = "Parse_NotAWholeNumber_PlacesWindowsNormally")]
        public void Parse_UnusableValue_PlacesWindowsNormally(string setting)
        {
            Assert.That(
                AutomationWindowPlacement.Parse(setting, 3, out var monitor),
                Is.EqualTo(Choice.AsBloomNormallyWould)
            );
            Assert.That(monitor, Is.EqualTo(0));
        }

        [Test]
        public void Parse_OnlyOneMonitor_TakesTheFirstAndRefusesTheSecond()
        {
            Assert.That(
                AutomationWindowPlacement.Parse("1", 1, out _),
                Is.EqualTo(Choice.OnTheChosenMonitor)
            );
            Assert.That(
                AutomationWindowPlacement.Parse("2", 1, out _),
                Is.EqualTo(Choice.AsBloomNormallyWould),
                "Monitor 2 does not exist on a one-monitor machine."
            );
        }

        /// <summary>
        /// The variable is only for automation runs. A developer who leaves it set in their shell
        /// must still get an ordinary, visible Bloom when they start one themselves.
        /// </summary>
        [Test]
        public void GetChoice_WithoutTheAutomationFlag_IgnoresTheVariable()
        {
            Environment.SetEnvironmentVariable(
                AutomationWindowPlacement.VariableName,
                AutomationWindowPlacement.HeadlessSetting
            );

            Program.ParseStartupPortArguments(Array.Empty<string>(), out var errorMessage);
            Assert.That(errorMessage, Is.Null);
            Assert.That(
                Program.StartupAutomation,
                Is.False,
                "Sanity check: this test needs a run that is NOT automation."
            );

            Assert.That(
                AutomationWindowPlacement.GetChoice(),
                Is.EqualTo(Choice.AsBloomNormallyWould)
            );
            Assert.That(AutomationWindowPlacement.IsOffEveryMonitor, Is.False);
        }

        [Test]
        public void GetChoice_WithTheAutomationFlag_ObeysTheVariable()
        {
            Environment.SetEnvironmentVariable(
                AutomationWindowPlacement.VariableName,
                AutomationWindowPlacement.HeadlessSetting
            );

            Program.ParseStartupPortArguments(new[] { "--automation" }, out var errorMessage);
            Assert.That(errorMessage, Is.Null);
            Assert.That(Program.StartupAutomation, Is.True, "Sanity check.");

            Assert.That(AutomationWindowPlacement.GetChoice(), Is.EqualTo(Choice.OffEveryMonitor));
            Assert.That(AutomationWindowPlacement.IsOffEveryMonitor, Is.True);
        }

        /// <summary>
        /// The window has to end up wholly off every monitor, and at a coordinate Windows will
        /// honour. This runs against however many monitors the machine has, which is the point:
        /// the answer has to hold on the CI runner's one screen and on a developer's several.
        /// </summary>
        [Test]
        public void GetBoundsOffEveryMonitor_IsWhollyOffEveryMonitorAndWithinWindowsLimit()
        {
            var bounds = AutomationWindowPlacement.GetBoundsOffEveryMonitor();

            Assert.That(bounds.Width, Is.GreaterThan(0), "Sanity check: a real size.");
            Assert.That(bounds.Height, Is.GreaterThan(0), "Sanity check: a real size.");
            Assert.That(
                bounds.Left,
                Is.GreaterThanOrEqualTo(-32000),
                "Windows does not honour a position further left than this."
            );
            foreach (var screen in System.Windows.Forms.Screen.AllScreens)
            {
                Assert.That(
                    bounds.IntersectsWith(screen.Bounds),
                    Is.False,
                    $"The window overlaps the monitor at {screen.Bounds}."
                );
            }

            // The desktop can make the window as much as four times as wide as this process asked
            // for, because Windows scales a monitor by at most 400%. So a clearance of one width
            // is not enough: measure it, and require four. See GetBoundsOffEveryMonitor.
            var leftmostX = System.Windows.Forms.Screen.AllScreens.Min(screen =>
                screen.Bounds.Left
            );
            Assert.That(
                leftmostX - bounds.Left,
                Is.GreaterThanOrEqualTo(bounds.Width * 4),
                "A window four times this wide would still have to clear the leftmost monitor."
            );
        }
    }
}
