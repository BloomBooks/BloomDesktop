using System;
using System.Runtime.ExceptionServices;
using System.Threading;
using Bloom.Api;
using Bloom.Book;
using NUnit.Framework;

namespace BloomTests.web
{
    /// <summary>
    /// Tests for BloomServer's worker pool keeping a worker able to take work while others are blocked.
    ///
    /// Deliberately a separate fixture from BloomServerTests, which needs a collection, a localization
    /// manager, and a BloomFileLocator; none of that is relevant here, and a real listening server is
    /// (unlike in those tests) exactly what we need, since the workers only exist once it is listening.
    /// </summary>
    [TestFixture]
    public class BloomServerWorkerPoolTests
    {
        /// <summary>
        /// The whole point of reporting a block (BL-16612): once every worker is blocked, no worker is left
        /// to take a request off the queue -- including the request that would let the blocked workers
        /// finish. So the server has to add one.
        /// </summary>
        [Test]
        public void RegisterThreadBlocking_WhenTheLastFreeWorkerBlocks_AddsAWorker()
        {
            using (var server = new BloomServer(new BookSelection()))
            {
                server.EnsureListening();
                var workersAtStart = server.WorkerCount;
                Assert.That(
                    workersAtStart,
                    Is.GreaterThan(1),
                    "SANITY: the pool should start out with several workers"
                );

                // The server only counts blocks reported by its own worker threads, so we have to report
                // from a thread that looks like one.
                RunOnAPretendWorkerThread(() =>
                {
                    // One short of all of them: a worker is still free, so the pool should be left alone.
                    for (var i = 0; i < workersAtStart - 1; i++)
                        server.RegisterThreadBlocking();
                    Assert.That(
                        server.WorkerCount,
                        Is.EqualTo(workersAtStart),
                        "should not have added a worker while one was still free"
                    );

                    // Now the last free worker blocks too.
                    server.RegisterThreadBlocking();
                    Assert.That(
                        server.WorkerCount,
                        Is.EqualTo(workersAtStart + 1),
                        "should have added a worker once every worker was blocked"
                    );

                    for (var i = 0; i < workersAtStart; i++)
                        server.RegisterThreadUnblocked();
                });
            }
        }

        /// <summary>
        /// Blocks reported by threads that are not server workers must be ignored: they are not consuming a
        /// worker, so adding one would be pointless. (ProblemReportApi, for instance, reports a block from
        /// both server and non-server code.)
        /// </summary>
        [Test]
        public void RegisterThreadBlocking_FromAThreadThatIsNotAWorker_DoesNotAddAWorker()
        {
            using (var server = new BloomServer(new BookSelection()))
            {
                server.EnsureListening();
                var workersAtStart = server.WorkerCount;

                // Report far more blocks than there are workers -- from this thread, which is not one.
                for (var i = 0; i < workersAtStart * 3; i++)
                    server.RegisterThreadBlocking();

                Assert.That(
                    server.WorkerCount,
                    Is.EqualTo(workersAtStart),
                    "blocks reported by a non-worker thread should not grow the pool"
                );
            }
        }

        /// <summary>
        /// Runs the action on a thread named the way BloomServer names its workers, since the name is how
        /// RegisterThreadBlocking recognizes one of its own workers. Rethrows whatever the action threw, so
        /// that an assertion failure inside it fails the test instead of being swallowed on that thread.
        /// </summary>
        private static void RunOnAPretendWorkerThread(Action action)
        {
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    action();
                }
                catch (Exception e)
                {
                    failure = e;
                }
            })
            {
                Name = BloomServer.WorkerThreadNamePrefix + "pretend",
                IsBackground = true,
            };
            thread.Start();
            Assert.That(
                thread.Join(10000),
                Is.True,
                "the pretend worker thread should have finished"
            );
            if (failure != null)
                ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }
}
