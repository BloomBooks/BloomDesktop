using System;
using System.Collections.Generic;
using Bloom.Edit;
using NUnit.Framework;

namespace BloomTests.Edit
{
    /// <summary>
    /// Tests for EditingStateMachine.SaveThenNavigate: what a page click, and every page-list
    /// command, does. The current page is saved from content the browser has already given us, the
    /// caller's action runs, the book is written, and we go to the page the action names -- all in
    /// one step, with nothing to wait for. See EditingModel.MergeCurrentPageThenSave.
    /// </summary>
    [TestFixture]
    public class EditingStateMachineTests
    {
        private List<string> _navigatedTo;
        private List<string> _updatedWith;
        private int _saveBookCount;
        private List<Exception> _reportedFailures;
        private EditingStateMachine _stateMachine;

        [SetUp]
        public void Setup()
        {
            _navigatedTo = new List<string>();
            _updatedWith = new List<string>();
            _saveBookCount = 0;
            _reportedFailures = new List<Exception>();
            _stateMachine = new EditingStateMachine(
                navigate: pageId => _navigatedTo.Add(pageId),
                updateBookWithPageContents: (_, data) => _updatedWith.Add(data),
                saveBook: () => _saveBookCount++,
                hidePage: () => { }
            );
        }

        private void GoToEditing(string pageId)
        {
            Assert.That(
                _stateMachine.ToNavigating(pageId),
                Is.True,
                "test setup: should be able to start navigating"
            );
            Assert.That(
                _stateMachine.ToEditing(pageId),
                Is.True,
                "test setup: should be able to get to Editing"
            );
        }

        private SaveOutcome SaveThenGoTo(string content, string pageId)
        {
            return SaveThenDoAndGoTo(content, () => pageId);
        }

        private SaveOutcome SaveThenDoAndGoTo(string content, Func<string> changeBookBeforeWriting)
        {
            return _stateMachine.SaveThenNavigate(
                content,
                changeBookBeforeWriting,
                e => _reportedFailures.Add(e)
            );
        }

        [Test]
        public void SaveThenNavigate_WhileEditing_SavesThenGoesToTheOtherPage()
        {
            GoToEditing("page1");
            _navigatedTo.Clear(); // the navigation that got us here is not what we're testing

            Assert.That(
                SaveThenGoTo("body<SPLIT-DATA>css", "page2"),
                Is.EqualTo(SaveOutcome.Saved)
            );

            Assert.That(_updatedWith, Is.EqualTo(new[] { "body<SPLIT-DATA>css" }));
            Assert.That(_saveBookCount, Is.EqualTo(1));
            Assert.That(
                _navigatedTo,
                Is.EqualTo(new[] { "page2" }),
                "should have gone to the clicked page, in the same step"
            );
            Assert.That(_reportedFailures, Is.Empty);
        }

        [Test]
        public void SaveThenNavigate_LandsInAStateThatCanAcceptTheNextPageClick()
        {
            // The bug this avoids: while the old SavePending state lasted, a further page click was
            // silently dropped.
            GoToEditing("page1");
            Assert.That(SaveThenGoTo("content", "page2"), Is.EqualTo(SaveOutcome.Saved));

            // Finish arriving, then click again, as an impatient user would.
            Assert.That(_stateMachine.ToEditing("page2"), Is.True);
            _navigatedTo.Clear();

            Assert.That(SaveThenGoTo("more content", "page3"), Is.EqualTo(SaveOutcome.Saved));
            Assert.That(_navigatedTo, Is.EqualTo(new[] { "page3" }));
        }

        [Test]
        public void SaveThenNavigate_WhileNavigating_DoesNothing()
        {
            Assert.That(_stateMachine.ToNavigating("page1"), Is.True);
            _navigatedTo.Clear();

            Assert.That(SaveThenGoTo("content", "page2"), Is.EqualTo(SaveOutcome.Declined));

            Assert.That(_updatedWith, Is.Empty);
            Assert.That(_saveBookCount, Is.EqualTo(0));
            Assert.That(_navigatedTo, Is.Empty);
            Assert.That(_reportedFailures, Is.Empty, "not being ready to save is not a failure");
        }

