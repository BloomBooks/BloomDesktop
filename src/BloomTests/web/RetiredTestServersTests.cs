// Copyright (c) 2026 SIL International
// This software is licensed under the MIT License (http://opensource.org/licenses/MIT)

using Bloom.Api;
using Bloom.Book;
using NUnit.Framework;

namespace BloomTests.web
{
    /// <summary>
    /// Guards the bookkeeping behind RetiredTestServers: that a retired server's listener is
    /// closed eventually, and that only a handful are held open at a time. The second is the one
    /// that would fail silently — EnsureListening only tries 20 ports before giving up and calling
    /// ProgramExit.Exit, and a run retires about 55 servers, so letting the queue grow would take
    /// the suite out somewhere unrelated and baffling.
    ///
    /// Two things these tests deliberately do NOT do, because both would re-create the very race
    /// the scheme exists to avoid (Devin caught the first version of this fixture doing both):
    ///
    /// - They drive their own <see cref="RetiredServerQueue"/> rather than the one the run is
    ///   using, so nothing here can close a listener that some other fixture retired moments ago.
    /// - They retire servers that never listened. A server that has served a response and is then
    ///   closed microseconds later is exactly the unsafe timing; a server with no listener has
    ///   nothing to close and nothing to interrupt, and the queue's behaviour is the same either
    ///   way, since it counts servers rather than ports.
    ///
    /// That the real queue is wired to a cap the port budget can afford is checked separately,
    /// below, and that ports genuinely last a whole run is what the full suite demonstrates.
    /// </summary>
    [TestFixture]
    public class RetiredTestServersTests
    {
        private static BloomServer AServerThatNeverListened() =>
            new BloomServer(new BookSelection());

        [Test]
        public void Retire_HoldsNoMoreThanTheCap()
        {
            const int cap = 3;
            var queue = new RetiredServerQueue(cap);

            for (var i = 0; i < cap + 4; i++)
            {
                queue.Retire(AServerThatNeverListened());
                Assert.That(
                    queue.CountAwaitingClose,
                    Is.LessThanOrEqualTo(cap),
                    $"after retiring {i + 1} servers, more than the cap were still waiting; in the "
                        + "real queue each of those holds a port, and a run retires about 55"
                );
            }
        }

        [Test]
        public void Retire_BelowTheCap_ClosesNothingYet()
        {
            var queue = new RetiredServerQueue(3);

            queue.Retire(AServerThatNeverListened());
            queue.Retire(AServerThatNeverListened());

            Assert.That(
                queue.CountAwaitingClose,
                Is.EqualTo(2),
                "below the cap, retiring should hold on to servers rather than closing them — "
                    + "the waiting is the whole point"
            );
        }

        [Test]
        public void CloseAllNow_LeavesNothingWaiting()
        {
            var queue = new RetiredServerQueue(3);
            queue.Retire(AServerThatNeverListened());
            queue.Retire(AServerThatNeverListened());
            Assert.That(
                queue.CountAwaitingClose,
                Is.EqualTo(2),
                "SANITY: the servers we just retired should be waiting"
            );

            queue.CloseAllNow();

            Assert.That(queue.CountAwaitingClose, Is.EqualTo(0));
        }

        [Test]
        public void CloseAllNow_WhenNothingIsWaiting_DoesNothing()
        {
            var queue = new RetiredServerQueue(3);
            Assert.DoesNotThrow(() => queue.CloseAllNow());
            Assert.That(queue.CountAwaitingClose, Is.EqualTo(0));
        }

        [Test]
        public void Retire_GivenNull_DoesNothing()
        {
            var queue = new RetiredServerQueue(3);
            Assert.DoesNotThrow(() => queue.Retire(null));
            Assert.That(queue.CountAwaitingClose, Is.EqualTo(0));
        }

        /// <summary>
        /// The cap and the port budget are declared in different files — RetiredTestServers and
        /// BloomServer.EnsureListening — so nothing but a test keeps them in a sensible relation.
        ///
        /// And the relation that matters is not this run's, it is the machine's: ports are
        /// machine-wide, we habitually run suites in several worktrees at once, and a run that
        /// cannot find a port does not fail a test — EnsureListening gives up and calls
        /// ProgramExit.Exit, with nothing in the output to say it was about ports. Devin raised
        /// exactly this, and it is why the cap came down from 5 to 3.
        /// </summary>
        [Test]
        public void TheRealCapLeavesRoomForSeveralConcurrentTestRuns()
        {
            const int concurrentRunsToAllowFor = 4;
            // The ones waiting to be closed, plus the one the run is actually using.
            var portsHeldPerRun = RetiredTestServers.kMaxAwaitingClose + 1;

            Assert.That(
                portsHeldPerRun * concurrentRunsToAllowFor,
                Is.LessThanOrEqualTo(RetiredTestServers.kPortsEnsureListeningTries),
                $"holding {portsHeldPerRun} ports per run leaves room for fewer than "
                    + $"{concurrentRunsToAllowFor} concurrent runs in EnsureListening's "
                    + $"{RetiredTestServers.kPortsEnsureListeningTries} candidates; the run that "
                    + "loses does not fail a test, it calls ProgramExit.Exit"
            );
        }
    }
}
