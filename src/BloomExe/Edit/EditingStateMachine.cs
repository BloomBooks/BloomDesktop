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

    // Work that arrived while a save was in flight and could not be done then. It runs once that
    // save has completed and we are back in a state that allows transitions.
    // See DeferUntilSaveCompletes.
    private Action _workToDoAfterInFlightSave;

    // Work that arrived while we were navigating to a page and could not be done then. It runs
    // once that page has loaded. See DeferUntilPageIsLoaded.
    private Action _workToDoAfterNavigation;
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
    /// <param name="enableStateTransitions">Called to ask the UI to stop offering actions that would result in new state
    /// transitions, because they are not valid in SavePending or SavedAndStripped. It is only a request, and does not
    /// take effect soon enough to stop a click already on its way (see WorkspaceView.SetTabsEnabled and BL-16766), so
    /// every transition must still refuse or defer an invalid request when one arrives.</param>
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
                    // The navigation this work was waiting for is over, and no page loaded, so
                    // the work must not run: it belonged to a page we are no longer showing. This
                    // is the path a switch away from the Edit tab takes.
                    _workToDoAfterNavigation = null;
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
    /// Called after we hear from the browser JS that the dom is finished loading.
    /// Work that had to wait for this navigation does NOT run here; the caller takes it with
    /// TakeWorkDeferredUntilPageIsLoaded once it has done its own work on the loaded page.
    /// </summary>
    public bool ToEditing(string pageId)
    {
        return ToEditingFromNavigating(pageId);
    }

    /// <summary>
    /// Hand back the work that was deferred until a page loaded (see DeferUntilPageIsLoaded), and
    /// forget it, so the caller can run it. Returns null when there is none.
    ///
    /// The caller runs it rather than ToEditing, because the code that hears "the page has loaded"
    /// has its own work to do first: an action queued for the next page load, and the next step of
    /// the Update Book pass. A jump that ran before those would save and navigate away from the
    /// page they were about to act on. See EditingModel.HandlePageDomLoadedEvent.
    /// </summary>
    public Action TakeWorkDeferredUntilPageIsLoaded()
    {
        var deferredWork = _workToDoAfterNavigation;
        _workToDoAfterNavigation = null;
        return deferredWork;
    }

    private bool ToEditingFromNavigating(string pageId)
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
    /// For a caller that could not start a save (ToSavePending returned false) because one is
    /// already in flight, and whose work is not safe to do until that save has finished. If a save
    /// really is in flight, <paramref name="work"/> is remembered and run when the save completes,
    /// and this returns true — the caller must then do nothing else. Otherwise it returns false and
    /// the caller must get on with its own work.
    ///
    /// Leaving the Edit tab is the case this exists for (BL-16766): the user clicked another tab
    /// twice in quick succession, and the second click found the first click's save still waiting
    /// on the browser for the page content. Pressing on with the tab change then reached
    /// ToNoPage() while still in SavePending, which throws, and left the workspace half switched
    /// between the two tabs.
    ///
    /// Only one piece of deferred work is kept: a later request supersedes an earlier one, since it
    /// is the more recent thing the user asked for.
    /// </summary>
    public bool DeferUntilSaveCompletes(Action work)
    {
        // No save to wait for, or nothing to do: the caller must handle it itself.
        if (_currentState != State.SavePending || work == null)
            return false;
        _workToDoAfterInFlightSave = work;
        return true;
    }

    /// <summary>
    /// For a caller whose work needs a loaded page, and which found us navigating to one (which is
    /// why ToSavePending refused it). If we really are navigating, <paramref name="work"/> is
    /// remembered and run once that page has loaded, and this returns true — the caller must then
    /// do nothing else. Otherwise it returns false and the caller must handle its own request.
    ///
    /// A navigation that ends any other way than with a loaded page (a switch away from the Edit
    /// tab, which comes through ToNoPage) throws the work away: it belonged to a page that is no
    /// longer being shown.
    ///
    /// A jump to a page is the case this exists for: it arrives from an API call at any moment,
    /// including while the Edit tab is loading the page it displays on becoming visible. Bloom used
    /// to drop such a jump and tell the caller it had succeeded (see src/BloomE2E/AUTOMATION-DEBT.md).
    ///
    /// Only one piece of deferred work is kept: a later request supersedes an earlier one, since it
    /// is the more recent thing that was asked for.
    /// </summary>
    public bool DeferUntilPageIsLoaded(Action work)
    {
        // We are not navigating, or there is nothing to do: the caller must handle it itself.
        if (_currentState != State.Navigating || work == null)
            return false;
        _workToDoAfterNavigation = work;
        return true;
    }

    /// <summary>
    /// Source: API call providing content of current page will request this after saving and before executing pending action
    /// (e.g. changing pages)
    /// </summary>
    public bool ToSavedAndStripped(string pageContentData)
    {
        // This is the only way out of SavePending, so it is where anything that had to wait for the
        // in-flight save gets its turn (see DeferUntilSaveCompletes). It runs after the whole save,
        // including the post-save action, so that we are in a state that allows transitions again;
        // and in a finally, because a save that fails must not swallow a pending tab change.
        try
        {
            return ToSavedAndStrippedFromSavePending(pageContentData);
        }
        finally
        {
            var deferredWork = _workToDoAfterInFlightSave;
            _workToDoAfterInFlightSave = null;
            deferredWork?.Invoke();
        }
    }

    private bool ToSavedAndStrippedFromSavePending(string pageContentData)
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
