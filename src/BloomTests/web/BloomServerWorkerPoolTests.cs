using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
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
        public void ReportThreadBlocking_WhenTheLastFreeWorkerBlocks_AddsAWorker()
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
                    var scopes = new List<IDisposable>();
                    try
                    {
                        // One short of all of them: a worker is still free, so leave the pool alone.
                        for (var i = 0; i < workersAtStart - 1; i++)
                            scopes.Add(server.ReportThreadBlocking());
                        Assert.That(
                            server.WorkerCount,
                            Is.EqualTo(workersAtStart),
                            "should not have added a worker while one was still free"
                        );

                        // Now the last free worker blocks too.
                        scopes.Add(server.ReportThreadBlocking());
                        Assert.That(
                            server.WorkerCount,
                            Is.EqualTo(workersAtStart + 1),
                            "should have added a worker once every worker was blocked"
                        );
                    }
                    finally
                    {
                        foreach (var scope in scopes)
                            scope.Dispose();
                    }
                });
            }
        }

        /// <summary>
        /// Blocks reported by threads that are not server workers must be ignored: they are not consuming a
        /// worker, so adding one would be pointless. (ProblemReportApi, for instance, reports a block from
        /// both server and non-server code.)
        /// </summary>
        [Test]
        public void ReportThreadBlocking_FromAThreadThatIsNotAWorker_DoesNotAddAWorker()
        {
            using (var server = new BloomServer(new BookSelection()))
            {
                server.EnsureListening();
                var workersAtStart = server.WorkerCount;

                // Report far more blocks than there are workers -- from this thread, which is not one.
                for (var i = 0; i < workersAtStart * 3; i++)
                    server.ReportThreadBlocking();

                Assert.That(
                    server.WorkerCount,
                    Is.EqualTo(workersAtStart),
                    "blocks reported by a non-worker thread should not grow the pool"
                );
                Assert.That(
                    server.BlockedWorkerCount,
                    Is.EqualTo(0),
                    "blocks reported by a non-worker thread should not be counted at all"
                );
            }
        }

        /// <summary>
        /// The bug this contract exists to prevent (BL-16612). Blocking work that contains an await resumes
        /// on a different thread than it started on, because a server worker carries no synchronization
        /// context. The old design decided whether to decrement by asking "am I on a worker thread?" at the
        /// END of the block, so on that path it answered no, skipped the decrement, and left the count
        /// permanently high -- which, now that the count drives adding workers, would grow the pool on every
        /// later block. Disposing the scope has to work wherever it happens.
        /// </summary>
        [Test]
        public void ReportThreadBlocking_WhenTheScopeIsDisposedOnAnotherThread_StillUndoesTheBlock()
        {
            using (var server = new BloomServer(new BookSelection()))
            {
                server.EnsureListening();
                Assert.That(
                    server.BlockedWorkerCount,
                    Is.EqualTo(0),
                    "SANITY: nothing should be blocked on a fresh server"
                );

                IDisposable scope = null;
                RunOnAPretendWorkerThread(() =>
                {
                    scope = server.ReportThreadBlocking();
                });
                Assert.That(
                    server.BlockedWorkerCount,
                    Is.EqualTo(1),
                    "SANITY: reporting from a worker thread should have counted the block"
                );

                // Dispose from a plain thread-pool thread -- which is exactly where an await's continuation
                // lands, and which is NOT named like a worker.
                Task.Run(() => scope.Dispose()).Wait(10000);

                Assert.That(
                    server.BlockedWorkerCount,
                    Is.EqualTo(0),
                    "disposing the scope on a different thread must still undo the block"
                );
            }
        }

        /// <summary>
        /// Disposal has to be idempotent, since a scope can be disposed by a `using` whose caller also
        /// disposes it; double-counting downwards would make the server believe workers were free when they
        /// were not, which is the opposite of the deadlock but just as wrong.
        /// </summary>
        [Test]
        public void ReportThreadBlocking_WhenTheScopeIsDisposedTwice_OnlyUndoesTheBlockOnce()
        {
            using (var server = new BloomServer(new BookSelection()))
            {
                server.EnsureListening();
                IDisposable first = null,
                    second = null;
                RunOnAPretendWorkerThread(() =>
                {
                    first = server.ReportThreadBlocking();
                    second = server.ReportThreadBlocking();
                });
                Assert.That(
                    server.BlockedWorkerCount,
                    Is.EqualTo(2),
                    "SANITY: two reported blocks should count as two"
                );

                first.Dispose();
                first.Dispose();

                Assert.That(
                    server.BlockedWorkerCount,
                    Is.EqualTo(1),
                    "a second Dispose of the same scope must not decrement again"
                );
                second.Dispose();
            }
        }

        /// <summary>
        /// A worker thread that has died leaves its entry behind until the listener prunes it, so the raw
        /// entry count can claim a worker is available when nothing of it remains but the entry. The shortage
        /// check has to count only LIVE workers, or it declines to add one in exactly the situation it exists
        /// for. Devin raised this on BL-16612.
        ///
        /// Reaches into the private worker collection to plant a dead entry, because there is no honest way
        /// to make a real worker die on cue. The alternative -- adding a production seam only tests use --
        /// seemed worse than a reflective poke that fails loudly if the field is ever renamed.
        /// </summary>
        [Test]
        public void ReportThreadBlocking_WhenAnEntryIsForADeadThread_StillAddsAWorker()
        {
            using (var server = new BloomServer(new BookSelection()))
            {
                server.EnsureListening();
                var liveWorkers = server.WorkerCount;

                var field = typeof(BloomServer).GetField(
                    "_workers",
                    BindingFlags.NonPublic | BindingFlags.Instance
                );
                Assert.That(
                    field,
                    Is.Not.Null,
                    "BloomServer._workers was not found; this test needs updating to match the field name"
                );
                var workers = (ConcurrentDictionary<int, Thread>)field.GetValue(server);

                // A thread that has already finished: alive is false, but the entry is present.
                var deadThread = new Thread(() => { }) { IsBackground = true };
                deadThread.Start();
                Assert.That(
                    deadThread.Join(10000),
                    Is.True,
                    "SANITY: the throwaway thread should finish"
                );
                Assert.That(
                    workers.TryAdd(deadThread.ManagedThreadId, deadThread),
                    Is.True,
                    "SANITY: should have been able to plant the dead entry"
                );
                Assert.That(
                    server.WorkerCount,
                    Is.EqualTo(liveWorkers + 1),
                    "SANITY: the dead entry should now be inflating the raw worker count"
                );

                // Block every LIVE worker -- one fewer than the raw count. Judged on the raw count this
                // looks like a worker is still free, and no worker gets added; judged on live workers the
                // pool is exhausted and one must be.
                RunOnAPretendWorkerThread(() =>
                {
                    var scopes = new List<IDisposable>();
                    try
                    {
                        for (var i = 0; i < liveWorkers; i++)
                            scopes.Add(server.ReportThreadBlocking());

                        Assert.That(
                            server.WorkerCount,
                            Is.EqualTo(liveWorkers + 2),
                            "a dead entry must not be counted as a worker that could take the request"
                        );
                    }
                    finally
                    {
                        foreach (var scope in scopes)
                            scope.Dispose();
                    }
                });
            }
        }

        /// <summary>
        /// Runs the action on a thread named the way BloomServer names its workers, since the name is how
        /// ReportThreadBlocking recognizes one of its own workers. Rethrows whatever the action threw, so
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
