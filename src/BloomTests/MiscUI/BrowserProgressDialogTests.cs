using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Bloom.MiscUI;
using Bloom.web;
using NUnit.Framework;

namespace BloomTests.MiscUI
{
    [TestFixture]
    public class BrowserProgressDialogTests
    {
        /// <summary>
        /// Records what was sent, and does nothing else. The real server would deliver
        /// open-progress to an EmbeddedProgressDialog in some document; here nothing is
        /// listening, so nothing ever posts progress/ready.
        /// </summary>
        private class FakeWebSocketServer : IBloomWebSocketServer
        {
            public readonly List<string> EventIds = new List<string>();

            public void SendString(string clientContext, string eventId, string message)
            {
                lock (EventIds)
                    EventIds.Add(eventId);
            }

            public void SendBundle(string clientContext, string eventId, object messageBundle)
            {
                lock (EventIds)
                    EventIds.Add(eventId);
            }

            public void SendEvent(string clientContext, string eventId)
            {
                lock (EventIds)
                    EventIds.Add(eventId);
            }

            public void Init(string port) { }

            public void Dispose() { }
        }

        [Test]
        public async Task DoWorkWithDeterminateProgressDialogAsync_NothingHostsTheDialog_WorkStillRuns()
        {
            var socketServer = new FakeWebSocketServer();
            var workRan = new ManualResetEventSlim(false);

            // Sanity check: nothing has run yet, and in particular nothing has told the
            // dialog to open.
            Assert.That(workRan.IsSet, Is.False, "the work cannot have run before we start it");
            Assert.That(socketServer.EventIds, Is.Empty);

            await BrowserProgressDialog.DoWorkWithDeterminateProgressDialogAsync(
                socketServer,
                "noSuchDialog",
                "Testing",
                progress =>
                {
                    workRan.Set();
                    return Task.CompletedTask;
                }
            );

            Assert.That(
                socketServer.EventIds,
                Does.Contain("open-progress"),
                "should have asked the dialog to open"
            );
            // The work waits a few seconds for the dialog to report ready, then goes ahead
            // without it. Allow well over that bound so a slow machine does not fail here.
            Assert.That(
                workRan.Wait(TimeSpan.FromSeconds(30)),
                Is.True,
                "the work should run even though no dialog reported ready"
            );
        }
    }
}
