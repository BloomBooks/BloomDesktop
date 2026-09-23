using System;
using Bloom;
using NUnit.Framework;

namespace BloomTests
{
    [TestFixture]
    public class ShellTests
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
        }

        [Test]
        public void ShouldShowPortSummaryInWindowTitle_TracksAutomationFlag()
        {
            Program.ParseStartupPortArguments(
                Array.Empty<string>(),
                out var noAutomationErrorMessage
            );

            Assert.That(noAutomationErrorMessage, Is.Null);
            Assert.That(Shell.ShouldShowPortSummaryInWindowTitle(), Is.False);

            Program.ParseStartupPortArguments(
                new[] { "--automation" },
                out var automationErrorMessage
            );

            Assert.That(automationErrorMessage, Is.Null);
            Assert.That(Shell.ShouldShowPortSummaryInWindowTitle(), Is.True);
        }
    }
}
