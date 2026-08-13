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
    }
}
