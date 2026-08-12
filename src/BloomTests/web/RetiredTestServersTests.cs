// Copyright (c) 2026 SIL International
// This software is licensed under the MIT License (http://opensource.org/licenses/MIT)

using System.Threading;
using Bloom.Api;
using Bloom.Book;
using NUnit.Framework;

namespace BloomTests.web
{
    /// <summary>
    /// Guards the two properties the whole scheme rests on: that a retired server's listener is
    /// closed eventually, and that only a handful are held open at a time. The second is the one
    /// that would fail silently — EnsureListening only tries 20 ports before giving up and calling
    /// ProgramExit.Exit, and a run retires about 55 servers, so letting the queue grow would take
    /// the suite out somewhere unrelated and baffling.
    /// </summary>
    [TestFixture]
    public class RetiredTestServersTests
    {
        [SetUp]
        public void Setup()
        {
            // These servers listen, so they must not overlap with the other fixtures that do.
            Monitor.Enter(EndpointHandlerTests._portMonitor);
        }

        [TearDown]
        public void TearDown()
        {
            Monitor.Exit(EndpointHandlerTests._portMonitor);
        }

        [Test]
        public void Retire_HoldsNoMoreThanAHandfulOfListenersOpen()
        {
            // Deliberately more than the cap, and more than a run would ever have open at once.
            const int howMany = RetiredTestServers.kMaxAwaitingClose + 4;

            for (var i = 0; i < howMany; i++)
            {
                var server = new BloomServer(new BookSelection());
                server.EnsureListening();
                RetiredTestServers.Retire(server);

                Assert.That(
                    RetiredTestServers.CountAwaitingClose,
                    Is.LessThanOrEqualTo(RetiredTestServers.kMaxAwaitingClose),
                    $"after retiring {i + 1} servers, more than the cap were still holding a port; "
                        + "a run retires about 55, so this would exhaust EnsureListening's ports"
                );
            }
        }

        [Test]
        public void Retire_ThenCloseAllNow_LeavesNothingHoldingAPort()
        {
            var server = new BloomServer(new BookSelection());
            server.EnsureListening();
            RetiredTestServers.Retire(server);
            Assert.That(
                RetiredTestServers.CountAwaitingClose,
                Is.GreaterThan(0),
                "SANITY: the server we just retired should be waiting to be closed"
            );

            RetiredTestServers.CloseAllNow();

            Assert.That(RetiredTestServers.CountAwaitingClose, Is.EqualTo(0));
        }

        [Test]
        public void Retire_GivenNull_DoesNothing()
        {
            var before = RetiredTestServers.CountAwaitingClose;
            Assert.DoesNotThrow(() => RetiredTestServers.Retire(null));
            Assert.That(RetiredTestServers.CountAwaitingClose, Is.EqualTo(before));
        }

        [Test]
        public void Retire_GivenAServerThatNeverListened_DoesNotThrow()
        {
            // Nothing to wait for, but callers should not have to know that.
            Assert.DoesNotThrow(() =>
                RetiredTestServers.Retire(new BloomServer(new BookSelection()))
            );
            RetiredTestServers.CloseAllNow();
        }
    }
}
