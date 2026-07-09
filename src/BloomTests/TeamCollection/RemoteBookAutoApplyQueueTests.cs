using System;
using System.Collections.Generic;
using System.Threading;
using Bloom.TeamCollection;
using NUnit.Framework;

namespace BloomTests.TeamCollection
{
    // Unit tests for the small single-consumer queue that TeamCollection.HandleModifiedFile uses
    // to apply auto-appliable remote book changes off the UI thread (batch item 4+5). These tests
    // use a synchronous runWorker (action => action()) so behavior is deterministic and doesn't
    // depend on real background-thread timing, except where a test specifically wants to verify
    // real single-consumer-thread behavior (marked below).
    public class RemoteBookAutoApplyQueueTests
    {
        [Test]
        public void Enqueue_OneBook_ProcessesIt()
        {
            var processed = new List<string>();
            var queue = new RemoteBookAutoApplyQueue(processed.Add, runWorker: action => action());

            queue.Enqueue("My Book");

            Assert.That(processed, Is.EqualTo(new[] { "My Book" }));
        }

        [Test]
        public void Enqueue_SameBookTwiceBeforeProcessing_DedupesToOneCall()
        {
            // Sanity check on the test setup: we want the SECOND Enqueue call to happen while the
            // first book is still considered "in flight" from the queue's point of view, i.e.
            // before the (synchronous, in this test) processing callback has returned. We arrange
            // that by re-entrantly calling Enqueue for the SAME name from inside the processing
            // callback itself.
            var callCount = 0;
            RemoteBookAutoApplyQueue queue = null;
            queue = new RemoteBookAutoApplyQueue(
                bookName =>
                {
                    callCount++;
                    if (callCount == 1)
                    {
                        // Still "in flight" per the queue's own bookkeeping (removed only in the
                        // caller's finally, after this callback returns) -- must be deduped.
                        queue.Enqueue(bookName);
                    }
                },
                runWorker: action => action()
            );

            queue.Enqueue("Dup Book");

            Assert.That(
                callCount,
                Is.EqualTo(1),
                "Re-entrant Enqueue of a book already being processed must be a no-op"
            );
        }

        [Test]
        public void Enqueue_SameBookAfterProcessingCompletes_ProcessesAgain()
        {
            var processed = new List<string>();
            var queue = new RemoteBookAutoApplyQueue(processed.Add, runWorker: action => action());

            queue.Enqueue("Book A");
            queue.Enqueue("Book A"); // first call has already fully completed by now

            Assert.That(
                processed,
                Is.EqualTo(new[] { "Book A", "Book A" }),
                "Once a book's processing has finished, a later change for the same book should be queued again"
            );
        }

        [Test]
        public void Enqueue_MultipleDifferentBooks_ProcessesAllInOrder()
        {
            var processed = new List<string>();
            var queue = new RemoteBookAutoApplyQueue(processed.Add, runWorker: action => action());

            queue.Enqueue("Book One");
            queue.Enqueue("Book Two");
            queue.Enqueue("Book Three");

            Assert.That(processed, Is.EqualTo(new[] { "Book One", "Book Two", "Book Three" }));
        }

        [Test]
        public void Enqueue_ProcessingThrows_DoesNotStopLaterBooksFromBeingProcessed()
        {
            var processed = new List<string>();
            var queue = new RemoteBookAutoApplyQueue(
                bookName =>
                {
                    if (bookName == "Bad Book")
                        throw new InvalidOperationException("simulated failure");
                    processed.Add(bookName);
                },
                runWorker: action => action()
            );

            queue.Enqueue("Bad Book");
            queue.Enqueue("Good Book");

            Assert.That(
                processed,
                Is.EqualTo(new[] { "Good Book" }),
                "One book's processing failure must not prevent later books (or a later requeue) from being processed"
            );
        }

        [Test]
        public void Enqueue_ProcessingThrows_SameBookCanBeQueuedAgainAfterwards()
        {
            var attempts = 0;
            var queue = new RemoteBookAutoApplyQueue(
                _ =>
                {
                    attempts++;
                    if (attempts == 1)
                        throw new InvalidOperationException("simulated failure");
                },
                runWorker: action => action()
            );

            queue.Enqueue("Flaky Book");
            queue.Enqueue("Flaky Book");

            Assert.That(
                attempts,
                Is.EqualTo(2),
                "A book must be re-queueable after a prior processing attempt threw"
            );
        }

        [Test]
        public void Enqueue_RealBackgroundWorker_EventuallyProcessesAllQueuedBooks()
        {
            // Uses the REAL default (Task.Run) worker to sanity-check actual background-thread
            // behavior, not just the synchronous test double used above.
            var processed = new List<string>();
            var gate = new object();
            var queue = new RemoteBookAutoApplyQueue(bookName =>
            {
                lock (gate)
                    processed.Add(bookName);
            });

            queue.Enqueue("Async Book One");
            queue.Enqueue("Async Book Two");

            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (DateTime.UtcNow < deadline)
            {
                lock (gate)
                {
                    if (processed.Count >= 2)
                        break;
                }
                Thread.Sleep(20);
            }

            lock (gate)
            {
                Assert.That(
                    processed,
                    Is.EquivalentTo(new[] { "Async Book One", "Async Book Two" })
                );
            }
        }
    }
}
