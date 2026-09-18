using System;
using Bloom.ToPalaso;
using NUnit.Framework;

namespace BloomTests.ToPalaso
{
    /// <summary>
    /// QuietStatusProgress wraps another progress for a loop that would otherwise write a status
    /// line per item (BL-16893). It must drop exactly the status lines and pass everything else,
    /// including the percent, straight through.
    /// </summary>
    [TestFixture]
    public class QuietStatusProgressTests
    {
        [Test]
        public void WriteStatus_IsDropped()
        {
            var inner = new RecordingProgress();
            var quiet = new QuietStatusProgress(inner);

            quiet.WriteStatus("Reading metadata from {0}", "a.png");

            Assert.That(inner.Statuses, Is.Empty);
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
