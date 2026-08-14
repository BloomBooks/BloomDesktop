using System;
using System.Collections.Generic;
using Bloom.Edit;
using NUnit.Framework;

namespace BloomTests.Edit
{
    /// <summary>
    /// Tests for EditingStateMachine.ToSavedInPlace, the transition that saves the current page
    /// from content the browser gathered on its own initiative and stays in Editing (no stripped
    /// page to recover from, so no navigation afterwards). See EditingModel.SavePageInPlace.
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
                requestPageSave: _ => { },
                updateBookWithPageContents: (_, data) => _updatedWith.Add(data),
                saveBook: () => _saveBookCount++,
                hidePage: () => { },
                enableStateTransitions: _ => { }
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

        private bool SaveInPlace(string content)
        {
            return _stateMachine.ToSavedInPlace(content, e => _reportedFailures.Add(e));
        }

        private InPlaceSaveOutcome SaveInPlaceThenGoTo(string content, string pageId)
        {
            return SaveInPlaceThenDoAndGoTo(content, () => pageId);
        }

        private InPlaceSaveOutcome SaveInPlaceThenDoAndGoTo(
            string content,
            Func<string> doBeforeSaveToDisk
        )
        {
            return _stateMachine.ToSavedInPlaceThenNavigating(
                content,
                doBeforeSaveToDisk,
                e => _reportedFailures.Add(e)
            );
        }

        [Test]
        public void ToSavedInPlace_WhileEditing_UpdatesDomAndSavesWithoutNavigating()
        {
            GoToEditing("page1");
            _navigatedTo.Clear(); // the navigation that got us here is not what we're testing

            Assert.That(SaveInPlace("body<SPLIT-DATA>css"), Is.True);

            Assert.That(_updatedWith, Is.EqualTo(new[] { "body<SPLIT-DATA>css" }));
            Assert.That(_saveBookCount, Is.EqualTo(1));
            Assert.That(_navigatedTo, Is.Empty, "an in-place save must not navigate");
            Assert.That(_reportedFailures, Is.Empty);
        }

        [Test]
        public void ToSavedInPlace_Twice_BothSaveBecauseWeStayInEditing()
        {
            GoToEditing("page1");

            Assert.That(SaveInPlace("first"), Is.True);
            Assert.That(
                SaveInPlace("second"),
                Is.True,
                "the first in-place save should have left us in Editing"
            );

            Assert.That(_updatedWith, Is.EqualTo(new[] { "first", "second" }));
            Assert.That(_saveBookCount, Is.EqualTo(2));
        }

        [Test]
        public void ToSavedInPlace_WhileNavigating_DoesNothing()
        {
            Assert.That(_stateMachine.ToNavigating("page1"), Is.True);

            Assert.That(SaveInPlace("body<SPLIT-DATA>css"), Is.False);

            Assert.That(_updatedWith, Is.Empty);
            Assert.That(_saveBookCount, Is.EqualTo(0));
            Assert.That(_reportedFailures, Is.Empty, "not being ready to save is not a failure");
        }

        [Test]
        public void ToSavedInPlace_WhileSavePending_DoesNothing()
        {
            GoToEditing("page1");
            Assert.That(_stateMachine.ToSavePending(() => "page1"), Is.True);

            Assert.That(SaveInPlace("body<SPLIT-DATA>css"), Is.False);

            Assert.That(_updatedWith, Is.Empty);
            Assert.That(_saveBookCount, Is.EqualTo(0));
        }

        [Test]
        public void ToSavedInPlace_BrowserReportedError_ReportsAndSavesNothing()
        {
            GoToEditing("page1");
            _navigatedTo.Clear();

            Assert.That(SaveInPlace("ERROR: something went wrong in the browser"), Is.False);

            Assert.That(_updatedWith, Is.Empty, "we must not put an error message in the book");
            Assert.That(_saveBookCount, Is.EqualTo(0));
            Assert.That(_reportedFailures.Count, Is.EqualTo(1));
            Assert.That(
                _navigatedTo,
                Is.Empty,
                "we are still in Editing with a good page, so there is nothing to recover from"
            );
        }

        [Test]
        public void ToSavedInPlace_RepeatedFailureOnSamePage_ReportsOnlyOnce()
        {
            GoToEditing("page1");

            SaveInPlace("ERROR: first try");
            SaveInPlace("ERROR: second try");

            Assert.That(
                _reportedFailures.Count,
                Is.EqualTo(1),
                "a page that always fails must not lock the user out with repeated dialogs"
            );
        }

