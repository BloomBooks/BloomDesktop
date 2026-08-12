// Copyright (c) 2026 SIL International
// This software is licensed under the MIT License (http://opensource.org/licenses/MIT)

using System;
using System.Collections.Generic;
using Bloom.Api;
using NUnit.Framework;

namespace BloomTests
{
    /// <summary>
    /// Where a test's BloomServer goes when the test is done with it, instead of being disposed
    /// on the spot.
    ///
    /// The problem (BL-16667). A response body can still be going out after the worker thread that
    /// produced it has finished: RequestInfo hands the tail of the send to the framework, which
    /// finishes it on a callback whose only handler is `catch (Win32Exception)`. Closing the
    /// HttpListener underneath such a send makes it fail with an ObjectDisposedException that the
    /// handler does not catch, on a thread none of our own try/catch blocks cover. It reaches the
    /// runtime and kills the process. In a full test run that showed up as the run stopping part
    /// way through -- while still printing a cheerful summary of however many tests had passed
    /// before it died -- roughly one run in three.
    ///
    /// Production meets this only when Bloom quits, once per session. The tests meet it 55 times a
    /// run, which is why they are where it hurts.
    ///
    /// What this does. A retired server is PreDisposed at once -- that stops it accepting
    /// requests, stops and joins its threads, and frees its image cache -- and then simply waits.
    /// Its listener, the only thing that can crash on the way out, is closed later, once several
    /// more servers have been retired behind it. By then the requests it was serving finished long
    /// ago, so there is nothing left for the close to interrupt.
    ///
    /// Why a queue rather than keeping them all to the end of the run: a listener holds its port
    /// until it is closed, and EnsureListening will only try <see cref="kPortsEnsureListeningTries"/>
    /// ports before giving up and calling ProgramExit.Exit. A run binds about 55 servers, so
    /// holding them all would run the suite out of ports; and squatting 55 ports for five minutes
    /// would collide with the other test runs this team habitually has going in parallel
    /// worktrees. Keeping a handful in the queue costs a handful of ports and still puts whole
    /// fixtures between a server's last request and its listener closing.
    ///
    /// This is a mitigation, not a proof. It does not make closing a listener safe; it waits until
    /// there is nothing to be unsafe about. If an aborted run ever shows up again -- and
    /// build/agent-dotnet.* now says so loudly rather than printing a passing summary -- the thing
    /// to look for is a test that leaves a response half-read.
    /// </summary>
    public static class RetiredTestServers
    {
        /// <summary>Matches kNumberOfPortsToTry in BloomServer.EnsureListening.</summary>
        private const int kPortsEnsureListeningTries = 20;

        /// <summary>
        /// How many retired servers keep their listener open at once. Every one of these holds a
        /// port, so this has to stay well under the number of ports EnsureListening will try;
        /// bigger also means longer between a server's last request and its listener closing,
        /// which is the safety this class is buying.
        /// </summary>
        internal const int kMaxAwaitingClose = 5;

        private static readonly object _lock = new object();
        private static readonly Queue<BloomServer> _awaitingClose = new Queue<BloomServer>();

        /// <summary>For the tests of this class, and for diagnosing a port shortage.</summary>
        internal static int CountAwaitingClose
        {
            get
            {
                lock (_lock)
                    return _awaitingClose.Count;
            }
        }

        /// <summary>
        /// Call this instead of server.Dispose() when a test or fixture has finished with a server
        /// it made listen. Safe to call with null, and safe to call on a server that never
        /// listened (there is simply nothing to wait for).
        /// </summary>
        public static void Retire(BloomServer server)
        {
            if (server == null)
                return;

            server.PreDispose();

            BloomServer readyToClose = null;
            lock (_lock)
            {
                _awaitingClose.Enqueue(server);
                if (_awaitingClose.Count > kMaxAwaitingClose)
                    readyToClose = _awaitingClose.Dequeue();
            }

            // Outside the lock: closing can block for a moment, and nothing here needs the queue.
            CloseFully(readyToClose);
        }

        /// <summary>
        /// Closes every listener still waiting. Called once, after the whole run, by
        /// <see cref="SetupFixture"/>.
        /// </summary>
        internal static void CloseAllNow()
        {
            while (true)
            {
                BloomServer next;
                lock (_lock)
                {
                    if (_awaitingClose.Count == 0)
                        return;
                    next = _awaitingClose.Dequeue();
                }
                CloseFully(next);
            }
        }

        private static void CloseFully(BloomServer server)
        {
            if (server == null)
                return;
            try
            {
                // The real Dispose this time. Everything PreDispose did is repeated harmlessly and
                // the listener is closed -- see the note on BloomServer.PreDispose.
                server.Dispose();
            }
            catch (Exception e)
            {
                // Deliberately swallowed, and deliberately noisy about it. A server we have already
                // finished with must not be able to fail a test that has nothing to do with it, but
                // if this ever happens it is worth someone seeing. Console.Error because that is the
                // only channel `dotnet test` shows by default.
                Console.Error.WriteLine(
                    $"RetiredTestServers: closing a retired server threw, which is unexpected: {e}"
                );
            }
        }
    }
}
