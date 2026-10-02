using Bloom.TeamCollection;
using NUnit.Framework;

namespace BloomTests.TeamCollection
{
    /// <summary>
    /// Tests for the "wait for a second failure before believing it" policy that keeps a
    /// transient network blip from disconnecting a Team Collection that is actually working.
    /// See BL-16729.
    /// </summary>
    [TestFixture]
    public class ConnectionFailureTrackerTests
    {
        private ConnectionFailureTracker _tracker;

        [SetUp]
        public void Setup()
        {
            _tracker = new ConnectionFailureTracker();
        }

        private static TeamCollectionMessage Problem(string l10nId)
        {
            return new TeamCollectionMessage(
                MessageAndMilestoneType.Error,
                l10nId,
                "some English text"
            );
        }

        [Test]
        public void RecordResult_Success_ReportsNoProblem()
        {
            Assert.That(_tracker.RecordResult(null), Is.False);
        }

        [Test]
        public void RecordResult_SingleFailure_DoesNotYetConclude()
        {
            Assert.That(
                _tracker.RecordResult(Problem("TeamCollection.NoNetwork")),
                Is.False,
                "one failure should not be enough; it is very often a transient blip"
            );
        }

        [Test]
        public void RecordResult_TwoSameFailures_Concludes()
        {
            // Sanity check that the first one really did not conclude.
            Assert.That(_tracker.RecordResult(Problem("TeamCollection.NoNetwork")), Is.False);

            Assert.That(_tracker.RecordResult(Problem("TeamCollection.NoNetwork")), Is.True);
        }

        [Test]
        public void RecordResult_SuccessBetweenFailures_StartsOver()
        {
            Assert.That(_tracker.RecordResult(Problem("TeamCollection.NoNetwork")), Is.False);
            Assert.That(_tracker.RecordResult(null), Is.False);

            Assert.That(
                _tracker.RecordResult(Problem("TeamCollection.NoNetwork")),
                Is.False,
                "the intervening success means these two failures were not consecutive"
            );
        }

        [Test]
        public void RecordResult_DifferentKindsOfFailure_StartsOver()
        {
            Assert.That(_tracker.RecordResult(Problem("TeamCollection.NoNetwork")), Is.False);

            Assert.That(
                _tracker.RecordResult(Problem("TeamCollection.MissingRepo")),
                Is.False,
                "two different problems are two transients, not one sustained outage"
            );
            // ...but two of the second kind in a row still counts.
            Assert.That(_tracker.RecordResult(Problem("TeamCollection.MissingRepo")), Is.True);
        }

        [Test]
        public void Reset_ForgetsPreviousFailure()
        {
            Assert.That(_tracker.RecordResult(Problem("TeamCollection.NoNetwork")), Is.False);

            _tracker.Reset();

            Assert.That(
                _tracker.RecordResult(Problem("TeamCollection.NoNetwork")),
                Is.False,
                "Reset should have discarded the earlier failure"
            );
        }

        [Test]
        public void RecordResult_ManyFailures_KeepsConcluding()
        {
            _tracker.RecordResult(Problem("TeamCollection.NoNetwork"));
            Assert.That(_tracker.RecordResult(Problem("TeamCollection.NoNetwork")), Is.True);
            Assert.That(
                _tracker.RecordResult(Problem("TeamCollection.NoNetwork")),
                Is.True,
                "a continuing outage should keep reporting as one"
            );
        }
    }
}