        [Test]
        public void ToSavedInPlace_FailureOnDifferentPage_ReportsAgain()
        {
            GoToEditing("page1");
            SaveInPlace("ERROR: first page");
            Assert.That(_reportedFailures.Count, Is.EqualTo(1), "test setup");

            // The only way out of Editing is through a save, so use the ordinary
            // request-content-from-the-browser route to get to another page.
            Assert.That(_stateMachine.ToSavePending(() => "page2"), Is.True, "test setup");
            Assert.That(_stateMachine.ToSavedAndStripped((string)null), Is.True, "test setup");
            Assert.That(_stateMachine.ToEditing("page2"), Is.True, "test setup");

            SaveInPlace("ERROR: second page");

            Assert.That(_reportedFailures.Count, Is.EqualTo(2));
        }

        [Test]
        public void ToSavedInPlace_AfterFailingThenSucceeding_ReportsAgainIfItFailsAgain()
        {
            GoToEditing("page1");
            SaveInPlace("ERROR: first try");
            Assert.That(_reportedFailures.Count, Is.EqualTo(1), "test setup");

            Assert.That(SaveInPlace("good content"), Is.True);
            SaveInPlace("ERROR: later try");

            Assert.That(
                _reportedFailures.Count,
                Is.EqualTo(2),
                "a successful save should clear the 'already reported' memory"
            );
        }

        // ToSavedInPlaceThenNavigating: what a page click does when the click brought the outgoing
        // page's content with it. See EditingModel.SaveThen's pageContentFromBrowser.

