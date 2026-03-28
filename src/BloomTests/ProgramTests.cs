using Bloom;
using NUnit.Framework;

namespace BloomTests
{
    [TestFixture]
    public class ProgramTests
    {
        [Test]
        public void ParseStartupPortArguments_RemovesPortsAndStoresExplicitValues()
        {
            var remainingArgs = Program.ParseStartupPortArguments(
                new[]
                {
                    "--automation",
                    "--vite-port",
                    "15173",
                    "--label=my-cool-feature",
                    @"C:\Temp\Example.bloomcollection",
                },
                out var errorMessage
            );

            Assert.That(errorMessage, Is.Null);
            Assert.That(Program.StartupAutomation, Is.True);
            Assert.That(Program.StartupVitePort, Is.EqualTo(15173));
            Assert.That(Program.StartupLabel, Is.EqualTo("my-cool-feature"));
            Assert.That(remainingArgs, Is.EqualTo(new[] { @"C:\Temp\Example.bloomcollection" }));
            Assert.That(WebView2Browser.RemoteDebuggingPort, Is.Null);
        }

        [Test]
        public void ParseStartupPortArguments_UsesAutomationFlagToBypassSingleInstance()
        {
            Program.ParseStartupPortArguments(
                new[] { "--automation" },
                out var automationErrorMessage
            );

            Assert.That(automationErrorMessage, Is.Null);
            Assert.That(Program.StartupAutomation, Is.True);
            Assert.That(Program.StartupRequestedPortSummary, Is.EqualTo("automation=true"));
        }

        [Test]
        public void ParseStartupPortArguments_VitePortAloneDoesNotEnableAutomation()
        {
            Program.ParseStartupPortArguments(
                new[] { "--vite-port", "15173" },
                out var viteErrorMessage
            );

            Assert.That(viteErrorMessage, Is.Null);
            Assert.That(Program.StartupAutomation, Is.False);
            Assert.That(Program.StartupRequestedPortSummary, Is.EqualTo("vitePort=15173"));
        }

        [Test]
        public void ParseStartupPortArguments_RejectsDuplicateAutomationArguments()
        {
            var remainingArgs = Program.ParseStartupPortArguments(
                new[] { "--automation", "--automation" },
                out var errorMessage
            );

            Assert.That(errorMessage, Is.EqualTo("Bloom only accepts one --automation argument."));
            Assert.That(remainingArgs, Is.Empty);
        }

        [Test]
        public void ParseStartupPortArguments_RejectsOutOfRangePorts()
        {
            var remainingArgs = Program.ParseStartupPortArguments(
                new[] { "--vite-port", "70000" },
                out var errorMessage
            );

            Assert.That(
                errorMessage,
                Is.EqualTo("Bloom requires --vite-port to be an integer from 1 to 65535.")
            );
            Assert.That(remainingArgs, Is.Empty);
        }

        [Test]
        public void ParseStartupPortArguments_RejectsLabelThatConsumesAnotherOption()
        {
            var remainingArgs = Program.ParseStartupPortArguments(
                new[] { "--label", "--help" },
                out var errorMessage
            );

            Assert.That(errorMessage, Is.EqualTo("Bloom requires a value after --label."));
            Assert.That(Program.StartupLabel, Is.Null);
            Assert.That(remainingArgs, Is.Empty);
        }

        [Test]
        public void ParseStartupPortArguments_RejectsEqualsLabelThatLooksLikeAnotherOption()
        {
            var remainingArgs = Program.ParseStartupPortArguments(
                new[] { "--label=--help" },
                out var errorMessage
            );

            Assert.That(errorMessage, Is.EqualTo("Bloom requires a value after --label."));
            Assert.That(Program.StartupLabel, Is.Null);
            Assert.That(remainingArgs, Is.Empty);
        }

        [TestCase("BloomDesktop", null, false, "/BloomDesktop/")]
        [TestCase("BloomDesktop", "testbranch", false, "/testbranch/")]
        [TestCase("BL-16014-MultipleDevExes", "BL-16014-MultipleDevExes", true, "/BL-16014-MultipleDevExes/")]
        [TestCase("BL-16014-MultipleDevExes", "testbranch", true, "/BL-16014-MultipleDevExes (testbranch)/")]
        public void FormatStartupLabel_UsesRepoAndBranchAsExpected(
            string repoLabel,
            string branchName,
            bool isGitWorktree,
            string expectedLabel
        )
        {
            Assert.That(
                Program.FormatStartupLabel(repoLabel, branchName, isGitWorktree),
                Is.EqualTo(expectedLabel)
            );
        }
    }
}
