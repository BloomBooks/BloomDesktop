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
/// What an attempt at an in-place save actually did. The point of the distinction is the third
/// case: a caller that has a fallback (MergeCurrentPageThenSave) must only use it when nothing
/// happened, because its changeBookBeforeWriting is usually not something you can afford to do
/// twice -- running it again would duplicate or delete a second page.
/// </summary>
public enum InPlaceSaveOutcome
{
    // We were not in a state to save, so nothing was written and changeBookBeforeWriting did NOT run.
    // A normal outcome, not an error: the user may have started changing pages. The caller is free
    // to fall back to its own alternative.
    Declined,

    // Saved, and (for the ...ThenNavigating form) on the way to the next page.
    Saved,

    // We started and something threw. The browser's content may already be in the book DOM and
    // changeBookBeforeWriting may have run and changed the book. The failure has been reported to the
    // user; the caller must NOT fall back, or the action happens twice.
    Failed,

    // We MUST not write this page at all -- an external process has replaced the book on disk and
    // the user's page is about to be discarded in favour of what it wrote. Nothing was written and
    // changeBookBeforeWriting did NOT run, exactly as for Declined; the difference is that the
    // caller must NOT treat it as "nothing happened and I may try another way", because trying
    // again would write the page and clobber the other program's work. Refused and Declined look
    // alike and mean opposite things, which is why they are separate values rather than one
    // "didn't save".
    Refused,
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
///     _runningSaveInPlaceAction).
/// </summary>
public class EditingStateMachine
{
    private State _currentState;
    private string _pageId;
    private string _pageIdWeFailedToSave;
    private Action<string> _navigate; // arg is (pageId)

    private Action<string, string> _updateBookWithPageContents; // args are (pageId, pageContentData)
    private Action _saveBook;

    // Set only while ToSavedInPlaceThenNavigating is running its changeBookBeforeWriting. In that window
    // the browser's content is already in the book DOM, so ToNavigating's "cannot navigate while
    // editing" guard does not apply -- there are no unsaved changes left to lose. Some actions do
    // navigate: relocating a page raises RelocatePageEvent, and EditingModel.OnRelocatePage
    // refreshes the display of the page whose HTML (side, page number) just changed. Under the old
    // asynchronous flow that was legal because the action ran in a state of its own.
    private bool _runningSaveInPlaceAction;
    private Action _hidePage;

