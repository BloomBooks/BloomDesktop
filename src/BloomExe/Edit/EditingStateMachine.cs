using System;
using System.Diagnostics;
using L10NSharp;
using SIL.Code;
using SIL.Reporting;

// The states that EditingModel can be in
// Diagram: https://www.tldraw.com/r/WDLCDLfNbcDZW1kSXZVli?v=-441,-130,2813,1522&p=page
public enum State
{
    NoPage,
    Navigating,
    Editing,
    SavePending,

    // The page has been saved; in the process, we stripped various UI elements from it,
    // so it's not in a valid state for editing. We hope to fix this one day (BL-13502).
    // In the meantime, to make sure we don't forget to load up some page in a valid state,
    // the action that always goes along with a switch to this state returns the ID of a page we
    // should navigate to next.
    SavedAndStripped,
}

/// <summary>
/// What an attempt at an in-place save actually did. The point of the distinction is the third
/// case: a caller that has a fallback (SaveThen) must only use it when nothing happened, because
/// its doBeforeSaveToDisk is usually not something you can afford to do twice -- running it again
/// would duplicate or delete a second page.
/// </summary>
public enum InPlaceSaveOutcome
{
    // We were not in a state to save, so nothing was written and doBeforeSaveToDisk did NOT run.
    // A normal outcome, not an error: the user may have started changing pages. The caller is free
    // to fall back to SaveThen.
    Declined,

    // Saved, and (for the ...ThenNavigating form) on the way to the next page.
    Saved,

    // We started and something threw. The browser's content may already be in the book DOM and
    // doBeforeSaveToDisk may have run and changed the book. The failure has been reported to the
    // user; the caller must NOT fall back, or the action happens twice.
    Failed,
}

/// <summary>
/// A state machine to help us reason about the possible states of the editing model,
/// manage the valid transitions between them, and ensure that we don't attempt invalid ones.
/// </summary>
public class EditingStateMachine
{
    private Func<string> _doBeforeSaveToDisk; // returns pageId
    private Action _failureAction;
    private Action _doAfterSaveToDisk;
    private State _currentState;
    private string _pageId;
    private string _pageIdWeFailedToSave;
    private Action<string> _navigate; // arg is (pageId)

    private Action<string> _requestPageSave; // arg is (pageId)

    private Action<string, string> _updateBookWithPageContents; // args are (pageId, pageContentData)
    private Action _saveBook;
    private bool _saveActionHandlesSaveBook;

    // When set, the in-flight save (we are in SavePending, waiting for the browser to return the
    // page content) will be discarded on completion rather than merged into the DOM and written to
    // disk. See DiscardInFlightSave.
    private bool _discardInFlightSave;

    // Set only while ToSavedInPlaceThenNavigating is running its doBeforeSaveToDisk. In that window
    // the browser's content is already in the book DOM, so ToNavigating's "cannot navigate while
    // editing" guard does not apply -- there are no unsaved changes left to lose. Some actions do
    // navigate: relocating a page raises RelocatePageEvent, and EditingModel.OnRelocatePage
    // refreshes the display of the page whose HTML (side, page number) just changed. Under the old
    // SaveThen flow that was legal because the action ran while the machine sat in SavedAndStripped.
    private bool _runningSaveInPlaceAction;
    private Action _hidePage;

    private Action<bool> _enableStateTransitions; // arg is (enabled)

    /// <summary>
    /// Set up a state machine. It must be passed six actions:
    /// </summary>
    /// <param name="navigate">Called to start navigation to another (or the same) page. String is page ID.</param>
    /// <param name="requestPageSave">Called to initiate getting the page contents. String is page ID.</param>
    /// <param name="updateBookWithPageContents">Called with page ID and pageContentData to update the main DOM with current page content</param>
    /// <param name="saveBook">Called to save the current state of the DOM to disk.</param>
    /// <param name="hidePage">Called to make the transition to NoPage (when edit tab is hidden).</param>
    /// <param name="enableStateTransitions">Called to enable or disabled UI actions that would result in new state transitions.
    /// These are not allowed when we are in state SavePending or SavedAndStripped.</param>
    public EditingStateMachine(
        Action<string> navigate,
        Action<string> requestPageSave,
        Action<string, string> updateBookWithPageContents,
        Action saveBook,
        Action hidePage,
        Action<bool> enableStateTransitions
    )
    {
        _currentState = State.NoPage;
        _navigate = navigate;
        _requestPageSave = requestPageSave;
        _updateBookWithPageContents = updateBookWithPageContents;
        _saveBook = saveBook;
        _hidePage = hidePage;
        _enableStateTransitions = enableStateTransitions;
    }

