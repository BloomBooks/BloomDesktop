using System;
using System.Diagnostics;
using SIL.Reporting;

// The states the Edit tab can be in.
public enum State
{
    NoPage,
    Navigating,
    Editing,
}

/// <summary>
/// What SaveThenNavigate did. The distinction that matters is Declined versus Failed: a caller
/// with a fallback must use it only when nothing happened, because changeBookBeforeWriting is
/// usually not something you can afford to run twice -- it would duplicate or delete a second page.
/// </summary>
public enum SaveOutcome
{
    // We were not in a state to save, so nothing was written and changeBookBeforeWriting did NOT run.
    // Not an error (the user may have started changing pages); the caller may fall back.
    Declined,

    // Saved, and on the way to the next page.
    Saved,

    // The save did not reach disk, either because something threw (reported; we did not navigate)
    // or because the write failed (the write reports it; we navigated anyway, see
    // RunActionThenSaveAndNavigate). Either way changeBookBeforeWriting may already have changed
    // the book, so the caller must NOT fall back, and must skip anything it does only after a save
    // that reached disk.
    Failed,
}

/// <summary>
/// Keeps track of what the Edit tab is doing -- showing nothing, loading a page, or editing one --
/// and refuses transitions that do not make sense from where it is. Each guard protects something
/// real:
///
///   - you cannot navigate away from, or blank, a page that is being edited, because its unsaved
///     edits would go with it. ToNoPageHavingSaved is how a caller that HAS saved says so.
///   - a "page finished loading" notification for a page we are no longer going to is ignored,
///     since those arrive asynchronously and can be late.
///   - a save arriving while a page is still loading is declined: there is no settled page to save.
///     A page click declined that way is remembered (GoToOnceLoaded) and acted on once the page
///     has loaded, so that clicking quickly through pages does not lose the last click.
///   - a save requested from inside another save's own action is declined rather than re-entered.
///     Reordering a page does exactly this, by changing the page selection (see _saveInProgress).
/// </summary>
public class EditingStateMachine
{
    private State _currentState;
    private string _pageId;
    private string _pageIdWeFailedToSave;
    private Action<string> _navigate; // arg is (pageId)

    private Action<string, string> _updateBookWithPageContents; // args are (pageId, pageContent)
    private Func<bool> _saveBook; // returns whether whatever needed writing reached disk

    // Set while SaveThenNavigate runs its changeBookBeforeWriting and writes the book. The browser's
    // content is already in the book DOM, so there are no unsaved changes left to lose; a request
    // for another save is declined rather than re-entered. Navigation is prevented because the book
    // is being written: the write can rename the book's folder, which the page's URL is in
    // (BL-17055). A navigation asked for meanwhile is not kept for later; once the book is written
    // we go to the page the action named.
    private bool _saveInProgress;
    private Action _hidePage;

    // A page the user asked for while another was still loading; see GoToOnceLoaded.
    private string _pageToGoToOnceLoaded;

    /// <summary>
    /// Set up a state machine. It must be passed four actions:
    /// </summary>
    /// <param name="navigate">Called to start navigation to another (or the same) page. String is page ID.</param>
    /// <param name="updateBookWithPageContents">Called with page ID and pageContent to update the main DOM with current page content</param>
    /// <param name="saveBook">Called to save the current state of the DOM to disk. Returns false if
    /// something needed writing and the write failed (which it reports itself).</param>
    /// <param name="hidePage">Called to make the transition to NoPage (when edit tab is hidden).</param>
    public EditingStateMachine(
        Action<string> navigate,
        Action<string, string> updateBookWithPageContents,
        Func<bool> saveBook,
        Action hidePage
    )
    {
        _currentState = State.NoPage;
        _navigate = navigate;
        _updateBookWithPageContents = updateBookWithPageContents;
        _saveBook = saveBook;
        _hidePage = hidePage;
    }