        [Test]
        public void SaveThenNavigate_FromNoPage_JustGoesThere()
        {
            // Nothing to save, but the click still means "show me that page".
            Assert.That(SaveThenGoTo("content", "page2"), Is.EqualTo(SaveOutcome.Saved));

            Assert.That(_updatedWith, Is.Empty, "there was no page to save");
            Assert.That(_navigatedTo, Is.EqualTo(new[] { "page2" }));
        }

        [Test]
        public void SaveThenNavigate_FromNoPage_StillAsksToWriteWhatTheActionDid()
        {
            // There is no browser content to merge here, but the action can still change the book
            // -- deleting a page, say -- and that has to reach disk. (Whether anything is actually
            // written is the saveBook action's decision; ours is to ask.)
            Assert.That(SaveThenDoAndGoTo("content", () => "page2"), Is.EqualTo(SaveOutcome.Saved));

            Assert.That(
                _saveBookCount,
                Is.EqualTo(1),
                "whatever the action changed must still be offered to disk"
            );
        }

        [Test]
        public void SaveThenNavigate_SaveFails_ReportsAndStaysPut()
        {
            GoToEditing("page1");
            _navigatedTo.Clear();

            Assert.That(
                SaveThenGoTo("ERROR: the browser could not gather it", "page2"),
                Is.EqualTo(SaveOutcome.Failed),
                "Failed, not Declined: the caller must not fall back and run the action again"
            );

            Assert.That(_updatedWith, Is.Empty, "we must not put an error message in the book");
            Assert.That(_saveBookCount, Is.EqualTo(0));
            Assert.That(_reportedFailures.Count, Is.EqualTo(1));
            Assert.That(
                _navigatedTo,
                Is.Empty,
                "going on to the clicked page would silently discard the edits we failed to save"
            );
        }

        [Test]
        public void SaveThenNavigate_RepeatedFailureOnSamePage_ReportsOnlyOnce()
        {
            GoToEditing("page1");

            SaveThenGoTo("ERROR: first try", "page2");
            SaveThenGoTo("ERROR: second try", "page2");

            Assert.That(
                _reportedFailures.Count,
                Is.EqualTo(1),
                "a page that always fails must not lock the user out with repeated dialogs"
            );
        }

        [Test]
        public void SaveThenNavigate_FailureOnDifferentPage_ReportsAgain()
        {
            GoToEditing("page1");
            SaveThenGoTo("ERROR: first page", "page2");
            Assert.That(_reportedFailures.Count, Is.EqualTo(1), "test setup");

            Assert.That(
                SaveThenGoTo("content", "page2"),
                Is.EqualTo(SaveOutcome.Saved),
                "test setup"
            );
            Assert.That(_stateMachine.ToEditing("page2"), Is.True, "test setup");

            SaveThenGoTo("ERROR: second page", "page3");

            Assert.That(_reportedFailures.Count, Is.EqualTo(2));
        }

        [Test]
        public void SaveThenNavigate_AfterFailingThenSucceeding_ReportsAgainIfItFailsAgain()
        {
            GoToEditing("page1");
            SaveThenGoTo("ERROR: first try", "page2");
            Assert.That(_reportedFailures.Count, Is.EqualTo(1), "test setup");

            // A successful save of the same page, then failing on it again after coming back.
            Assert.That(SaveThenGoTo("good content", "page1"), Is.EqualTo(SaveOutcome.Saved));
            Assert.That(_stateMachine.ToEditing("page1"), Is.True, "test setup");
            SaveThenGoTo("ERROR: later try", "page2");

            Assert.That(
                _reportedFailures.Count,
                Is.EqualTo(2),
                "a successful save should clear the 'already reported' memory"
            );
        }

        // The changeBookBeforeWriting form: what duplicate/delete/paste/move page do. The action has
        // to see the user's latest edits (so it must run AFTER the browser's content goes into the
        // book DOM) and its work has to reach disk (so it must run BEFORE the book is written).

        [Test]
        public void SaveThenNavigate_RunsTheActionBetweenTheDomUpdateAndTheDiskSave()
        {
            GoToEditing("page1");
            _navigatedTo.Clear();
            var domUpdatesWhenActionRan = -1;
            var saveBookCountWhenActionRan = -1;

            var result = SaveThenDoAndGoTo(
                "body<SPLIT-DATA>css",
                () =>
                {
                    domUpdatesWhenActionRan = _updatedWith.Count;
                    saveBookCountWhenActionRan = _saveBookCount;
                    return "theDuplicatedPage";
                }
            );

            Assert.That(result, Is.EqualTo(SaveOutcome.Saved));
            Assert.That(
                domUpdatesWhenActionRan,
                Is.EqualTo(1),
                "the action must see the edits the browser just sent us"
            );
            Assert.That(
                saveBookCountWhenActionRan,
                Is.EqualTo(0),
                "the action must run before the disk save, so what it does gets written too"
            );
            Assert.That(_saveBookCount, Is.EqualTo(1), "and the disk save must still happen");
            Assert.That(_navigatedTo, Is.EqualTo(new[] { "theDuplicatedPage" }));
        }