        [Test]
        public void ToSavedInPlaceThenNavigating_WhileEditing_SavesThenGoesToTheOtherPage()
        {
            GoToEditing("page1");
            _navigatedTo.Clear();

            Assert.That(
                SaveInPlaceThenGoTo("body<SPLIT-DATA>css", "page2"),
                Is.EqualTo(InPlaceSaveOutcome.Saved)
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
        public void ToSavedInPlaceThenNavigating_NeverEntersSavePending()
        {
            GoToEditing("page1");

            Assert.That(
                SaveInPlaceThenGoTo("content", "page2"),
                Is.EqualTo(InPlaceSaveOutcome.Saved)
            );

            Assert.That(
                _stateMachine.SavePending,
                Is.False,
                "the whole point is that we never wait on the browser, so we never sit in SavePending"
            );
            Assert.That(_stateMachine.Navigating, Is.True);
        }

        [Test]
        public void ToSavedInPlaceThenNavigating_LandsInAStateThatCanAcceptTheNextPageClick()
        {
            // The bug this avoids: while in SavePending, a further page click is silently dropped.
            GoToEditing("page1");
            Assert.That(
                SaveInPlaceThenGoTo("content", "page2"),
                Is.EqualTo(InPlaceSaveOutcome.Saved)
            );

            // Finish arriving, then click again, as an impatient user would.
            Assert.That(_stateMachine.ToEditing("page2"), Is.True);
            _navigatedTo.Clear();

            Assert.That(
                SaveInPlaceThenGoTo("more content", "page3"),
                Is.EqualTo(InPlaceSaveOutcome.Saved)
            );
            Assert.That(_navigatedTo, Is.EqualTo(new[] { "page3" }));
        }

        [Test]
        public void ToSavedInPlaceThenNavigating_WhileNavigating_DoesNothing()
        {
            Assert.That(_stateMachine.ToNavigating("page1"), Is.True);
            _navigatedTo.Clear();

            Assert.That(
                SaveInPlaceThenGoTo("content", "page2"),
                Is.EqualTo(InPlaceSaveOutcome.Declined)
            );

            Assert.That(_updatedWith, Is.Empty);
            Assert.That(_saveBookCount, Is.EqualTo(0));
            Assert.That(_navigatedTo, Is.Empty);
        }

        [Test]
        public void ToSavedInPlaceThenNavigating_FromNoPage_JustGoesThere()
        {
            // Nothing to save, but the click still means "show me that page".
            Assert.That(
                SaveInPlaceThenGoTo("content", "page2"),
                Is.EqualTo(InPlaceSaveOutcome.Saved)
            );

            Assert.That(_updatedWith, Is.Empty, "there was no page to save");
            Assert.That(_navigatedTo, Is.EqualTo(new[] { "page2" }));
        }

        [Test]
        public void ToSavedInPlaceThenNavigating_SaveFails_ReportsAndStaysPut()
        {
            GoToEditing("page1");
            _navigatedTo.Clear();

            Assert.That(
                SaveInPlaceThenGoTo("ERROR: the browser could not gather it", "page2"),
                Is.EqualTo(InPlaceSaveOutcome.Failed),
                "Failed, not Declined: the caller must not fall back and run the action again"
            );

            Assert.That(_saveBookCount, Is.EqualTo(0));
            Assert.That(_reportedFailures.Count, Is.EqualTo(1));
            Assert.That(
                _navigatedTo,
                Is.Empty,
                "going on to the clicked page would silently discard the edits we failed to save"
            );
        }

        // The doBeforeSaveToDisk form: what duplicate/delete/paste/move page do, now that the page
        // list sends the current page's content with the command. The action has to see the user's
        // latest edits (so it must run AFTER the browser's content goes into the book DOM) and its
        // work has to reach disk (so it must run BEFORE the book is written).
        // See EditingModel.SavePageInPlaceThen.

        [Test]
        public void ToSavedInPlaceThenNavigating_RunsTheActionBetweenTheDomUpdateAndTheDiskSave()
        {
            GoToEditing("page1");
            _navigatedTo.Clear();
            var domUpdatesWhenActionRan = -1;
            var saveBookCountWhenActionRan = -1;

            var result = SaveInPlaceThenDoAndGoTo(
                "body<SPLIT-DATA>css",
                () =>
                {
                    domUpdatesWhenActionRan = _updatedWith.Count;
                    saveBookCountWhenActionRan = _saveBookCount;
                    return "theDuplicatedPage";
                }
            );

            Assert.That(result, Is.EqualTo(InPlaceSaveOutcome.Saved));
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
        public void ToSavedInPlaceThenNavigating_WrongState_DoesNotRunTheAction()
        {
            Assert.That(_stateMachine.ToNavigating("page1"), Is.True, "test setup");
            var actionRan = false;

            var result = SaveInPlaceThenDoAndGoTo(
                "content",
                () =>
                {
                    actionRan = true;
                    return "page2";
                }
            );

            Assert.That(result, Is.EqualTo(InPlaceSaveOutcome.Declined));
            Assert.That(
                actionRan,
                Is.False,
                "the caller falls back to SaveThen when we Decline, so the action must not have "
                    + "happened already -- it would then happen twice"
            );
        }

        [Test]
        public void ToSavedInPlaceThenNavigating_SaveFails_DoesNotRunTheAction()
        {
            GoToEditing("page1");
            var actionRan = false;

            var result = SaveInPlaceThenDoAndGoTo(
                "ERROR: the browser could not gather it",
                () =>
                {
                    actionRan = true;
                    return "page2";
                }
            );

            Assert.That(
                result,
                Is.EqualTo(InPlaceSaveOutcome.Failed),
                "Failed, not Declined -- see the next test for why the difference matters"
            );
            Assert.That(
                actionRan,
                Is.False,
                "deleting or duplicating a page we failed to save would act on stale content"
            );
        }

        [Test]
        public void ToSavedInPlaceThenNavigating_ActionThrows_ReportsFailedSoTheCallerWillNotRetry()
        {
            // Found live: relocating a page threw part way through, the caller read the result as
            // "not saved, fall back to SaveThen", and the page got relocated a SECOND time. An
            // action that has already changed the book must never be offered to the fallback.
            GoToEditing("page1");
            var timesActionRan = 0;

            var result = SaveInPlaceThenDoAndGoTo(
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
                Is.EqualTo(InPlaceSaveOutcome.Failed),
                "Declined here would invite the caller to run the action a second time"
            );
            Assert.That(_reportedFailures.Count, Is.EqualTo(1));
        }

        [Test]
        public void ToSavedInPlaceThenNavigating_ActionNavigatesToTheSamePage_DoesNotNavigateTwice()
        {
            // Found live: relocating a page raises RelocatePageEvent, and OnRelocatePage refreshes
            // the display of the page whose HTML just changed -- i.e. the action navigates. That
            // used to throw "Cannot navigate while editing", because unlike the old SaveThen flow
            // (which ran the action in SavedAndStripped) we are still in Editing. It is safe here:
            // the browser's content is already in the book DOM, so there is nothing left to lose.
            GoToEditing("page1");
            _navigatedTo.Clear();

            var result = SaveInPlaceThenDoAndGoTo(
                "good content",
                () =>
                {
                    _stateMachine.ToNavigating("theMovedPage");
                    return "theMovedPage";
                }
            );

            Assert.That(result, Is.EqualTo(InPlaceSaveOutcome.Saved));
            Assert.That(_reportedFailures, Is.Empty, "an action that navigates is legal here");
            Assert.That(_saveBookCount, Is.EqualTo(1));
            Assert.That(
                _navigatedTo,
                Is.EqualTo(new[] { "theMovedPage" }),
                "the action's navigation and ours are to the same page, so it should happen once"
            );
        }

        [Test]
        public void ToSavedInPlaceThenNavigating_ActionNavigatesElsewhere_OurTargetWins()
        {
            GoToEditing("page1");
            _navigatedTo.Clear();

            var result = SaveInPlaceThenDoAndGoTo(
                "good content",
                () =>
                {
                    _stateMachine.ToNavigating("somewhereTheActionWanted");
                    return "whereWeSaidToGo";
                }
            );

            Assert.That(result, Is.EqualTo(InPlaceSaveOutcome.Saved));
            Assert.That(
                _navigatedTo,
                Is.EqualTo(new[] { "somewhereTheActionWanted", "whereWeSaidToGo" }),
                "the page the action named is where we must end up"
            );
        }

        [Test]
        public void ToNavigating_WhileEditingAndNoSaveInPlaceUnderWay_StillThrows()
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