    /// <summary>
    /// Leave the editor showing nothing, when the caller has ALREADY merged and written the page
    /// and the book itself (used when the user leaves the Edit tab). ToNoPage refuses to leave
    /// Editing because that would abandon unsaved edits; here there are none.
    /// </summary>
    public bool ToNoPageHavingSaved()
    {
        // No page will load now, and a page asked for while one was loading is not wanted on
        // coming back to the tab.
        _pageToGoToOnceLoaded = null;
        if (_currentState == State.Editing)
        {
            LogTransition("empty page (already saved)", null);
            _hidePage();
            _currentState = State.NoPage;
            return true;
        }
        return ToNoPage();
    }

    /// <summary>
    /// Go to the state where we have no page loaded (switching to another tab). Refuses to abandon
    /// a page that is being edited; ToNoPageHavingSaved is the way past that for a caller that has
    /// already saved.
    /// </summary>
    public bool ToNoPage()
    {
        _pageToGoToOnceLoaded = null; // as in ToNoPageHavingSaved
        switch (_currentState)
        {
            case State.NoPage:
                LogIgnore("empty page");
                return true;
            case State.Navigating:
                LogShortcut("empty page");
                _hidePage();
                _currentState = State.NoPage;
                return true;
            case State.Editing:
                if (_saveInProgress)
                {
                    // A save's action blanking the editor (changing the page's orientation hides
                    // and re-shows it). Its content is already merged (see _saveInProgress).
                    LogTransition("empty page", null);
                    _hidePage();
                    _currentState = State.NoPage;
                    return true;
                }
                LogError("empty page");
                throw new InvalidOperationException("Cannot empty page while editing.");
            default:
                throw new InvalidOperationException(
                    "Unknown state in ToNoPage(): " + _currentState.ToString()
                );
        }
    }

    /// <summary>
    /// True if we are in the process of navigating to a new page.
    /// </summary>
    public bool Navigating => _currentState == State.Navigating;

    /// <summary>
    /// True if a page is loaded and being edited, so that a save (and anything that starts with
    /// one, such as duplicating or deleting the page) will be acted on rather than ignored.
    /// </summary>
    public bool Editing => _currentState == State.Editing;

    /// <summary>
    /// The state we are in, and the page it is about. These exist for automation: the e2e suite
    /// waits until the Edit tab is settled before it asks that tab for anything, and the DOM
    /// cannot tell it that (see E2eTestingApi's editState endpoint). Read only.
    /// </summary>
    public State CurrentState => _currentState;

    /// <summary>
    /// The page the current state is about, or null. See CurrentState.
    /// </summary>
    public string CurrentPageId => _pageId;

    /// <summary>
    /// Called to initiate navigation to a new page (or the same one again).
    /// Should not be called when there are unsaved (or incompletely saved) changes.
    /// </summary>
    public bool ToNavigating(string pageId)
    {
        if (_saveInProgress)
        {
            Log(
                $"Not navigating to {pageId} because the book is being written; we will go to the page the save's action names"
            );
            return true;
        }
        switch (_currentState)
        {
            case State.NoPage:
                StartNavigating(pageId);
                return true;
            case State.Navigating:
                if (_pageId == pageId)
                {
                    LogIgnore("navigate");
                    return true; // we're already headed there
                }
                else
                {
                    StartNavigating(pageId);
                    return true;
                }
            case State.Editing:
                LogError("navigate");
                throw new InvalidOperationException("Cannot navigate while editing");
            default:
                throw new InvalidOperationException(
                    "Unknown state in ToNavigating(): " + _currentState.ToString()
                );
        }
    }

    private void StartNavigating(string pageId)
    {
        LogTransition("navigating", pageId);
        _currentState = State.Navigating;
        _pageId = pageId;
        if (_pageToGoToOnceLoaded == pageId)
            _pageToGoToOnceLoaded = null; // we are on the way there now
        _navigate(pageId);
    }

