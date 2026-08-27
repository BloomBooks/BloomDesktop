using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace BloomTests.Edit
{
    /// <summary>
    /// Tests of EditingStateMachine, the class that decides which editing transitions are legal.
    /// These drive it through its public API with recording stubs for the six actions it needs,
    /// the same way EditingModel wires it up in production.
    /// </summary>
    [TestFixture]
    public class EditingStateMachineTests
    {
        private EditingStateMachine _machine;
        private List<string> _actions;
        private string _navigatedTo;
        private string _saveRequestedFor;

        [SetUp]
        public void Setup()
        {
            _actions = new List<string>();
            _navigatedTo = null;
            _saveRequestedFor = null;
            _machine = new EditingStateMachine(
                navigate: pageId =>
                {
                    _navigatedTo = pageId;
                    _actions.Add("navigate:" + pageId);
                },
                requestPageSave: pageId =>
                {
                    _saveRequestedFor = pageId;
                    _actions.Add("requestPageSave:" + pageId);
                },
                updateBookWithPageContents: (pageId, content) =>
                    _actions.Add("updateBook:" + pageId),
                saveBook: () => _actions.Add("saveBook"),
                hidePage: () => _actions.Add("hidePage"),
                enableStateTransitions: enabled => _actions.Add("enableTransitions:" + enabled)
            );
        }

        /// <summary>
        /// Get to the state a user is in while editing a page: navigation finished, no save yet.
        /// </summary>
        private void GetToEditing(string pageId)
        {
            Assert.That(_machine.ToNavigating(pageId), Is.True, "test setup: should navigate");
            Assert.That(
                _navigatedTo,
                Is.EqualTo(pageId),
                "test setup: navigation should have been started"
            );
            Assert.That(_machine.ToEditing(pageId), Is.True, "test setup: should reach Editing");
            Assert.That(_machine.SavePending, Is.False, "test setup: no save in flight yet");
        }

        /// <summary>
        /// Start the save that leaving the Edit tab does: it returns null from
        /// doBeforeSaveToDisk, meaning "don't navigate to another page, we're leaving".
        /// </summary>
        private bool StartSaveForLeavingEditTab(Action postponedWork)
        {
            return _machine.ToSavePending(
                () =>
                {
                    postponedWork?.Invoke();
                    return null; // leaving this tab, show blank page
                },
                saveActionHandlesSaveBook: true
            );
        }

        /// <summary>
        /// What the browser sends back to ReceivePageContent. The state machine only passes it
        /// on to updateBookWithPageContents, so any non-null string will do here.
        /// </summary>
        private const string kPageContentFromBrowser = "<div class='bloom-page'/><SPLIT-DATA>";

        /// <summary>
        /// The invariant behind BL-16766: while we are waiting for the browser to hand back the
        /// page content, emptying the page would throw away the user's edits, so the state
        /// machine refuses. Anything on the path from a tab change to ToNoPage has to respect
        /// this rather than let the exception reach the user as an error report.
        /// </summary>
        [Test]
        public void ToNoPage_WhileSaveInFlight_Throws()
        {
            GetToEditing("page1");
            Assert.That(StartSaveForLeavingEditTab(null), Is.True);
            Assert.That(_machine.SavePending, Is.True, "test setup: save should be in flight");

            var error = Assert.Throws<InvalidOperationException>(() => _machine.ToNoPage());
            Assert.That(error.Message, Does.Contain("Cannot empty page while saving"));
        }

        /// <summary>
        /// BL-16766: the user clicked the Collection tab twice in quick succession. The first
        /// click started a save; the second arrived while that save was still in flight, so it
        /// could not start one of its own. Rather than pressing on with the tab change — which
        /// crashed in ToNoPage and left the workspace half-switched — it must be able to wait for
        /// the in-flight save to finish.
        /// </summary>
        [Test]
        public void DeferUntilSaveCompletes_SaveInFlight_RunsTheWorkOnceTheSaveIsDone()
        {
            GetToEditing("page1");
            var tabChanges = 0;
            Assert.That(StartSaveForLeavingEditTab(() => tabChanges++), Is.True);
            Assert.That(_machine.SavePending, Is.True, "test setup: save should be in flight");
            Assert.That(tabChanges, Is.EqualTo(0), "test setup: nothing has switched tabs yet");

            // The second click. It cannot start a save of its own...
            Assert.That(
                StartSaveForLeavingEditTab(() => tabChanges++),
                Is.False,
                "a second save must not start while one is in flight"
            );
            // ...so it asks to be called back instead.
            var deferredRuns = 0;
            Assert.That(_machine.DeferUntilSaveCompletes(() => deferredRuns++), Is.True);
            Assert.That(
                deferredRuns,
                Is.EqualTo(0),
                "the deferred work must not run while the save is still in flight"
            );

            // The browser finally hands back the page content, completing the first save.
            Assert.That(_machine.ToSavedAndStripped(kPageContentFromBrowser), Is.True);

            Assert.That(tabChanges, Is.EqualTo(1), "the first click's tab change should have run");
            Assert.That(deferredRuns, Is.EqualTo(1), "the deferred work should have run");
            Assert.That(_machine.SavePending, Is.False, "the save should be finished");
            // And by now emptying the page is legal, so the deferred tab change can complete.
            Assert.DoesNotThrow(() => _machine.ToNoPage());
        }

        /// <summary>
        /// Only one piece of deferred work is kept; a later request supersedes an earlier one,
        /// because it represents what the user most recently asked for.
        /// </summary>
        [Test]
        public void DeferUntilSaveCompletes_CalledTwice_RunsOnlyTheLastRequest()
        {
            GetToEditing("page1");
            Assert.That(StartSaveForLeavingEditTab(null), Is.True);
            var firstRuns = 0;
            var secondRuns = 0;
            Assert.That(_machine.DeferUntilSaveCompletes(() => firstRuns++), Is.True);
            Assert.That(_machine.DeferUntilSaveCompletes(() => secondRuns++), Is.True);

            _machine.ToSavedAndStripped(kPageContentFromBrowser);

            Assert.That(firstRuns, Is.EqualTo(0), "the superseded request should not run");
            Assert.That(secondRuns, Is.EqualTo(1));
        }

        /// <summary>
        /// With no save in flight there is nothing to wait for, so the caller is told to get on
        /// with its work itself.
        /// </summary>
        [Test]
        public void DeferUntilSaveCompletes_NoSaveInFlight_DoesNotDefer()
        {
            GetToEditing("page1");
            var runs = 0;
            Assert.That(_machine.DeferUntilSaveCompletes(() => runs++), Is.False);
            Assert.That(runs, Is.EqualTo(0), "it should not run the work either");
        }

        /// <summary>
        /// A caller with nothing to retry (OpenSpecificCollection raises the tab-about-to-change
        /// event with no postponed work) is not made to wait.
        /// </summary>
        [Test]
        public void DeferUntilSaveCompletes_NothingToRetry_DoesNotDefer()
        {
            GetToEditing("page1");
            Assert.That(StartSaveForLeavingEditTab(null), Is.True);
            Assert.That(_machine.SavePending, Is.True, "test setup: save should be in flight");
            Assert.That(_machine.DeferUntilSaveCompletes(null), Is.False);
        }

        /// <summary>
        /// BL-16766 end to end: replays the sequence in the crash report through the calls
        /// production makes — EditingModel.OnTabAboutToChange's two branches, and the ToNoPage()
        /// that WorkspaceView's postponed work reaches via EditingView.OnVisibleChanged(false).
        /// Before the fix the second click threw "Cannot empty page while saving" from inside the
        /// postponed work, which is exactly where the reported stack trace ends.
        /// </summary>
        [Test]
        public void TwoRequestsToLeaveEditTab_SecondArrivesDuringTheFirstsSave_ChangesTabOnce()
        {
            var tabChanges = 0;
            var currentTab = "edit"; // WorkspaceView._previouslySelectedTabArea

            // WorkspaceView.ChangeTab's CompleteTheChange: it raises the tab-changed event, which
            // reaches EditingView.OnVisibleChanged(false), and only then records the new tab.
            Action postponedWorkOfTabChange = () =>
            {
                tabChanges++;
                _machine.ToNoPage();
                currentTab = "collection";
            };

            // WorkspaceView.ChangeTab, including the EditingModel.OnTabAboutToChange handler it
            // raises. Assigned rather than declared so that the fallback can pass it as
            // details.StartTheChangeOver, which is how WorkspaceView supplies it.
            Action clickTheCollectionTab = null;
            clickTheCollectionTab = () =>
            {
                if (currentTab == "collection")
                    return; // "Already on the desired tab: nothing to do."
                if (StartSaveForLeavingEditTab(postponedWorkOfTabChange))
                    return;
                // the fallback: doIfNotInRightStateToSave
                if (_machine.Navigating)
                    _machine.ToNoPage();
                if (_machine.DeferUntilSaveCompletes(clickTheCollectionTab))
                    return;
                postponedWorkOfTabChange();
            };

            GetToEditing("page1");

            clickTheCollectionTab();
            Assert.That(_machine.SavePending, Is.True, "test setup: save should be in flight");
            Assert.That(
                _saveRequestedFor,
                Is.EqualTo("page1"),
                "test setup: the browser should have been asked for the page content"
            );
            Assert.That(tabChanges, Is.EqualTo(0), "test setup: the tab cannot change yet");

            // The user clicks it again before the browser has answered.
            Assert.DoesNotThrow(() => clickTheCollectionTab());
            Assert.That(
                tabChanges,
                Is.EqualTo(0),
                "the second click must not switch tabs while the save is in flight"
            );

            // The browser hands back the page content, completing the save.
            _machine.ToSavedAndStripped(kPageContentFromBrowser);

            Assert.That(_actions, Does.Contain("updateBook:page1"), "the page should be saved");
            Assert.That(
                tabChanges,
                Is.EqualTo(1),
                "the tab should have changed exactly once, when the save finished"
            );
            Assert.That(_machine.SavePending, Is.False);
        }

        /// <summary>
        /// The deferred work must run even if completing the save fails, or a tab click could be
        /// swallowed for the rest of the session.
        /// </summary>
        [Test]
        public void DeferUntilSaveCompletes_SaveCompletionThrows_StillRunsTheWork()
        {
            GetToEditing("page1");
            Assert.That(
                _machine.ToSavePending(() => throw new ApplicationException("save failed")),
                Is.True
            );
            var deferredRuns = 0;
            Assert.That(_machine.DeferUntilSaveCompletes(() => deferredRuns++), Is.True);

            Assert.Throws<ApplicationException>(() =>
                _machine.ToSavedAndStripped(kPageContentFromBrowser)
            );

            Assert.That(deferredRuns, Is.EqualTo(1));
            Assert.That(_machine.SavePending, Is.False);
        }
    }
}
