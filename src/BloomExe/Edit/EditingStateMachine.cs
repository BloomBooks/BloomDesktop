using System;
using System.Diagnostics;

// The states the Edit tab can be in.
//
// There used to be five. SavePending and SavedAndStripped existed only to be somewhere to wait
// while the browser was asked for the page and answered on another API; the browser now volunteers
// it (see PageSnapshot), so a save finishes inside the call that asks for it. Note that the diagram
// at https://www.tldraw.com/r/WDLCDLfNbcDZW1kSXZVli?v=-441,-130,2813,1522&p=page still shows the
// old five and has not been redrawn.
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
    // A normal outcome, not an error: the user may have started changing pages. The caller is free
    // to fall back to its own alternative.
    Declined,

    // Saved, and on the way to the next page.
    Saved,

    // We started and something threw. The browser's content may already be in the book DOM and
    // changeBookBeforeWriting may have run and changed the book. The failure has been reported to the
    // user; the caller must NOT fall back, or the action happens twice.
    Failed,
}

/// <summary>
/// Keeps track of what the Edit tab is doing -- showing nothing, loading a page, or editing one --
/// and refuses transitions that do not make sense from where it is.
///
/// It used to do more. While a save meant asking the browser for the page and waiting for the
/// answer on another API, this was where we waited: two further states existed for that, and the
/// work a caller wanted done afterwards was parked here until the answer came. None of that
/// remains.
///
/// What is left is the guarding, and each guard protects something real rather than this class's
/// own consistency:
///
///   - you cannot navigate away from, or blank, a page that is being edited, because its unsaved
///     edits would go with it. ToNoPageHavingSaved is how a caller that HAS saved says so.
///   - a "page finished loading" notification for a page we are no longer going to is ignored,
///     since those arrive asynchronously and can be late.
///   - a save arriving while a page is still loading is declined: there is no settled page to save.
///   - a save requested from inside another save's own action is declined rather than re-entered.
///     Reordering a page does exactly this, by changing the page selection (see
///     _runningSaveThenNavigateAction).
/// </summary>
public class EditingStateMachine
{
    private State _currentState;
    private string _pageId;
    private string _pageIdWeFailedToSave;
    private Action<string> _navigate; // arg is (pageId)

    private Action<string, string> _updateBookWithPageContents; // args are (pageId, pageContent)
    private Action _saveBook;

    // Set only while SaveThenNavigate is running its changeBookBeforeWriting. In that window the
    // browser's content is already in the book DOM, so ToNavigating's "cannot navigate while
    // editing" guard does not apply -- there are no unsaved changes left to lose. Some actions do
    // navigate: relocating a page raises RelocatePageEvent, and EditingModel.OnRelocatePage
    // refreshes the display of the page whose HTML (side, page number) just changed. Under the old
    // asynchronous flow that was legal because the action ran in a state of its own.
    private bool _runningSaveThenNavigateAction;
    private Action _hidePage;