        [Test]
        public void SaveThenNavigate_WrongState_DoesNotRunTheAction()
        {
            Assert.That(_stateMachine.ToNavigating("page1"), Is.True, "test setup");
            var actionRan = false;

            var result = SaveThenDoAndGoTo(
                "content",
                () =>
                {
                    actionRan = true;
                    return "page2";
                }
            );

            Assert.That(result, Is.EqualTo(SaveOutcome.Declined));
            Assert.That(
                actionRan,
                Is.False,
                "Declined tells the caller nothing happened, so the action must not have run"
            );
        }

        [Test]
        public void SaveThenNavigate_SaveFails_DoesNotRunTheAction()
        {
            GoToEditing("page1");
            var actionRan = false;

            var result = SaveThenDoAndGoTo(
                "ERROR: the browser could not gather it",
                () =>
                {
                    actionRan = true;
                    return "page2";
                }
            );

            Assert.That(
                result,
                Is.EqualTo(SaveOutcome.Failed),
                "Failed, not Declined -- see the next test for why the difference matters"
            );
            Assert.That(
                actionRan,
                Is.False,
                "deleting or duplicating a page we failed to save would act on stale content"
            );
        }

        [Test]
        public void SaveThenNavigate_ActionThrows_ReportsFailedSoTheCallerWillNotRetry()
        {
            // Found live: relocating a page threw part way through, the caller read the result as
            // "nothing happened", and the page got relocated a SECOND time. An action that has
            // already changed the book must never be reported as not having run.
            GoToEditing("page1");
            var timesActionRan = 0;

            var result = SaveThenDoAndGoTo(
                "good content",
                () =>
                {
                    timesActionRan++;
                    throw new ApplicationException("the action blew up after changing the book");
                }
            );

            Assert.That(timesActionRan, Is.EqualTo(1), "test setup: the action should have run");
            Assert.That(
                result,
                Is.EqualTo(SaveOutcome.Failed),
                "Declined here would invite the caller to run the action a second time"
            );
            Assert.That(_reportedFailures.Count, Is.EqualTo(1));
        }

        [Test]
        public void SaveThenNavigate_ActionNavigatesToTheSamePage_DoesNotNavigateTwice()
        {
            // Found live: relocating a page raises RelocatePageEvent, and OnRelocatePage refreshes
            // the display of the page whose HTML just changed -- i.e. the action navigates. That
            // used to throw "Cannot navigate while editing", because we are still in Editing while
            // the action runs. It is safe here: the browser's content is already in the book DOM,
            // so there is nothing left to lose.
            GoToEditing("page1");
            _navigatedTo.Clear();

            var result = SaveThenDoAndGoTo(
                "good content",
                () =>
                {
                    _stateMachine.ToNavigating("theMovedPage");
                    return "theMovedPage";
                }
            );

            Assert.That(result, Is.EqualTo(SaveOutcome.Saved));
            Assert.That(_reportedFailures, Is.Empty, "an action that navigates is legal here");
            Assert.That(_saveBookCount, Is.EqualTo(1));
            Assert.That(
                _navigatedTo,
                Is.EqualTo(new[] { "theMovedPage" }),
                "the action's navigation and ours are to the same page, so it should happen once"
            );
        }

        [Test]
        public void SaveThenNavigate_ActionNavigatesElsewhere_OurTargetWins()
        {
            GoToEditing("page1");
            _navigatedTo.Clear();

            var result = SaveThenDoAndGoTo(
                "good content",
                () =>
                {
                    _stateMachine.ToNavigating("somewhereTheActionWanted");
                    return "whereWeSaidToGo";
                }
            );

            Assert.That(result, Is.EqualTo(SaveOutcome.Saved));
            Assert.That(
                _navigatedTo,
                Is.EqualTo(new[] { "somewhereTheActionWanted", "whereWeSaidToGo" }),
                "the page the action named is where we must end up"
            );
        }

