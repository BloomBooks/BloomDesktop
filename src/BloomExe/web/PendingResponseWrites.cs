// Copyright (c) 2026 SIL International
// This software is licensed under the MIT License (http://opensource.org/licenses/MIT)

using System;
using System.Diagnostics;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using SIL.Reporting;

namespace Bloom.Api
{
    /// <summary>
    /// Keeps track of response bodies the server is still sending after the worker thread that
    /// produced them has gone back for another request, so that shutdown can wait for them.
    ///
    /// Why this exists (BL-16667). Sending a small file used to be one call:
    /// <c>HttpListenerResponse.Close(buffer, willBlock: false)</c>. That returns as soon as the
    /// write has been issued, which is exactly what we want — it frees the worker thread for the
    /// next request — but the framework then finishes the send on an I/O completion callback of
    /// its own, and that callback's only handler is <c>catch (Win32Exception)</c>. When the
    /// listener is closed while such a send is in flight — precisely what BloomServer.Dispose
    /// does — the completion fails with an ObjectDisposedException, which that handler does not
    /// catch. It is on a thread we do not own, so none of our own try/catch blocks see it either:
    /// it reaches the runtime as an unhandled exception and takes the process with it. In a test
    /// run that shows up as a run that stops partway through while still printing a cheerful
    /// summary of the tests that got to run.
    ///
    /// So we issue the same asynchronous write — same I/O completion port, same thread, no
    /// difference in when the data goes out — but we supply the completion ourselves. Two things
    /// follow from owning that last step: a failure at the end of a send is ours to log and
    /// swallow, and we know when a send has finished, so Dispose can wait for the ones still in
    /// flight instead of closing the listener and hoping.
    /// </summary>
    internal static class PendingResponseWrites
    {
        /// <summary>Guards both <see cref="_inFlight"/> and <see cref="_noneInFlight"/>.</summary>
        private static readonly object _lock = new object();

        /// <summary>How many sends have been started and not yet finished.</summary>
        private static int _inFlight;

        /// <summary>Set whenever <see cref="_inFlight"/> is zero, so a caller can wait on it.</summary>
        private static readonly ManualResetEventSlim _noneInFlight = new ManualResetEventSlim(true);

        /// <summary>
        /// How many sends are in flight right now. For tests and diagnostics; a caller that wants
        /// to wait for them should use <see cref="WaitUntilAllHaveFinished"/> rather than poll this.
        /// </summary>
        internal static int InFlightCount
        {
            get
            {
                lock (_lock)
                    return _inFlight;
            }
        }

        /// <summary>
        /// Sends the first <paramref name="count"/> bytes of <paramref name="buffer"/> as the body
        /// of <paramref name="response"/> and then closes the response, without waiting for any of
        /// that to finish.
        /// </summary>
        /// <remarks>
        /// Sets ContentLength64 from <paramref name="count"/> rather than trusting the caller to
        /// have got it right, because the two disagreeing is not a small mistake: http.sys refuses
        /// a body longer than the declared length, so the client would get a dead connection and
        /// the only trace would be a line in the log. A caller that set it already is setting it to
        /// the same number.
        /// </remarks>
        internal static void SendAndClose(HttpListenerResponse response, byte[] buffer, int count)
        {
            response.ContentLength64 = count;
            Track(
                () => response.OutputStream.WriteAsync(buffer, 0, count),
                // Closes the output stream and releases the request context, which is what the
                // "Close" in Response.Close(buffer, ...) did for us before.
                () => response.Close()
            );
        }

        /// <summary>
        /// The heart of <see cref="SendAndClose"/>: counts a send as in flight from the moment
        /// <paramref name="startSend"/> is called until <paramref name="finishSend"/> has run,
        /// and makes sure that nothing either of them does can escape to the runtime.
        /// </summary>
        /// <remarks>
        /// Split out from SendAndClose so that tests can drive the counting and the error handling
        /// without needing a real HttpListener and a real client to send to.
        /// </remarks>
        internal static void Track(Func<Task> startSend, Action finishSend)
        {
            NoteSendStarted();
            try
            {
                // TaskScheduler.Default so that this always runs on the thread pool. Left to
                // itself ContinueWith uses whatever scheduler the caller happens to be on, and
                // finishing a response is not something we want queued behind anyone's work.
                startSend()
                    .ContinueWith(
                        sendTask => FinishSend(finishSend, sendTask.Exception),
                        TaskScheduler.Default
                    );
            }
            catch (Exception error)
            {
                // The send threw before it even got as far as returning a task to us. The likely
                // reason is that the listener has already been closed, so the handle the write
                // needs is gone.
                FinishSend(finishSend, error);
            }
        }

        /// <summary>
        /// Waits for every send that has been started to finish, giving up after
        /// <paramref name="timeout"/>. Returns false if we gave up, which tells the caller that
        /// whatever it was waiting to do — closing the listener — is going to make those sends
        /// fail.
        /// </summary>
        /// <remarks>
        /// Every send in the process, not just one server's. Bloom runs one server, so that is the
        /// same thing; in tests, where fixtures make a server each, the worst it costs is that one
        /// server's Dispose briefly waits for another's send, and the wait is bounded anyway.
        /// </remarks>
        internal static bool WaitUntilAllHaveFinished(TimeSpan timeout)
        {
            return _noneInFlight.Wait(timeout);
        }

        /// <summary>
        /// Runs the caller's tidying-up for a send that has ended, however it ended, and stops
        /// counting it. Deliberately catches everything: this runs on an I/O completion thread, so
        /// anything that got out of here would be an unhandled exception and would kill the
        /// process, which is the whole thing this class exists to prevent.
        /// </summary>
        private static void FinishSend(Action finishSend, Exception sendError)
        {
            try
            {
                if (sendError != null)
                    ReportProblem("could not finish sending a response", sendError);
                finishSend();
            }
            catch (Exception error)
            {
                ReportProblem("could not close a response it had sent", error);
            }
            finally
            {
                NoteSendFinished();
            }
        }

        /// <summary>
        /// Notes a problem with a send. These are expected during shutdown and whenever the client
        /// goes away mid-request (the user switched pages, paused a video, closed a preview), so
        /// they are worth a log entry and nothing more.
        /// </summary>
        private static void ReportProblem(string whatHappened, Exception error)
        {
            // Unwrap the AggregateException a faulted Task hands us; the one inside is the one
            // that means anything.
            if (error is AggregateException aggregate && aggregate.InnerExceptions.Count == 1)
                error = aggregate.InnerException;
            Logger.WriteEvent($"BloomServer {whatHappened}: {error.Message}");
            Debug.WriteLine($"BloomServer {whatHappened}: {error}");
        }

        private static void NoteSendStarted()
        {
            lock (_lock)
            {
                if (_inFlight++ == 0)
                    _noneInFlight.Reset();
            }
        }

        private static void NoteSendFinished()
        {
            lock (_lock)
            {
                if (--_inFlight == 0)
                    _noneInFlight.Set();
            }
        }
    }
}
