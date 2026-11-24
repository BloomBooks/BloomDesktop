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
///   - a "page finished loading" notification is accepted only from the load we started, which
///     it names by the navigation number we gave it (see NavigationNumber). Others are ignored:
///     one for a page we are no longer going to arrives late, and the page layout update loads
///     every page off-screen, each of which sends one (BL-17089).
///   - no page is shown while the Edit tab is hidden. A request that would show one (a click
///     that reaches us after the user has gone to another tab) still saves, but the page it
///     names is handed on to be shown when the tab comes back (BL-17089).
///   - a save arriving while a page is still loading is declined: there is no settled page to save.
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
    private Func<bool> _canShowPage;
    private Action<string> _showPageWhenVisible; // arg is (pageId)

    /// <summary>
    /// Set up a state machine. It must be passed six actions:
    /// </summary>
    /// <param name="navigate">Called to start navigation to another (or the same) page. String is page ID.
    /// NavigationNumber is already the number of this navigation when it is called.</param>
    /// <param name="updateBookWithPageContents">Called with page ID and pageContent to update the main DOM with current page content</param>
    /// <param name="saveBook">Called to save the current state of the DOM to disk. Returns false if
    /// something needed writing and the write failed (which it reports itself).</param>
    /// <param name="hidePage">Called to make the transition to NoPage (when edit tab is hidden).</param>
    /// <param name="canShowPage">Returns false while the Edit tab is hidden, when no page may be shown.</param>
    /// <param name="showPageWhenVisible">Called, instead of navigating, with the page that a
    /// navigation asked for while the Edit tab was hidden, so that it can be shown when the tab
    /// next becomes visible.</param>
    public EditingStateMachine(
        Action<string> navigate,
        Action<string, string> updateBookWithPageContents,
        Func<bool> saveBook,
        Action hidePage,
        Func<bool> canShowPage,
        Action<string> showPageWhenVisible
    )
    {
        _currentState = State.NoPage;
        _navigate = navigate;
        _updateBookWithPageContents = updateBookWithPageContents;
        _saveBook = saveBook;
        _hidePage = hidePage;
        _canShowPage = canShowPage;
        _showPageWhenVisible = showPageWhenVisible;
    }

    /// <summary>
    /// The number of the latest navigation we started. The page loaded for it must hand this back
    /// when it reports that it has loaded (see ToEditing), so that a report from any other load
    /// of a page cannot make us think the page being edited has arrived. EditingModel puts it in
    /// the URL of the page it shows (GetUrlForCurrentPage).
    /// </summary>
    public int NavigationNumber { get; private set; }

    /// <summary>
    /// Leave the editor showing nothing, when the caller has ALREADY merged and written the page
    /// and the book itself (used when the user leaves the Edit tab). ToNoPage refuses to leave
    /// Editing because that would abandon unsaved edits; here there are none.
    /// </summary>
    public bool ToNoPageHavingSaved()
    {
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
        if (!_canShowPage())
        {
            // The Edit tab is hidden, so nothing can be shown, and a page that loaded now would be
            // edited where the user cannot see it. Leaving the tab has already saved the page and
            // emptied the editor; whatever asked for this page has already been saved too (this
            // is reached after the write in RunActionThenSaveAndNavigate). So the page is just
            // the one to show when the user comes back.
            Log(
                $"Not navigating to {pageId} because the Edit tab is hidden; it will be shown when the tab is next visible"
            );
            EmptyTheEditorIfShowingAPage();
            _showPageWhenVisible(pageId);
            return;
        }
        NavigationNumber++;
        LogTransition("navigating", pageId);
        _currentState = State.Navigating;
        _pageId = pageId;
        _navigate(pageId);
    }

    /// <summary>
    /// Called after we hear from the browser JS that the dom is finished loading.
    /// <paramref name="navigationNumber"/> is the NavigationNumber the page was loaded with, or
    /// null if it was not loaded by us (the page layout update loads pages off-screen, and a
    /// developer may open the page in another browser). Only the load of the navigation we are
    /// waiting for takes us to Editing.
    /// </summary>
    public bool ToEditing(string pageId, int? navigationNumber)
    {
        if (
            _currentState == State.Navigating
            && _pageId == pageId
            && navigationNumber == NavigationNumber
        )
        {
            LogTransition("editing", pageId);
            _currentState = State.Editing;
            return true;
        }
        LogIgnore("edit", $"{pageId} from navigation {navigationNumber}");
        return false;
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
            EmptyTheEditorIfShowingAPage();
            return written;
        }
        StartNavigating(pageIdToGoTo);
        return written;
    }

    /// <summary>
    /// Go to NoPage, hiding the page, unless we are there already. For callers that have saved
    /// whatever needed saving.
    /// </summary>
    private void EmptyTheEditorIfShowingAPage()
    {
        if (_currentState == State.NoPage)
            return;
        LogTransition("empty page", null);
        _hidePage();
        _currentState = State.NoPage;
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