    /// <summary>
    /// Set up a state machine. It must be passed four actions:
    /// </summary>
    /// <param name="navigate">Called to start navigation to another (or the same) page. String is page ID.</param>
    /// <param name="updateBookWithPageContents">Called with page ID and pageContent to update the main DOM with current page content</param>
    /// <param name="saveBook">Called to save the current state of the DOM to disk.</param>
    /// <param name="hidePage">Called to make the transition to NoPage (when edit tab is hidden).</param>
    public EditingStateMachine(
        Action<string> navigate,
        Action<string, string> updateBookWithPageContents,
        Action saveBook,
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
    /// Leave the editor showing nothing, when the caller has ALREADY written the page and the
    /// book, synchronously, itself. Used when the user leaves the Edit tab.
    ///
    /// This exists because ToNoPage refuses to go straight from Editing: that guard is there to
    /// stop us abandoning a page whose edits have not been saved. Here they have been -- the
    /// browser volunteered the page and the caller merged and wrote it before calling (see
    /// PageSnapshot) -- so there is nothing for the guard to protect, and saying so explicitly is
    /// better than the caller pretending to be a save-then-navigate action.
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
        // Anything else -- mid-navigation, or already blank -- ToNoPage already handles.
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
                if (_runningSaveThenNavigateAction)
                {
                    // See _runningSaveThenNavigateAction: we have just saved, so the guard below
                    // (which is about losing unsaved edits) has nothing to protect. This is
                    // the "action returned null, leave the editor blank" case.
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
    /// Called to initiate navigation to a new page (or the same one again).
    /// Should not be called when there are unsaved (or incompletely saved) changes.
    /// </summary>
    public bool ToNavigating(string pageId)
    {
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
                if (_runningSaveThenNavigateAction)
                {
                    // See _runningSaveThenNavigateAction: we have just saved, so the guard below
                    // (which is about losing unsaved edits) has nothing to protect.
                    StartNavigating(pageId);
                    return true;
                }
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
        _navigate(pageId);
    }

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
    /// Save the current page from the content we have for it, let changeBookBeforeWriting change
    /// the book, then go to whichever page it names. Editing -> Navigating in one step, because
    /// there is nothing to wait for: the browser volunteers the page as it is edited (see
    /// PageSnapshot), so we already have it.
    ///
    /// pageContent may be null, meaning the page has not been changed since it loaded; then there
    /// is nothing to merge, but the action still runs and the book is still written if anything
    /// else needs it.
    ///
    /// changeBookBeforeWriting runs after the browser's content has been merged into the book DOM
    /// and before the book is written to disk (so a page it duplicates or deletes already reflects
    /// the user's latest edits), and it returns the id of the page to show afterwards, or null to
    /// leave the editor blank. For a caller that only wants to change pages it is simply
    /// () => theNewPageId. It is allowed to navigate (see _runningSaveThenNavigateAction); if it
    /// does, the navigation we do afterwards to its returned page supersedes it, or is ignored if
    /// it is to the same page.
    ///
    /// If it fails we report it and do NOT navigate: doing so would throw away the edits we failed
    /// to save, and we are not in a broken state we have to escape, since the browser still has the
    /// page intact and editable. Declined means the action never ran and the caller may fall back;
    /// Failed means it may have run already and the caller must not run it again.
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
                    if (_runningSaveThenNavigateAction)
                    {
                        // We are inside a save's own action, which is allowed to do things that
                        // normally start a save -- changing the page selection does, via
                        // PageListController.OnPageSelectedChanged. There is nothing for a second
                        // save to do: the content is already merged and _saveBook() is about to
                        // run. Accepting it would re-enter this method and run the whole thing
                        // again, including the caller's action.
                        LogIgnore("save then navigate");
                        return SaveOutcome.Declined;
                    }
                    LogTransition("saved, then navigating", _pageId);
                    if (pageContent != null)
                    {
                        if (pageContent.StartsWith("ERROR:"))
                            throw new ApplicationException(pageContent);
                        _updateBookWithPageContents(_pageId, pageContent);
                    }
                    _pageIdWeFailedToSave = null;
                    RunActionThenSaveAndNavigate(changeBookBeforeWriting);
                    return SaveOutcome.Saved;
                case State.NoPage:
                    // There is no browser content to merge, but the action can still change the
                    // book (it may duplicate or delete a page), and that has to reach disk just
                    // the same: run the action, save the book, then navigate.
                    RunActionThenSaveAndNavigate(changeBookBeforeWriting);
                    return SaveOutcome.Saved;
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
            // Whether the page content failed to apply or the caller's action threw, the caller is
            // told: by reportFailure, and by the Failed outcome. We do NOT navigate: the browser
            // still has an intact page in front of the user, and rebuilding it from an in-memory
            // book we know to be half-updated would be the worse of the two.
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
    /// The middle of SaveThenNavigate, from the point where the browser's content is safely in the
    /// book DOM: run the caller's action, write the book, and go to the page the action named.
    /// Separated out only so that _runningSaveThenNavigateAction is obviously scoped to the action,
    /// and obviously cleared even if it throws.
    /// </summary>
    private void RunActionThenSaveAndNavigate(Func<string> changeBookBeforeWriting)
    {
        _runningSaveThenNavigateAction = true;
        try
        {
            var pageIdToGoTo = changeBookBeforeWriting();
            // If the write fails we still navigate. The action has already changed the book in
            // memory, and the page list already shows the result; the user has been told about the
            // failure, and EditingModel.SaveBookToDisk keeps the book marked as needing a full
            // write, so the next save retries it. Staying put would leave the editor showing a page
            // the list no longer agrees with.
            _saveBook();
            if (pageIdToGoTo == null)
            {
                // The contract: the action returns null to say "leave the editor blank". Trying
                // to navigate to no page would just leave a broken editor.
                ToNoPage();
                return;
            }
            // Via ToNavigating rather than StartNavigating so that an action which already
            // navigated to this very page (as relocating one does) is not made to do it twice.
            // While _runningSaveThenNavigateAction is set, ToNavigating accepts being called from
            // Editing, which is the state we are still in if the action did not navigate.
            ToNavigating(pageIdToGoTo);
        }
        finally
        {
            _runningSaveThenNavigateAction = false;
        }
    }

    private void Log(string message)
    {
        Debug.WriteLine("[EditingStateMachine] " + message);
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
