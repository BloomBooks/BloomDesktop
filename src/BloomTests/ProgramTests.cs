using Bloom;
using NUnit.Framework;

namespace BloomTests
{
    [TestFixture]
    public class ProgramTests
    {
        /// <summary>
        /// ParseStartupPortArguments stores its results in Program statics (StartupAutomation etc.)
        /// which live for the rest of the test run. Re-parse empty args after each test to restore
        /// the defaults; the method resets all of them on entry. Without this, the "--automation"
        /// tests here left StartupAutomation=true for every later fixture, which (among other
        /// things) made BloomServer print automation banners in unrelated tests' output.
        /// </summary>
        [TearDown]
        public void TearDown()
        {
            Program.ParseStartupPortArguments(System.Array.Empty<string>(), out _);
        }

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
        public void ParseStartupPortArguments_LeavesLabelNullWithoutExplicitLabel()
        {
            var remainingArgs = Program.ParseStartupPortArguments(
                new[] { @"C:\Temp\Example.bloomcollection" },
                out var errorMessage
            );

            Assert.That(errorMessage, Is.Null);
            Assert.That(Program.StartupLabel, Is.Null);
            Assert.That(remainingArgs, Is.EqualTo(new[] { @"C:\Temp\Example.bloomcollection" }));
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
    }
}
