using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Bloom;
using NUnit.Framework;

namespace BloomTests
{
    /// <summary>
    /// Covers Program.RunConsoleCommandLoop, which is how console-mode commands are run. The property
    /// that matters, and that WebView2 depends on, is that an await inside a console command resumes on
    /// the STA thread that started it. Console mode used to wait for its command with a bare
    /// "while (!task.IsCompleted) Application.DoEvents();" loop, which silently broke that property and
    /// is what made bulk upload fail from the second book onwards (BL-16767).
    /// </summary>
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    public class ConsoleCommandLoopTests
    {
        [Test]
        public void RunConsoleCommandLoop_AwaitsResumeOnTheCallingStaThread()
        {
            var callingThread = Thread.CurrentThread.ManagedThreadId;
            var threadAtStart = -1;
            var threadAfterFirstAwait = -1;
            var threadAfterSecondAwait = -1;
            var apartmentAfterAwait = ApartmentState.Unknown;

            var exitCode = Program.RunConsoleCommandLoop(async () =>
            {
                threadAtStart = Thread.CurrentThread.ManagedThreadId;
                // Task.Delay really yields, which is the case that used to lose the thread.
                await Task.Delay(20);
                threadAfterFirstAwait = Thread.CurrentThread.ManagedThreadId;
                apartmentAfterAwait = Thread.CurrentThread.GetApartmentState();
                await Task.Delay(20);
                threadAfterSecondAwait = Thread.CurrentThread.ManagedThreadId;
                return 42;
            });

            Assert.That(exitCode, Is.EqualTo(42), "The command's exit code should be returned.");
            Assert.That(
                threadAtStart,
                Is.EqualTo(callingThread),
                "Sanity check: the command should start on the thread that ran the loop."
            );
            Assert.That(
                threadAfterFirstAwait,
                Is.EqualTo(callingThread),
                "After the first await the command must be back on the thread that ran the loop; "
                    + "otherwise a WebView2 created by a console command cannot be used afterwards."
            );
            Assert.That(
                threadAfterSecondAwait,
                Is.EqualTo(callingThread),
                "The command must stay on that thread for every later await too."
            );
            Assert.That(
                apartmentAfterAwait,
                Is.EqualTo(ApartmentState.STA),
                "WebView2 can only be created on an STA thread, so the command must resume on one."
            );
        }

        /// <summary>
        /// The case BL-16767 actually failed on: a console command awaits something, and then creates and
        /// uses a WebView2. Under the old DoEvents wait this ran on an MTA thread-pool thread, where
        /// CoreWebView2Environment.CreateAsync throws RPC_E_CHANGED_MODE and the browser could never
        /// become ready.
        /// </summary>
        [Test]
        public void RunConsoleCommandLoop_WebView2CreatedAfterAnAwait_BecomesReady()
        {
            var apartmentWhereBrowserWasCreated = ApartmentState.Unknown;
            var becameReady = false;

            var exitCode = Program.RunConsoleCommandLoop(async () =>
            {
                await Task.Delay(20);
                apartmentWhereBrowserWasCreated = Thread.CurrentThread.GetApartmentState();
                using (var browser = new WebView2Browser())
                {
                    // Nothing parents this browser, so realize its window handle ourselves; WebView2
                    // initialization can only complete once the control has one.
                    browser.CreateControl();
                    var timer = Stopwatch.StartNew();
                    // Awaiting (rather than blocking) hands control back to the message loop, which is
                    // what lets the browser's initialization callbacks actually run.
                    while (!browser.IsReadyToNavigate && timer.ElapsedMilliseconds < 20000)
                        await Task.Delay(20);
                    becameReady = browser.IsReadyToNavigate;
                }
                return 0;
            });

            Assert.That(exitCode, Is.EqualTo(0));
            Assert.That(
                apartmentWhereBrowserWasCreated,
                Is.EqualTo(ApartmentState.STA),
                "Sanity check: the browser must be created on an STA thread, or it could not work at all."
            );
            Assert.That(
                becameReady,
                Is.True,
                "A WebView2 created by a console command after an await must be able to finish "
                    + "initializing. This is exactly what bulk upload could not do (BL-16767)."
            );
        }

        /// <summary>
        /// The reason RunConsoleCommandLoop exists. This is a characterization test of WinForms, not of
        /// our code: it records that Application.DoEvents(), used as the outermost message loop, throws
        /// away the WindowsFormsSynchronizationContext, so an await stops coming back to this thread. If
        /// WinForms ever stops doing this, this test fails and the loop above could be reconsidered.
        /// </summary>
        [Test]
        public void DoEventsAsOutermostLoop_DiscardsTheWinFormsContext_WhichIsWhyWeNeedARealLoop()
        {
            // Establish the starting point rather than assuming it. WinForms only auto-installs its
            // context when there isn't already one, and Application.Run leaves a plain
            // SynchronizationContext behind when it exits -- so whether this test's premise holds would
            // otherwise depend on which test in this fixture ran first.
            SynchronizationContext.SetSynchronizationContext(null);
            using (var control = new Control())
            {
                control.CreateControl();
                Assert.That(
                    SynchronizationContext.Current,
                    Is.TypeOf<WindowsFormsSynchronizationContext>(),
                    "Sanity check: creating a control with a handle should install the WinForms context."
                );

                Application.DoEvents();

                Assert.That(
                    SynchronizationContext.Current,
                    Is.Not.TypeOf<WindowsFormsSynchronizationContext>(),
                    "One outermost DoEvents() is enough to uninstall the WinForms context. This is the "
                        + "trap the old console wait loop fell into."
                );
            }
        }

        [Test]
        public void RunConsoleCommandLoop_CommandThrowsBeforeReturningATask_PropagatesIt()
        {
            // Nothing will ever complete the command, so the loop has to end itself and report why,
            // rather than pumping forever.
            var exception = Assert.Throws<InvalidOperationException>(
                () =>
                    Program.RunConsoleCommandLoop(() =>
                        throw new InvalidOperationException("could not start")
                    ),
                "A command that throws before returning its Task should not hang the loop."
            );
            Assert.That(exception.Message, Is.EqualTo("could not start"));
        }

        [Test]
        public void RunConsoleCommandLoop_CommandTaskFaults_PropagatesIt()
        {
            var exception = Assert.Throws<AggregateException>(() =>
                Program.RunConsoleCommandLoop(async () =>
                {
                    await Task.Delay(10);
                    throw new InvalidOperationException("failed part way");
                })
            );
            Assert.That(exception.InnerException, Is.TypeOf<InvalidOperationException>());
            Assert.That(exception.InnerException.Message, Is.EqualTo("failed part way"));
        }
    }
}
