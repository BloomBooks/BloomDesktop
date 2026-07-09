using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SIL.Reporting;

namespace Bloom.TeamCollection
{
    /// <summary>
    /// A minimal single-consumer work queue used to apply automatically-detected remote book
    /// changes off the UI thread (see <see cref="TeamCollection.CanAutoApplyRemoteChanges"/>).
    /// Enqueuing a book that is already queued or currently being processed is a no-op -- the
    /// eventual processing pass re-reads whatever is CURRENT in the repo/local state itself (see
    /// TeamCollection's re-verification in its auto-apply processing method), so no information is
    /// lost by not queuing a duplicate. Books are otherwise processed strictly one at a time, in
    /// the order they were first queued, so a big download for one book can't be interleaved with
    /// (and possibly corrupted by) another book's download.
    /// </summary>
    public class RemoteBookAutoApplyQueue
    {
        private readonly Action<string> _processBook;
        private readonly Action<Action> _runWorker;
        private readonly object _gate = new object();
        private readonly Queue<string> _pending = new Queue<string>();
        private readonly HashSet<string> _pendingOrInFlight = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase
        );
        private bool _workerRunning;

        /// <param name="processBook">
        /// Called once per queued book, one at a time, in the order first queued. Never called on
        /// the thread that called <see cref="Enqueue"/> unless a synchronous <paramref name="runWorker"/>
        /// is supplied (tests only -- see below).
        /// </param>
        /// <param name="runWorker">
        /// How to run the consumer loop. Defaults to a threadpool task (<see cref="Task.Run(Action)"/>),
        /// which is what keeps book downloads off the UI thread in production. Tests can pass a
        /// synchronous runner (e.g. <c>action => action()</c>) so the queue's effects are
        /// observable immediately, without waiting for a background thread to schedule.
        /// </param>
        public RemoteBookAutoApplyQueue(Action<string> processBook, Action<Action> runWorker = null)
        {
            _processBook = processBook;
            _runWorker = runWorker ?? (action => Task.Run(action));
        }

        /// <summary>
        /// Queues a book for processing unless it is already queued or currently being processed.
        /// </summary>
        public void Enqueue(string bookName)
        {
            lock (_gate)
            {
                if (!_pendingOrInFlight.Add(bookName))
                    return; // already queued or being processed; the eventual pass will see current state
                _pending.Enqueue(bookName);
                if (_workerRunning)
                    return; // a worker loop is already draining the queue
                _workerRunning = true;
            }
            _runWorker(RunLoop);
        }

        /// <summary>For tests: how many books are currently queued or being processed.</summary>
        internal int CountForTests
        {
            get
            {
                lock (_gate)
                    return _pendingOrInFlight.Count;
            }
        }

        private void RunLoop()
        {
            while (true)
            {
                string bookName;
                lock (_gate)
                {
                    if (_pending.Count == 0)
                    {
                        _workerRunning = false;
                        return;
                    }
                    bookName = _pending.Dequeue();
                }
                try
                {
                    _processBook(bookName);
                }
                catch (Exception e)
                {
                    // One book's failure must not stop the queue from processing the rest, or
                    // from accepting new work afterwards.
                    Logger.WriteError(
                        $"RemoteBookAutoApplyQueue: error auto-applying remote change for '{bookName}'",
                        e
                    );
                }
                finally
                {
                    lock (_gate)
                        _pendingOrInFlight.Remove(bookName);
                }
            }
        }
    }
}