    /// <summary>
    /// Remember that the user asked for <paramref name="pageId"/> (by clicking it in the page list)
    /// while another page was still loading, when a save, and so a page change, is declined. Once
    /// the loading page reaches Editing, the caller takes it (TakePageToGoToOnceLoaded) and goes
    /// there, so a click is not lost however quickly the user clicks. Only the latest is kept: it
    /// is where the user wants to end up. Returns false, remembering nothing, when no page is
    /// loading, so the request was declined for some other reason.
    /// </summary>
    public bool GoToOnceLoaded(string pageId)
    {
        if (_currentState != State.Navigating)
            return false;
        if (pageId == _pageId)
        {
            // The page that is loading is already where the user will end up.
            _pageToGoToOnceLoaded = null;
            return true;
        }
        Log($"Will go to {pageId} once {_pageId} has loaded");
        _pageToGoToOnceLoaded = pageId;
        return true;
    }

    /// <summary>
    /// The page GoToOnceLoaded remembered, or null, and forget it. The caller asks this when the
    /// loading page has reached Editing.
    /// </summary>
    public string TakePageToGoToOnceLoaded()
    {
        var pageId = _pageToGoToOnceLoaded;
        _pageToGoToOnceLoaded = null;
        return pageId;
    }

    /// <summary>
    /// The page GoToOnceLoaded remembered, or null. Read only.
    /// </summary>
    public string PageToGoToOnceLoaded => _pageToGoToOnceLoaded;

    /// <summary>
    /// Called after we hear from the browser JS that the dom is finished loading
    /// </summary>
    public bool ToEditing(string pageId)
    {
        switch (_currentState)
        {
            case State.Navigating:
                if (_pageId == pageId)
                {
                    LogTransition("editing", pageId);
                    _currentState = State.Editing;
                    return true;
                }
                else
                {
                    LogIgnore("edit");
                    return false;
                }
            default:
                LogIgnore("edit");
                return false;
        }
    }

    /// <summary>
    /// Save the current page from the content the browser has already volunteered (see
    /// PageSnapshot), let changeBookBeforeWriting change the book, then go to whichever page it
    /// names.
    ///
    /// pageContent may be null, meaning the page has not been changed since it loaded; then there
    /// is nothing to merge, but the action still runs and the book is still written if anything
    /// else needs it.
    ///
    /// changeBookBeforeWriting runs after the browser's content has been merged into the book DOM
    /// and before the book is written (so a page it duplicates or deletes already reflects the
    /// user's latest edits). It returns the id of the page to show afterwards, or null to leave the
    /// editor blank. It may ask to navigate (relocating a page refreshes it, and so does closing a
    /// picture's Copyright and License dialog), but that is prevented because the book is being
    /// written; we go to the page it returns instead (see _saveInProgress).
    ///
    /// If something throws we report it and do NOT navigate: that would throw away the edits we
    /// failed to save, and the browser still has the page intact and editable.
    /// </summary>
    public SaveOutcome SaveThenNavigate(
        string pageContent,
        Func<string> changeBookBeforeWriting,
        Action<Exception> reportFailure
    )
    {
        try
        {
            switch (_currentState)
            {
                case State.Editing:
                    if (_saveInProgress)
                    {
                        // We are inside a save's own action, which may do things that normally
                        // start a save (changing the page selection does, via
                        // PageListController.OnPageSelectedChanged). The content is already merged
                        // and _saveBook() is about to run; accepting would re-enter this method and
                        // run the caller's action again.
                        LogIgnore("save then navigate");
                        return SaveOutcome.Declined;
                    }
                    LogTransition("saved, then navigating", _pageId);
                    if (pageContent != null)
                    {
                        if (pageContent.StartsWith("ERROR:", StringComparison.Ordinal))
                            throw new ApplicationException(pageContent);
                        _updateBookWithPageContents(_pageId, pageContent);
                    }
                    _pageIdWeFailedToSave = null;
                    return RunActionThenSaveAndNavigate(changeBookBeforeWriting)
                        ? SaveOutcome.Saved
                        : SaveOutcome.Failed;
                case State.NoPage:
                    // No browser content to merge, but the action may still change the book (it
                    // may duplicate or delete a page), and that has to reach disk just the same.
                    return RunActionThenSaveAndNavigate(changeBookBeforeWriting)
                        ? SaveOutcome.Saved
                        : SaveOutcome.Failed;
                case State.Navigating:
                    LogIgnore("save then navigate");
                    return SaveOutcome.Declined;
                default:
                    throw new InvalidOperationException(
                        "Unknown state In SaveThenNavigate(): " + _currentState.ToString()
                    );
            }
        }
        catch (Exception e)
        {
            // Not navigating: the browser still has an intact page in front of the user, and
            // rebuilding it from an in-memory book we know to be half-updated would be worse.
            ReportFailureOncePerPage(e, reportFailure);
            return SaveOutcome.Failed;
        }
    }

