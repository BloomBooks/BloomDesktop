using System;
using Bloom;
using Bloom.MiscUI;
using NUnit.Framework;

namespace BloomTests.MiscUI
{
    [TestFixture]
    public class StartupScreenManagerTests
    {
        /// <summary>
        /// Run one step of the queue, the way an Application.Idle event would.
        /// </summary>
        private static void Tick()
        {
            StartupScreenManager.DoStartupAction(null, EventArgs.Empty);
        }

        [SetUp]
        public void SetUp()
        {
            // The queue is static, so start from a known-empty one rather than inheriting whatever
            // another fixture may have left queued.
            StartupScreenManager.ClearQueueForTests();
            StartupScreenManager.DoLastOfAllAfterClosingSplashScreen = null;
        }

        [TearDown]
        public void TearDown()
        {
            // Clear rather than tick the queue empty: ticking would *run* any action still queued, and
            // an empty tick closes the splash screen, so draining has side effects on global state that
            // clearing does not.
            StartupScreenManager.ClearQueueForTests();
            // Also static, and a test that leaves one set would make the next one's Program look as
            // though it were still in first-time startup.
            StartupScreenManager.DoLastOfAllAfterClosingSplashScreen = null;
        }

        /// <summary>
        /// A startup action that throws must not stop the ones behind it.
        /// </summary>
        /// <remarks>
        /// DoStartupAction returns immediately while an action is in progress, so an exception used to
        /// leave the queue dead for the rest of the run: the splash screen never closed, no later action
        /// ran -- including the one that offers the Open/Create Collections dialog when a collection
        /// won't open -- nothing called ProgramExit.Exit() so not even its 20-second force-quit net was
        /// armed, and the single-instance token was never released. Bloom was left invisible and
        /// unquittable, and the next launch was turned away with "Bloom is already running".
        /// See BL-16678.
        /// </remarks>
        [Test]
        public void DoStartupAction_ActionThrows_LaterActionsStillRun()
        {
            var secondRan = false;
            using (new NonFatalProblem.ExpectedByUnitTest())
            {
                StartupScreenManager.AddStartupAction(() =>
                    throw new ApplicationException("deliberate failure for the test")
                );
                StartupScreenManager.AddStartupAction(() => secondRan = true);

                Tick(); // runs, and fails, the first action
                Assert.That(
                    secondRan,
                    Is.False,
                    "one tick should run only one action; otherwise this test proves nothing"
                );

                Tick();
            }

            Assert.That(
                secondRan,
                Is.True,
                "an action that threw must not wedge the queue behind it"
            );
        }

        [Test]
        public void DoStartupAction_ActionThrows_ReportsTheProblem()
        {
            StartupScreenManager.AddStartupAction(() =>
                throw new ApplicationException("deliberate failure for the test")
            );

            // Throws on dispose if nothing was reported, which is the assertion we want: the failure is
            // not silently swallowed.
            using (new NonFatalProblem.ExpectedByUnitTest())
            {
                Tick();
            }
        }

        [Test]
        public void DoStartupAction_ActionSucceeds_ActionRunsOnceAndQueueMovesOn()
        {
            var firstCount = 0;
            var secondCount = 0;
            StartupScreenManager.AddStartupAction(() => firstCount++);
            StartupScreenManager.AddStartupAction(() => secondCount++);

            Tick();
            Tick();
            Tick(); // nothing left to do

            Assert.That(firstCount, Is.EqualTo(1));
            Assert.That(secondCount, Is.EqualTo(1));
        }

        /// <summary>
        /// OpenProjectWindow asks this question to decide whether it must bring the main window to
        /// the front itself: it must, whenever the one-shot that would otherwise do it has already
        /// been consumed. If this ever answers true after the splash screen has closed, Bloom goes
        /// back to opening behind whatever application holds the foreground when the user switches
        /// collections. See BL-16784.
        /// </summary>
        [Test]
        public void WillBringMainWindowToFrontWhenSplashCloses_FalseOnceCloseSplashScreenConsumesTheOneShot()
        {
            var oneShotRan = false;
            StartupScreenManager.DoLastOfAllAfterClosingSplashScreen = () => oneShotRan = true;

            Assert.That(
                StartupScreenManager.WillBringMainWindowToFrontWhenSplashCloses,
                Is.True,
                "should be true while the one-shot is still waiting to run"
            );

            StartupScreenManager.CloseSplashScreen();

            Assert.That(
                oneShotRan,
                Is.True,
                "closing the splash screen should have run the one-shot; if it didn't, the assertion below proves nothing"
            );
            Assert.That(
                StartupScreenManager.WillBringMainWindowToFrontWhenSplashCloses,
                Is.False,
                "the one-shot has been used up, so nothing will bring the main window to the front now"
            );
        }

        /// <summary>
        /// Hiding the splash screen to make way for a dialog is not the end of startup: the main
        /// window may not even exist yet, and the one-shot must survive to bring it to the front when
        /// it does. This is what makes the startup route that shows the collection chooser answer
        /// "no need" in OpenProjectWindow. See BL-16784.
        /// </summary>
        [Test]
        public void WillBringMainWindowToFrontWhenSplashCloses_StillTrueAfterHideSplashScreenForDialog()
        {
            var oneShotRan = false;
            StartupScreenManager.DoLastOfAllAfterClosingSplashScreen = () => oneShotRan = true;

            StartupScreenManager.HideSplashScreenForDialog();

            Assert.That(oneShotRan, Is.False, "hiding for a dialog must not run the one-shot");
            Assert.That(
                StartupScreenManager.WillBringMainWindowToFrontWhenSplashCloses,
                Is.True,
                "hiding for a dialog must not consume the one-shot"
            );
        }
    }
}
