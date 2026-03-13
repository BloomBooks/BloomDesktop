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
                    "--http-port",
                    "19089",
                    "--cdp-port=19092",
                    "--vite-port",
                    "15173",
                    "--label=my-cool-feature",
                    @"C:\Temp\Example.bloomcollection",
                },
                out var errorMessage
            );

            Assert.That(errorMessage, Is.Null);
            Assert.That(Program.StartupHttpPort, Is.EqualTo(19089));
            Assert.That(Program.StartupCdpPort, Is.EqualTo(19092));
            Assert.That(Program.StartupVitePort, Is.EqualTo(15173));
            Assert.That(Program.StartupLabel, Is.EqualTo("my-cool-feature"));
            Assert.That(remainingArgs, Is.EqualTo(new[] { @"C:\Temp\Example.bloomcollection" }));
        }

        [Test]
        public void ParseStartupPortArguments_RejectsCdpPortInsideReservedHttpBlock()
        {
            var remainingArgs = Program.ParseStartupPortArguments(
                new[] { "--http-port", "19089", "--cdp-port", "19090" },
                out var errorMessage
            );

            Assert.That(errorMessage, Does.Contain("must not overlap the reserved HTTP block"));
            Assert.That(remainingArgs, Is.Empty);
        }

        [Test]
        public void ParseStartupPortArguments_RejectsDuplicatePortArguments()
        {
            var remainingArgs = Program.ParseStartupPortArguments(
                new[] { "--http-port", "19089", "--http-port", "19091" },
                out var errorMessage
            );

            Assert.That(errorMessage, Is.EqualTo("Bloom only accepts one --http-port argument."));
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
    }
}
