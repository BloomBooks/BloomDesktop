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
        }

        [TearDown]
        public void TearDown()
        {
            // Clear rather than tick the queue empty: ticking would *run* any action still queued, and
            // an empty tick closes the splash screen, so draining has side effects on global state that
            // clearing does not.
            StartupScreenManager.ClearQueueForTests();
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
    }
}
