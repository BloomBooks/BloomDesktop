using System;
using Bloom.ToPalaso;
using NUnit.Framework;
using SIL.Reporting;

namespace BloomTests.ToPalaso
{
    /// <summary>
    /// QuietStatusProgress wraps another progress for a loop that would otherwise write a status
    /// line per item (BL-16893). It must keep exactly the status lines out of the progress it
    /// wraps -- while still writing them to the log, since a failure report needs to say which
    /// item the run had reached -- and pass everything else, including the percent, straight
    /// through. The log has to be the main one: a problem report carries that and not the
    /// separate minor-events buffer.
    /// </summary>
    [TestFixture]
    public class QuietStatusProgressTests
    {
        [Test]
        public void WriteStatus_IsNotPassedOn_ButIsLogged()
        {
            Logger.Init();
            try
            {
                var inner = new RecordingProgress();
                var quiet = new QuietStatusProgress(inner);
                // Sanity check: nothing of ours is in the log before we write it.
                Assert.That(
                    Logger.LogText,
                    Does.Not.Contain("Reading metadata from a.png"),
                    "test setup: the log already mentioned this item"
                );

                quiet.WriteStatus("Reading metadata from {0}", "a.png");

                // Not passed on: a line per image is what fills the progress dialog.
                Assert.That(inner.Statuses, Is.Empty);
                // But not lost either: "which image was it on?" is what a failure report needs.
                Assert.That(Logger.LogText, Does.Contain("Reading metadata from a.png"));
            }
            finally
            {
                Logger.ShutDown();
            }
        }

        [Test]
        public void WriteStatus_WithBracesAndNoArguments_LogsItRatherThanThrowing()
        {
            Logger.Init();
            try
            {
                var quiet = new QuietStatusProgress(new RecordingProgress());

                // string.Format would throw on this; a progress report must not.
                Assert.DoesNotThrow(() => quiet.WriteStatus("Shrinking {not a placeholder}"));

                Assert.That(Logger.LogText, Does.Contain("Shrinking {not a placeholder}"));
            }
            finally
            {
                Logger.ShutDown();
            }
        }

        [Test]
        public void OtherWrites_PassThrough()
        {
            var inner = new RecordingProgress();
            var quiet = new QuietStatusProgress(inner);
            var exception = new InvalidOperationException("boom");

            quiet.WriteMessage("message {0}", 1);
            quiet.WriteMessageWithColor("red", "colored {0}", 2);
            quiet.WriteWarning("warning {0}", 3);
            quiet.WriteError("error {0}", 4);
            quiet.WriteVerbose("verbose {0}", 5);
            quiet.WriteException(exception);

            Assert.That(inner.Messages, Is.EqualTo(new[] { "message 1", "colored 2" }));
            Assert.That(inner.Warnings, Is.EqualTo(new[] { "warning 3" }));
            Assert.That(inner.Errors, Is.EqualTo(new[] { "error 4" }));
            Assert.That(inner.Verbose, Is.EqualTo(new[] { "verbose 5" }));
            Assert.That(inner.Exceptions, Is.EqualTo(new[] { exception }));
        }

        [Test]
        public void Percent_ReachesTheInnerIndicator()
        {
            var inner = new RecordingProgress();
            var quiet = new QuietStatusProgress(inner);

            quiet.ProgressIndicator.PercentCompleted = 40;
            quiet.ProgressIndicator.PercentCompleted = 100;

            Assert.That(inner.Indicator.Percents, Is.EqualTo(new[] { 40, 100 }));
        }

        [Test]
        public void Flags_AreSharedWithTheInnerProgress()
        {
            var inner = new RecordingProgress();
            var quiet = new QuietStatusProgress(inner);

            quiet.CancelRequested = true;
            quiet.ErrorEncountered = true;
            quiet.ShowVerbose = true;

            Assert.That(inner.CancelRequested, Is.True);
            Assert.That(inner.ErrorEncountered, Is.True);
            Assert.That(inner.ShowVerboseWasSet, Is.True);
            // and reading them goes to the same place
            inner.CancelRequested = false;
            Assert.That(quiet.CancelRequested, Is.False);
        }

        [Test]
        public void NullInner_IsRejected()
        {
            Assert.Throws<ArgumentNullException>(() => new QuietStatusProgress(null));
        }
    }
}