    /// <summary>
    /// Report a save failure, but only once per page, so that a page which fails every time does
    /// not lock the user out of Bloom with a dialog on every attempt. A successful save of the page
    /// clears the memory, so a later failure on it is reported again.
    /// </summary>
    private void ReportFailureOncePerPage(Exception e, Action<Exception> reportFailure)
    {
        if (_pageId == _pageIdWeFailedToSave)
            return;
        _pageIdWeFailedToSave = _pageId;
        reportFailure(e);
    }

    /// <summary>
    /// The part of SaveThenNavigate after the browser's content is in the book DOM: run the
    /// caller's action, write the book, and go to the page the action named. Returns whether the
    /// write reached disk. Separate so that _saveInProgress is clearly scoped to the action and
    /// the write, and cleared even if one of them throws.
    /// </summary>
    private bool RunActionThenSaveAndNavigate(Func<string> changeBookBeforeWriting)
    {
        string pageIdToGoTo;
        bool written;
        _saveInProgress = true;
        try
        {
            pageIdToGoTo = changeBookBeforeWriting();
            // If the write fails we still navigate, but return false. The action has already
            // changed the book in memory and the page list shows the result; the write reported
            // the failure, and EditingModel.SaveBookToDisk keeps the book marked as needing a full
            // write, so the next save retries it. Staying put would leave the editor showing a
            // page the list no longer agrees with.
            written = _saveBook();
        }
        finally
        {
            _saveInProgress = false;
        }
        // Cleared before we navigate, so that if loading the page fails, the fallback to the first
        // page (EditingModel.StartNavigationToEditPage) is not prevented.
        if (pageIdToGoTo == null)
        {
            if (_currentState != State.NoPage)
            {
                LogTransition("empty page", null);
                _hidePage();
                _currentState = State.NoPage;
            }
            return written;
        }
        StartNavigating(pageIdToGoTo);
        return written;
    }

    private void Log(string message)
    {
        Debug.WriteLine("[EditingStateMachine] " + message);
        // Under --e2e these go into Bloom's log as well. A test that waits for a page the edit tab
        // never shows is otherwise a mystery: nothing else records which state the tab was in or
        // which request it turned down.
        if (Bloom.Program.RunningE2eTests)
            Logger.WriteEvent("[EditingStateMachine] " + message);
    }

    private void LogTransition(string nextState, string nextPageId)
    {
        Log($"{_currentState}({_pageId}) --> {nextState}({nextPageId})");
    }

    private void LogError(string transitionRequest)
    {
        Log($"Error: Cannot {transitionRequest} while in {_currentState} state");
    }

    private void LogIgnore(string transitionRequest, string nextPageId = null)
    {
        Log(
            $"Ignoring {transitionRequest}({nextPageId}) request while in {_currentState}({_pageId}) state"
        );
    }

    private void LogShortcut(string transitionRequest)
    {
        Log($"Shortcutting {transitionRequest} request while in {_currentState} state");
    }
}