        [Test]
        public void SaveThenNavigate_ActionAsksForAnotherSave_IgnoresItAndStillNavigates()
        {
            // An action is allowed to do things that would normally start a save -- changing the
            // page selection does, via PageListController.OnPageSelectedChanged. There is nothing
            // for that save to do: the content is already merged and the book is about to be
            // written. Accepting it would re-enter the whole save, including this action.
            GoToEditing("page1");
            _navigatedTo.Clear();
            var nestedSaveAccepted = true;

            var result = SaveThenDoAndGoTo(
                "good content",
                () =>
                {
                    nestedSaveAccepted =
                        _stateMachine.SaveThenNavigate(
                            "content from the nested save",
                            () => "pageTheNestedSaveWanted",
                            e => _reportedFailures.Add(e)
                        ) != SaveOutcome.Declined;
                    return "whereWeSaidToGo";
                }
            );

            Assert.That(
                nestedSaveAccepted,
                Is.False,
                "a save requested from inside the action should be refused"
            );
            Assert.That(
                _updatedWith,
                Is.EqualTo(new[] { "good content" }),
                "and the nested save must not have merged its content on top of ours"
            );
            Assert.That(result, Is.EqualTo(SaveOutcome.Saved));
            Assert.That(_saveBookCount, Is.EqualTo(1), "the book is written exactly once");
            Assert.That(
                _navigatedTo,
                Is.EqualTo(new[] { "whereWeSaidToGo" }),
                "the page we promised to go to must still be shown"
            );
        }

        [Test]
        public void SaveThenNavigate_NothingToMerge_StillRunsTheActionAndAsksToWriteTheBook()
        {
            // Null content means the page has not been changed since it loaded (see PageSnapshot:
            // a page nobody edited never produces a snapshot). There is nothing to merge, but the
            // action may itself change the book -- duplicating a page, say -- so it must still
            // run, the saveBook action must still get its chance, and we must still go where it
            // says.
            GoToEditing("page1");
            _navigatedTo.Clear();
            var actionRan = false;

            var result = SaveThenDoAndGoTo(
                null,
                () =>
                {
                    actionRan = true;
                    return "page2";
                }
            );

            Assert.That(result, Is.EqualTo(SaveOutcome.Saved));
            Assert.That(actionRan, Is.True, "the action must run even with nothing to merge");
            Assert.That(
                _updatedWith,
                Is.Empty,
                "there was no page content, so nothing should have been merged into the book DOM"
            );
            Assert.That(_saveBookCount, Is.EqualTo(1), "the saveBook action must still be called");
            Assert.That(_navigatedTo, Is.EqualTo(new[] { "page2" }));
        }

        [Test]
        public void SaveThenNavigate_ActionReturnsNull_LeavesTheEditorBlank()
        {
            // Returning null from the action means "show a blank screen"; trying to navigate to no
            // page would leave a broken editor.
            GoToEditing("page1");
            _navigatedTo.Clear();

            var result = SaveThenDoAndGoTo("good content", () => null);

            Assert.That(result, Is.EqualTo(SaveOutcome.Saved));
            Assert.That(_saveBookCount, Is.EqualTo(1), "it must still write the book");
            Assert.That(_navigatedTo, Is.Empty, "there is no page to go to");
            Assert.That(
                _stateMachine.ToNavigating("page2"),
                Is.True,
                "we should be in NoPage, from which navigating is allowed again"
            );
        }

        [Test]
        public void ToNoPage_WhileEditingAndNoSaveUnderWay_StillThrows()
        {
            // As with ToNavigating, the relaxation must be scoped to the action.
            GoToEditing("page1");

            Assert.Throws<InvalidOperationException>(
                () => _stateMachine.ToNoPage(),
                "emptying an unsaved page must still be refused"
            );
        }

        [Test]
        public void ToNavigating_WhileEditingAndNoSaveUnderWay_StillThrows()
        {
            // The relaxation above must be scoped to the action; the ordinary guard against
            // leaving a page with unsaved edits has to stay.
            GoToEditing("page1");

            Assert.Throws<InvalidOperationException>(
                () => _stateMachine.ToNavigating("page2"),
                "navigating away from an unsaved page must still be refused"
            );
        }
    }
}
