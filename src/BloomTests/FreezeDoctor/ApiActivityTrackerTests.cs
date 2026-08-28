using System;
using System.Linq;
using Bloom.FreezeDoctor;
using NUnit.Framework;

namespace BloomTests.FreezeDoctor
{
    /// <summary>
    /// What Bloom tells the Freeze Doctor about the requests it has in flight.
    ///
    /// This is the richest thing Bloom knows about its own trouble: in a freeze, which requests are stuck,
    /// for how long, and which of them are queued behind a lock another one is holding. The tests ask for
    /// everything in flight however brief, since a request they have just started has run for no time at
    /// all; in production the threshold is a couple of seconds, below which a request is only normal
    /// traffic.
    /// </summary>
    [TestFixture]
    public class ApiActivityTrackerTests
    {
        /// <summary>
        /// Everything in flight, regardless of how briefly. The table is process-wide static, so every test
        /// here disposes what it starts.
        ///
        /// The threshold is a shade below zero because the filter is "longer THAN", and a request begun in
        /// this same millisecond has run for exactly nothing. Production asks for a couple of seconds, where
        /// the distinction cannot arise.
        /// </summary>
        private static string[] Everything() =>
            ApiActivityTracker.DescribeStuckRequests(TimeSpan.FromMilliseconds(-1));

        [Test]
        public void A_request_is_named_with_its_path_and_thread()
        {
            Assert.That(
                Everything(),
                Is.Empty,
                "sanity: nothing should be in flight before we start"
            );

            using (ApiActivityTracker.Begin("api/publish/epub/save"))
            {
                var described = Everything();

                Assert.That(described.Length, Is.EqualTo(1));
                Assert.That(described[0], Does.Contain("api/publish/epub/save"));
                Assert.That(
                    described[0],
                    Does.Contain("OS thread"),
                    "the OS thread id is what joins this request to its stack in the dump"
                );
            }

            Assert.That(
                Everything(),
                Is.Empty,
                "disposing must remove it, or a healthy Bloom looks stuck"
            );
        }

        [Test]
        public void The_lock_a_request_wants_is_named_and_then_the_one_it_holds()
        {
            using (var activity = ApiActivityTracker.Begin("api/publish/bloompub/updatepreview"))
            {
                Assert.That(
                    Everything()[0],
                    Does.Not.Contain("lock"),
                    "sanity: a request says nothing about locks until it asks for one"
                );

                activity.NoteWaitingForLock("the thumbnail/preview lock");
                Assert.That(
                    Everything()[0],
                    Does.Contain("waiting for the thumbnail/preview lock")
                );

                activity.NoteHoldingLock("the thumbnail/preview lock");
                var holding = Everything()[0];
                Assert.That(holding, Does.Contain("holding the thumbnail/preview lock"));
                Assert.That(
                    holding,
                    Does.Not.Contain("waiting for"),
                    "holding replaces waiting; a request cannot be doing both"
                );
            }
        }

        [Test]
        public void Longest_running_comes_first()
        {
            using (ApiActivityTracker.Begin("api/first"))
            using (ApiActivityTracker.Begin("api/second"))
            {
                var described = Everything();

                Assert.That(described.Length, Is.EqualTo(2));
                Assert.That(
                    described[0],
                    Does.Contain("api/first"),
                    "the one stuck longest is the one a reader wants at the top"
                );
            }
        }

        [Test]
        public void A_flood_of_requests_is_capped_and_says_how_many_it_dropped()
        {
            // A Bloom in real trouble can have a great many in flight. The list has to stop somewhere, but
            // stopping silently would let a report look complete when it was truncated.
            var scopes = Enumerable
                .Range(0, 25)
                .Select(i => ApiActivityTracker.Begin($"api/request/{i:D2}"))
                .ToList();
            try
            {
                var described = Everything();

                Assert.That(described.Length, Is.EqualTo(21), "twenty requests plus the note");
                Assert.That(described[20], Does.Contain("5 further request(s) in flight"));
            }
            finally
            {
                foreach (var scope in scopes)
                    scope.Dispose();
            }

            Assert.That(Everything(), Is.Empty);
        }

        [Test]
        public void Nothing_in_flight_returns_the_shared_empty_array()
        {
            // Not a curiosity: the session record compares this by reference, and Bloom skips writing the
            // file when nothing has changed. A fresh empty array each time would make an idle Bloom look
            // different from itself and rewrite the session every ten seconds for the life of the process.
            Assert.That(Everything(), Is.Empty, "sanity: nothing in flight");

            Assert.That(
                Everything(),
                Is.SameAs(Everything()),
                "the empty result must be the cached instance, or an idle Bloom rewrites its session file forever"
            );
        }

        [Test]
        public void A_request_below_the_threshold_is_not_worth_naming()
        {
            using (ApiActivityTracker.Begin("api/quick"))
            {
                Assert.That(
                    ApiActivityTracker.DescribeStuckRequests(TimeSpan.FromMinutes(1)),
                    Is.Empty,
                    "a request that has just started is normal traffic, not evidence"
                );
                Assert.That(Everything().Length, Is.EqualTo(1), "sanity: it IS in flight");
            }
        }
    }
}