    /// <summary>
    /// Set up a state machine. It must be passed four actions:
    /// </summary>
    /// <param name="navigate">Called to start navigation to another (or the same) page. String is page ID.</param>
    /// <param name="updateBookWithPageContents">Called with page ID and pageContentData to update the main DOM with current page content</param>
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
    /// better than the caller pretending to be a save-in-place action.
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
                if (_runningSaveInPlaceAction)
                {
                    // See _runningSaveInPlaceAction: we have just saved, so the guard below
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
                if (_runningSaveInPlaceAction)
                {
                    // See _runningSaveInPlaceAction: we have just saved, so the guard below
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

    public bool ToSavedInPlace(string pageContentData, Action<Exception> reportFailure)
    {
        try
        {
            switch (_currentState)
            {
                case State.Editing:
                    LogTransition("saved in place", _pageId);
                    if (pageContentData.StartsWith("ERROR:"))
                        throw new ApplicationException(pageContentData);
                    _updateBookWithPageContents(_pageId, pageContentData);
                    _pageIdWeFailedToSave = null;
                    _saveBook();
                    return true;
                case State.NoPage:
                case State.Navigating:
                    LogIgnore("save in place");
                    return false;
                default:
                    throw new InvalidOperationException(
                        "Unknown state In ToSavedInPlace(): " + _currentState.ToString()
                    );
            }
        }
        catch (Exception e)
        {
            // We don't have to navigate to get out of an invalid state: we never left Editing, and
            // the browser still has the intact page. So all we owe the user is the report, and the
            // caller a 'false'. We report only once per page, so that a page which fails every time
            // does not lock the user out of Bloom.
            if (_pageId != _pageIdWeFailedToSave)
            {
                _pageIdWeFailedToSave = _pageId;
                reportFailure(e);
            }
            return false;
        }
    }

    /// <summary>
    /// Save the current page from the content we have for it, optionally change the book in some
    /// way, then go to whichever page changeBookBeforeWriting names. Editing -> Navigating in one
    /// step, because there is nothing to wait for: the browser volunteers the page as it is edited
    /// (see PageSnapshot), so we already have it.
    ///
    /// changeBookBeforeWriting runs after the browser's content has been merged into the book DOM
    /// and before the book is written to disk
    /// (so a page it duplicates or deletes already reflects the user's latest edits), and it
    /// returns the id of the page to show afterwards. For a caller that only wants to change pages
    /// it is simply () => theNewPageId. It is allowed to navigate (see _runningSaveInPlaceAction);
    /// if it does, the navigation we do afterwards to its returned page simply supersedes it, or is
    /// ignored if it is to the same page.
    ///
    /// If it fails we report it and do NOT navigate: doing so would throw away the edits we failed
    /// to save, and we are not in a broken state we have to escape, since the browser still has the
    /// page intact and editable. Note the difference between the two failure-ish outcomes -- see
    /// InPlaceSaveOutcome, and be careful to preserve it: Declined means the action never ran and
    /// the caller may fall back, whereas Failed means it may have run already and the caller must
    /// not run it again.
    /// </summary>
    public InPlaceSaveOutcome ToSavedInPlaceThenNavigating(
        string pageContentData,
        Func<string> changeBookBeforeWriting,
        Action<Exception> reportFailure
    )
    {
        try
        {
            switch (_currentState)
            {
                case State.Editing:
                    if (_runningSaveInPlaceAction)
                    {
                        // We are inside a save's own action, which is allowed to do things that
                        // normally start a save -- changing the page selection does, via
                        // PageListController.OnPageSelectedChanged. There is nothing for a second
                        // save to do: the content is already merged and _saveBook() is about to
                        // run. Accepting it would re-enter this method and run the whole thing
                        // again, including the caller's action.
                        //
                        // This guard used to live on the transition that asked the browser for
                        // the page, because that is where a nested save landed while such a path
                        // existed. There isn't one, so nested saves arrive here instead.
                        LogIgnore("save in place then navigate");
                        return InPlaceSaveOutcome.Declined;
                    }
                    LogTransition("saved in place, then navigating", _pageId);
                    // Null means the page has not been changed since it loaded, so there is
                    // nothing to merge -- but the action still has to run and the book still has
                    // to be written, because the action itself (duplicating a page, say) is a
                    // change. See PageSnapshot: a page nobody edited never produces a snapshot,
                    // which is exactly how we know there is nothing to merge rather than that we
                    // have not been told yet.
                    if (pageContentData != null)
                    {
                        if (pageContentData.StartsWith("ERROR:"))
                            throw new ApplicationException(pageContentData);
                        _updateBookWithPageContents(_pageId, pageContentData);
                    }
                    _pageIdWeFailedToSave = null;
                    RunActionThenSaveAndNavigate(changeBookBeforeWriting);
                    return InPlaceSaveOutcome.Saved;
                case State.NoPage:
                    // There is no browser content to merge, but the action can still change the
                    // book (it may duplicate or delete a page), and that has to reach disk just
                    // the same: run the action, save the book, then navigate.
                    RunActionThenSaveAndNavigate(changeBookBeforeWriting);
                    return InPlaceSaveOutcome.Saved;
                case State.Navigating:
                    LogIgnore("save in place then navigate");
                    return InPlaceSaveOutcome.Declined;
                default:
                    throw new InvalidOperationException(
                        "Unknown state In ToSavedInPlaceThenNavigating(): "
                            + _currentState.ToString()
                    );
            }
        }
        catch (Exception e)
        {
            // This covers the caller's action throwing as well as the page content failing to
            // apply, which is what BL-16776 asked for: the caller is told either way. Here it is
            // told twice over -- reportFailure, and the Failed outcome it gets back -- because the
            // call is synchronous now, so there is a return value to carry it. In the shape
            // BL-16776 fixed there was none, and an exception left the caller waiting for ever.
            //
            // We do NOT navigate, unlike that older path. It had to, because the page had been
            // stripped to be read and was no longer editable; nothing strips the page now, so the
            // browser still has an intact one in front of the user, and rebuilding it from an
            // in-memory book we know to be half-updated would be the worse of the two.
            if (_pageId != _pageIdWeFailedToSave)
            {
                _pageIdWeFailedToSave = _pageId;
                reportFailure(e);
            }
            return InPlaceSaveOutcome.Failed;
        }
    }

    /// <summary>
    /// The middle of ToSavedInPlaceThenNavigating, from the point where the browser's content is
    /// safely in the book DOM: run the caller's action, write the book, and go to the page the
    /// action named. Separated out only so that _runningSaveInPlaceAction is obviously scoped to
    /// the action, and obviously cleared even if it throws.
    /// </summary>
    private void RunActionThenSaveAndNavigate(Func<string> changeBookBeforeWriting)
    {
        _runningSaveInPlaceAction = true;
        try
        {
            var pageIdToGoTo = changeBookBeforeWriting();
            _saveBook();
            if (pageIdToGoTo == null)
            {
                // The contract: the action returns null to say "leave the editor blank" (which
                // is how leaving the Edit tab saves). Trying to navigate to no page would just
                // leave a broken editor.
                ToNoPage();
                return;
            }
            // Via ToNavigating rather than StartNavigating so that an action which already
            // navigated to this very page (as relocating one does) is not made to do it twice.
            // While _runningSaveInPlaceAction is set, ToNavigating accepts being called from
            // Editing, which is the state we are still in if the action did not navigate.
            ToNavigating(pageIdToGoTo);
        }
        finally
        {
            _runningSaveInPlaceAction = false;
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
