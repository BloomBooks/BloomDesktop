using System;
using Bloom;
using NUnit.Framework;

namespace BloomTests
{
    [TestFixture]
    public class ShellTests
    {
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
