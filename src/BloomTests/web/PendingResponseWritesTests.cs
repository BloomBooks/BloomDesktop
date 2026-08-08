// Copyright (c) 2026 SIL International
// This software is licensed under the MIT License (http://opensource.org/licenses/MIT)

using System;
using System.Threading.Tasks;
using Bloom.Api;
using NUnit.Framework;

namespace BloomTests.web
{
    /// <summary>
    /// Tests for the bookkeeping that lets BloomServer.Dispose wait for response bodies that are
    /// still going out, and that keeps a failure at the end of a send from reaching the runtime.
    /// See PendingResponseWrites, and BL-16667 for what happens when it does reach the runtime.
    ///
    /// These drive Track() rather than SendAndClose(), so that they can decide exactly when a send
    /// finishes and how it ends, which a real HttpListener and a real client would not let them do.
    /// </summary>
    [TestFixture]
    public class PendingResponseWritesTests
    {
        /// <summary>
        /// PendingResponseWrites is deliberately static — shutdown has to be able to ask about
        /// every send in the process — so a send left over from an earlier test would make these
        /// assertions meaningless. Check we are starting from nothing.
        /// </summary>
        [SetUp]
        public void EnsureNothingIsInFlightBeforeWeStart()
        {
            if (!PendingResponseWrites.WaitUntilAllHaveFinished(TimeSpan.FromSeconds(10)))
                Assert.Fail(
                    "A response send from an earlier test was still in flight; this fixture's counts would be meaningless."
                );
            Assert.That(
                PendingResponseWrites.InFlightCount,
                Is.EqualTo(0),
                "sanity check on the starting state"
            );
        }

        [Test]
        public void Track_WhileSendIsUnfinished_CountsItAndMakesWaitingCallersWait()
        {
            var send = new TaskCompletionSource<bool>();
            var finished = false;

            PendingResponseWrites.Track(() => send.Task, () => finished = true);

            // Sanity checks on the state we are about to change.
            Assert.That(
                PendingResponseWrites.InFlightCount,
                Is.EqualTo(1),
                "the send should be counted from the moment it starts"
            );
            Assert.That(finished, Is.False, "nothing should have tidied up yet");
            Assert.That(
                PendingResponseWrites.WaitUntilAllHaveFinished(TimeSpan.FromMilliseconds(50)),
                Is.False,
                "a caller must not be told everything is done while a send is unfinished"
            );

            send.SetResult(true);

            Assert.That(
                PendingResponseWrites.WaitUntilAllHaveFinished(TimeSpan.FromSeconds(10)),
                Is.True,
                "the wait should end once the send finishes"
            );
            Assert.That(finished, Is.True, "the response should have been closed");
            Assert.That(PendingResponseWrites.InFlightCount, Is.EqualTo(0));
        }

        [Test]
        public void Track_SendFails_StillClosesTheResponseAndStopsCounting()
        {
            var send = new TaskCompletionSource<bool>();
            var finished = false;

            PendingResponseWrites.Track(() => send.Task, () => finished = true);
            Assert.That(
                PendingResponseWrites.InFlightCount,
                Is.EqualTo(1),
                "sanity check: the send should be counted while it is unfinished"
            );

            // This is the shape of the real failure: the listener was closed under a send that had
            // already been handed to the operating system.
            send.SetException(new ObjectDisposedException("System.Net.HttpRequestQueueV2Handle"));

            Assert.That(
                PendingResponseWrites.WaitUntilAllHaveFinished(TimeSpan.FromSeconds(10)),
                Is.True,
                "a failed send must stop being counted, or shutdown would wait for it forever"
            );
            Assert.That(finished, Is.True, "we should still try to close the response");
        }

        [Test]
        public void Track_SendThrowsBeforeItStarts_StillClosesTheResponseAndStopsCounting()
        {
            var finished = false;

            // The other half of the same race: the listener's handle was already gone, so the write
            // could not even be issued.
            PendingResponseWrites.Track(
                () => throw new ObjectDisposedException("System.Threading.ThreadPoolBoundHandle"),
                () => finished = true
            );

            Assert.That(
                PendingResponseWrites.WaitUntilAllHaveFinished(TimeSpan.FromSeconds(10)),
                Is.True
            );
            Assert.That(finished, Is.True, "we should still try to close the response");
            Assert.That(PendingResponseWrites.InFlightCount, Is.EqualTo(0));
        }

        [Test]
        public void Track_ClosingTheResponseThrows_DoesNotEscapeAndStopsCounting()
        {
            // Closing can fail for the same reason sending can, and this runs on an I/O completion
            // thread, where anything that got out would be unhandled and would kill the process.
            Assert.DoesNotThrow(() =>
                PendingResponseWrites.Track(
                    () => Task.CompletedTask,
                    () => throw new ObjectDisposedException("some handle")
                )
            );

            Assert.That(
                PendingResponseWrites.WaitUntilAllHaveFinished(TimeSpan.FromSeconds(10)),
                Is.True
            );
            Assert.That(PendingResponseWrites.InFlightCount, Is.EqualTo(0));
        }

        [Test]
        public void Track_SeveralSends_WaitEndsOnlyWhenTheLastOneFinishes()
        {
            var first = new TaskCompletionSource<bool>();
            var second = new TaskCompletionSource<bool>();
            PendingResponseWrites.Track(() => first.Task, () => { });
            PendingResponseWrites.Track(() => second.Task, () => { });
            Assert.That(
                PendingResponseWrites.InFlightCount,
                Is.EqualTo(2),
                "sanity check on the starting state"
            );

            first.SetResult(true);

            // Deliberately not asserting InFlightCount here: the first send's tidying-up runs on
            // another thread, so 2 and 1 are both legitimate readings at this instant. What must be
            // true is that a caller is not yet told everything is done.
            Assert.That(
                PendingResponseWrites.WaitUntilAllHaveFinished(TimeSpan.FromMilliseconds(50)),
                Is.False,
                "one send finishing must not release a caller waiting for all of them"
            );

            second.SetResult(true);

            Assert.That(
                PendingResponseWrites.WaitUntilAllHaveFinished(TimeSpan.FromSeconds(10)),
                Is.True
            );
            Assert.That(PendingResponseWrites.InFlightCount, Is.EqualTo(0));
        }

        [Test]
        public void WaitUntilAllHaveFinished_NothingInFlight_ReturnsAtOnce()
        {
            Assert.That(
                PendingResponseWrites.WaitUntilAllHaveFinished(TimeSpan.Zero),
                Is.True,
                "with nothing in flight there is nothing to wait for"
            );
        }
    }
}