    private void UpdateUI()
    {
        _enableStateTransitions(
            _currentState != State.SavePending && _currentState != State.SavedAndStripped
        );
    }

    /// <summary>
    /// Go to the state where we have no page loaded (switching to another tab).
    /// </summary>
    public bool ToNoPage()
    {
        try
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
                    LogError("empty page");
                    throw new InvalidOperationException("Cannot empty page while editing.");
                case State.SavePending:
                    // Review
                    LogError("empty page");
                    throw new InvalidOperationException("Cannot empty page while saving");
                case State.SavedAndStripped:
                    LogTransition("empty page", null);
                    _hidePage();
                    _currentState = State.NoPage;
                    return true;
                default:
                    throw new InvalidOperationException(
                        "Unknown state In emptyPage(): " + _currentState.ToString()
                    );
            }
        }
        finally
        {
            UpdateUI();
        }
    }

    /// <summary>
    /// True if we are in the process of navigating to a new page.
    /// </summary>
    public bool Navigating => _currentState == State.Navigating;

    /// <summary>
    /// True if we have initiated saving a page, but not yet received the html and user styles
    /// from the browser.
    /// </summary>
    public bool SavePending => _currentState == State.SavePending;

    /// <summary>
    /// Called to initiate navigation to a new page (or the same one again).
    /// Should not be called when there are unsaved (or incompletely saved) changes.
    /// </summary>
    public bool ToNavigating(string pageId)
    {
        try
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
                case State.SavePending:
                    LogIgnore("navigate");
                    return false;
                case State.SavedAndStripped:
                    StartNavigating(pageId);
                    return true;
                default:
                    throw new InvalidOperationException(
                        "Unknown state in ToNavigating(): " + _currentState.ToString()
                    );
            }
        }
        finally
        {
            UpdateUI();
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
        try
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
        finally
        {
            UpdateUI();
        }
    }

    private void DoPostSaveAction(
        string pageContentData,
        Func<string> doBeforeSaveToDisk,
        Action failureAction = null,
        Action doAfterSaveToDisk = null
    )
    {
        // If an external process overwrote the book on disk while this save was in flight, we are
        // intentionally discarding the gathered page content (see DiscardInFlightSave): don't merge
        // it into the DOM and don't write it to disk below, or we'd clobber what that process wrote.
        var discard = _discardInFlightSave;
        _discardInFlightSave = false;
        try
        {
            if (pageContentData != null && !discard)
            {
                if (pageContentData.StartsWith("ERROR:"))
                    throw new ApplicationException(pageContentData); // This is caught immediately below. We want that error handling for this case.
                _updateBookWithPageContents(_pageId, pageContentData);
                _pageIdWeFailedToSave = null;
            }
            else
            {
                // We're in the no page state (or discarding), and there's nothing to save.
            }
        }
        catch (Exception e)
        {
            // This prevents us from reporting the same error over and over again, which would also prevent the user from doing anything, including closing Bloom.
            if (_pageId != _pageIdWeFailedToSave)
            {
                _pageIdWeFailedToSave = _pageId;

                var msg = LocalizationManager.GetString(
                    "Errors.CouldNotSavePage",
                    "Bloom had trouble saving a page. Please report the problem to us. Then quit Bloom, run it again, and check to see if the page you just edited is missing anything. Sorry!"
                );
                ErrorReport.NotifyUserOfProblem(e, msg);

                failureAction?.Invoke();

                // We must not get stuck in the SavedAndStripped state, so we'll navigate to the page
                // we were on before the save.
                ToNavigating(_pageId);
                return;
            }
        }

        string pageId = _pageId;
        try
        {
            pageId = doBeforeSaveToDisk();
        }
        catch (Exception)
        {
            // We must not get stuck in the SavedAndStripped state, so we'll navigate to the page
            // we were on before the save.
            ToNavigating(pageId);
            throw;
        }

        try
        {
            if (_saveActionHandlesSaveBook)
            {
                _saveActionHandlesSaveBook = false;
            }
            else if (!discard)
            {
                _saveBook();
                doAfterSaveToDisk?.Invoke();
            }
        }
        // I'm not sure what should happen if we get an exception in _saveBook,
        // but we definitely don't want to get stuck in the SavedAndStripped state,
        // so for now we'll do whatever we were planning to do if it succeeded.
        finally
        {
            if (pageId != null)
                ToNavigating(pageId);
            else
                ToNoPage();
        }
    }

    /// <summary>
    /// Start saving the current page. When we get the page content and update the main HTML DOM with it,
    /// doBeforeSaveToDisk will be called. Then, unless saveActionHandlesSaveBook is passed as true,
    /// we will call saveBook, saving the changes to disk. If doAfterSaveToDisk is provided, it is called
    /// after the disk save. Finally, we navigate to the page whose ID is returned by doBeforeSaveToDisk.
    /// (This is convenient, and also ensures that we don't leave a page in the stripped state.)
    /// </summary>
    public bool ToSavePending(
        Func<string> doBeforeSaveToDisk,
        bool saveActionHandlesSaveBook = false,
        Action failureAction = null,
        Action doAfterSaveToDisk = null
    )
    {
        try
        {
            switch (_currentState)
            {
                case State.NoPage:
                    _saveActionHandlesSaveBook = saveActionHandlesSaveBook;
                    DoPostSaveAction(null, doBeforeSaveToDisk, failureAction, doAfterSaveToDisk);
                    return true;
                case State.Editing:
                    _saveActionHandlesSaveBook = saveActionHandlesSaveBook;
                    _doBeforeSaveToDisk = doBeforeSaveToDisk;
                    _failureAction = failureAction;
                    _doAfterSaveToDisk = doAfterSaveToDisk;
                    LogTransition("savePending", null);
                    _currentState = State.SavePending;
                    _requestPageSave(_pageId);
                    return true;

                case State.Navigating:
                case State.SavePending:
                case State.SavedAndStripped:
                    LogIgnore("save");
                    return false;
                default:
                    throw new InvalidOperationException(
                        "Unknown state In ToSavePending(): " + _currentState.ToString()
                    );
            }
        }
        finally
        {
            UpdateUI();
        }
    }

    /// <summary>
    /// If a save is in flight (we are in SavePending, having asked the browser for the current page's
    /// content but not yet received it), arrange for that save's completion to throw the content away
    /// rather than merging it into the book DOM or writing it to disk. Used when an external process
    /// has overwritten the book on disk and we are intentionally discarding the user's unsaved edits
    /// (see EditingModel.ReloadCurrentBookDiscardingEdits). Without this, the in-flight save would
    /// finish after we reload and clobber the external process's content on disk.
    /// Returns true if a save was actually in flight (so the discard will take effect).
    /// </summary>
    public bool DiscardInFlightSave()
    {
        if (_currentState != State.SavePending)
            return false;
        _discardInFlightSave = true;
        return true;
    }

    /// <summary>
    /// Save the current page from content the browser gathered on its own initiative, and stay in
    /// Editing.
    ///
    /// This is the transition that exists because the browser can now produce the page content
    /// WITHOUT wrecking the live page (it cleans a clone: see getPageContentForSave() in
    /// bloomEditing.ts). So, unlike the ToSavePending/ToSavedAndStripped pair, there is no stripped
    /// page to recover from, nothing to wait for, and no navigation to do afterwards: the user is
    /// still editing the same page when we return.
    ///
    /// Only legal while Editing. In every other state either there is nothing to save (NoPage), or
    /// a save/navigation is already under way and this one would race with it; we return false so
    /// the caller can decide what to do about that.
    /// </summary>
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
                case State.SavePending:
                case State.SavedAndStripped:
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
            // Unlike the ToSavedAndStripped path we don't have to navigate to get out of an
            // invalid state: we never left Editing, and the browser still has the intact page. So
            // all we owe the user is the report, and the caller a 'false'.
            // As there, we only report once per page, so that a page that fails every time doesn't
            // lock the user out of Bloom.
            if (_pageId != _pageIdWeFailedToSave)
            {
                _pageIdWeFailedToSave = _pageId;
                reportFailure(e);
            }
            return false;
        }
        finally
        {
            UpdateUI();
        }
    }

    /// <summary>
    /// Save the current page from content the browser sent WITH its request, optionally change the
    /// book in some way, then go to whichever page doBeforeSaveToDisk names. This is the
    /// straight-line form of the whole ToSavePending/ToSavedAndStripped sequence, and it is the
    /// reason SavePending can eventually go away: when the browser hands us the content up front
    /// there is nothing to wait for, so this is Editing -> Navigating in one step rather than
    /// Editing -> SavePending (ask the browser, wait) -> SavedAndStripped -> Navigating.
    ///
    /// doBeforeSaveToDisk plays exactly the role it plays in ToSavePending: it runs after the
    /// browser's content has been merged into the book DOM and before the book is written to disk
    /// (so a page it duplicates or deletes already reflects the user's latest edits), and it
    /// returns the id of the page to show afterwards. For a caller that only wants to change pages
    /// it is simply () => theNewPageId. It is allowed to navigate (see _runningSaveInPlaceAction);
    /// if it does, the navigation we do afterwards to its returned page simply supersedes it, or is
    /// ignored if it is to the same page.
    ///
    /// If it fails we report it and do NOT navigate: doing so would throw away the edits we failed
    /// to save, and unlike the ToSavedAndStripped path we are not in a broken state we have to
    /// escape, since the browser still has the page intact and editable. Note the difference
    /// between the two failure-ish outcomes -- see InPlaceSaveOutcome, and be careful to preserve
    /// it: Declined means the action never ran and the caller may fall back to SaveThen, whereas
    /// Failed means it may have run already and the caller must not run it again.
    /// </summary>
    public InPlaceSaveOutcome ToSavedInPlaceThenNavigating(
        string pageContentData,
        Func<string> doBeforeSaveToDisk,
        Action<Exception> reportFailure
    )
    {
        try
        {
            switch (_currentState)
            {
                case State.Editing:
                    LogTransition("saved in place, then navigating", _pageId);
                    if (pageContentData.StartsWith("ERROR:"))
                        throw new ApplicationException(pageContentData);
                    _updateBookWithPageContents(_pageId, pageContentData);
                    _pageIdWeFailedToSave = null;
                    RunActionThenSaveAndNavigate(doBeforeSaveToDisk);
                    return InPlaceSaveOutcome.Saved;
                case State.NoPage:
                    // Nothing to save, but the action and going to a page are still meaningful.
                    // (ToSavePending treats NoPage the same way.)
                    StartNavigating(doBeforeSaveToDisk());
                    return InPlaceSaveOutcome.Saved;
                case State.Navigating:
                case State.SavePending:
                case State.SavedAndStripped:
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
            if (_pageId != _pageIdWeFailedToSave)
            {
                _pageIdWeFailedToSave = _pageId;
                reportFailure(e);
            }
            return InPlaceSaveOutcome.Failed;
        }
        finally
        {
            UpdateUI();
        }
    }

    /// <summary>
    /// The middle of ToSavedInPlaceThenNavigating, from the point where the browser's content is
    /// safely in the book DOM: run the caller's action, write the book, and go to the page the
    /// action named. Separated out only so that _runningSaveInPlaceAction is obviously scoped to
    /// the action, and obviously cleared even if it throws.
    /// </summary>
    private void RunActionThenSaveAndNavigate(Func<string> doBeforeSaveToDisk)
    {
        _runningSaveInPlaceAction = true;
        try
        {
            var pageIdToGoTo = doBeforeSaveToDisk();
            _saveBook();
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

    /// <summary>
    /// Source: API call providing content of current page will request this after saving and before executing pending action
    /// (e.g. changing pages)
    /// </summary>
    public bool ToSavedAndStripped(string pageContentData)
    {
        try
        {
            switch (_currentState)
            {
                case State.SavePending:
                    Guard.AgainstNull(_doBeforeSaveToDisk, "doBeforeSaveToDisk");
                    LogTransition("saved and stripped", null);
                    _currentState = State.SavedAndStripped;
                    DoPostSaveAction(
                        pageContentData,
                        _doBeforeSaveToDisk,
                        _failureAction,
                        _doAfterSaveToDisk
                    );
                    _doBeforeSaveToDisk = null;
                    _failureAction = null;
                    _doAfterSaveToDisk = null;
                    return true;
                case State.NoPage:
                case State.Navigating:
                case State.Editing:
                case State.SavedAndStripped:
                    LogError("ToSavedAndStripped");
                    return false;
                default:
                    throw new InvalidOperationException(
                        "Unknown state In ToSavedAndStripped(): " + _currentState.ToString()
                    );
            }
        }
        finally
        {
            UpdateUI();
        }
    }

    /// <summary>
    /// Various (and growing) list of Javascript methods that gather the html to save and call Api:______(html-to-save, post-save-action)
    /// Untested since we don't have any such methods yet.
    /// </summary>
    public bool ToSavedAndStripped(Func<string> postSaveAction, string pageContentOrNull = null)
    {
        try
        {
            switch (_currentState)
            {
                case State.Editing:
                    Guard.AssertThat(
                        _doBeforeSaveToDisk == null,
                        "stored postSaveAction should be null, we're going to use the parameter instead."
                    );
                    Guard.AgainstNull(postSaveAction, "postSaveAction");
                    LogTransition("saved and stripped", null);
                    _currentState = State.SavedAndStripped;
                    DoPostSaveAction(pageContentOrNull, postSaveAction);
                    return true;
                case State.NoPage:
                case State.Navigating:
                case State.SavePending:
                case State.SavedAndStripped:
                    LogError("ToSavedAndStripped");
                    return false;
                default:
                    throw new InvalidOperationException(
                        "Unknown state In ToSavedAndStripped(): " + _currentState.ToString()
                    );
            }
        }
        finally
        {
            UpdateUI();
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
