using Bloom;
using NUnit.Framework;
using Sentry;
using Sentry.Protocol;

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

        // --- IsBenignUnobservedTaskSocketNoise: the Sentry BeforeSend filter for
        //     BLOOM-DESKTOP-EQ4 / -E4J / -E9K ---

        private static SentryException MakeException(
            string type,
            string value = null,
            string mechanismType = null
        )
        {
            var exception = new SentryException { Type = type, Value = value };
            if (mechanismType != null)
                exception.Mechanism = new Mechanism { Type = mechanismType, Handled = false };
            return exception;
        }

        private static SentryEvent MakeEvent(params SentryException[] exceptions)
        {
            return new SentryEvent { SentryExceptions = exceptions };
        }

        [Test]
        public void IsBenignUnobservedTaskSocketNoise_DropsUnobservedSocketAbort()
        {
            // The shape Sentry actually serialized for BLOOM-DESKTOP-EQ4: an inner SocketException,
            // wrapped in an AggregateException carrying the UnobservedTaskException mechanism.
            var sentryEvent = MakeEvent(
                MakeException(
                    "System.Net.Sockets.SocketException",
                    "The I/O operation has been aborted because of either a thread exit or an application request."
                ),
                MakeException(
                    "System.AggregateException",
                    "A Task's exception(s) were not observed...",
                    "UnobservedTaskException"
                )
            );

            // Sanity check the setup before asserting the method's behavior.
            Assert.That(
                sentryEvent.SentryExceptions,
                Is.Not.Empty,
                "test setup should produce an exception chain"
            );

            Assert.That(Program.IsBenignUnobservedTaskSocketNoise(sentryEvent), Is.True);
        }

        [Test]
        public void IsBenignUnobservedTaskSocketNoise_DropsUnobservedIoAbort()
        {
            var sentryEvent = MakeEvent(
                MakeException("System.IO.IOException", "aborted"),
                MakeException("System.AggregateException", null, "UnobservedTaskException")
            );

            Assert.That(Program.IsBenignUnobservedTaskSocketNoise(sentryEvent), Is.True);
        }

        [TestCase("System.OperationCanceledException")]
        [TestCase("System.Threading.Tasks.TaskCanceledException")]
        public void IsBenignUnobservedTaskSocketNoise_DropsUnobservedCancellation(
            string cancellationType
        )
        {
            // A shutdown race can cancel the fire-and-forget send instead of aborting the socket;
            // that is still benign teardown noise, so both cancellation types are dropped.
            var sentryEvent = MakeEvent(
                MakeException(cancellationType, "A task was canceled."),
                MakeException("System.AggregateException", null, "UnobservedTaskException")
            );

            Assert.That(Program.IsBenignUnobservedTaskSocketNoise(sentryEvent), Is.True);
        }

        [Test]
        public void IsBenignUnobservedTaskSocketNoise_DropsRegardlessOfLocalizedMessage()
        {
            // The message is localized by the user's OS (this is the Spanish variant,
            // BLOOM-DESKTOP-E4J). The filter must decide on TYPE + mechanism, not text.
            var sentryEvent = MakeEvent(
                MakeException(
                    "System.Net.Sockets.SocketException",
                    "Se ha anulado la operacion de E/S debido a la salida del subproceso o a una solicitud de la aplicacion."
                ),
                MakeException("System.AggregateException", null, "UnobservedTaskException")
            );

            Assert.That(Program.IsBenignUnobservedTaskSocketNoise(sentryEvent), Is.True);
        }

        [Test]
        public void IsBenignUnobservedTaskSocketNoise_KeepsUnobservedTaskThatIsNotSocketNoise()
        {
            // A genuine unobserved-Task bug (not a socket/IO abort) must still be reported.
            var sentryEvent = MakeEvent(
                MakeException("System.NullReferenceException", "Object reference not set..."),
                MakeException("System.AggregateException", null, "UnobservedTaskException")
            );

            Assert.That(Program.IsBenignUnobservedTaskSocketNoise(sentryEvent), Is.False);
        }

        [Test]
        public void IsBenignUnobservedTaskSocketNoise_KeepsSocketExceptionWithoutUnobservedMechanism()
        {
            // A SocketException reported through some other (e.g. handled) path is not this noise.
            var sentryEvent = MakeEvent(
                MakeException("System.Net.Sockets.SocketException", "connection reset")
            );

            Assert.That(Program.IsBenignUnobservedTaskSocketNoise(sentryEvent), Is.False);
        }

        [Test]
        public void IsBenignUnobservedTaskSocketNoise_KeepsEventWithNoExceptions()
        {
            Assert.That(Program.IsBenignUnobservedTaskSocketNoise(new SentryEvent()), Is.False);
        }
    }
}
