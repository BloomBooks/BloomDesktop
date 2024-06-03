using System;
using System.Diagnostics;
using SIL.Code;

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
    SavedAndStripped
}

/// <summary>
/// A state machine to help us reason about the possible states of the editing model,
/// manage the valid transitions between them, and ensure that we don't attempt invalid ones.
/// </summary>
public class EditingStateMachine
{
    private Func<string /* pageId*/
    > _postSaveAction;
    private State _currentState;
    private string _pageId;
    private Action<string> _navigate;

    // arg is pageId
    private Action<string> _requestPageSave;

    // pageId, htmlAndUserStyles
    private Action<string, string> _updateBookWithPageContents;
    private Action _saveBook;
    private bool _saveActionHandlesSaveBook;
    private Action _hidePage;

    private Action<bool> _enableStateTransitions;

    /// <summary>
    /// Set up a state machine. It must be passed six actions:
    /// </summary>
    /// <param name="navigate">Called to start navigation to another (or the same) page. String is page ID.</param>
    /// <param name="requestPageSave">Called to initiate getting the page contents. String is page ID.</param>
    /// <param name="updateBookWithPageContents">Called with page ID and htmlAndUserStyles data to update the main DOM with current page content</param>
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

    private void DoPostSaveAction(string htmlAndUserStyles, Func<string> postSaveAction)
    {
        if (htmlAndUserStyles != null)
            _updateBookWithPageContents(_pageId, htmlAndUserStyles);
        var pageId = postSaveAction();
        if (_saveActionHandlesSaveBook)
        {
            _saveActionHandlesSaveBook = false;
        }
        else
        {
            _saveBook();
        }

        if (pageId != null)
            ToNavigating(pageId);
        else
            ToNoPage();
    }

    /// <summary>
    /// Start saving the current page. When get the page content and update the main HTML DOM with it,
    /// the postSaveAction will be called. Then, unless saveActionHandlesSaveBook is passed as true,
    /// we will call saveBook, typically saving the changes to disk. Finally, we navigate to the page
    /// whose ID is returned by postSaveAction. (This is convenient, and also ensures that we don't leave
    /// a page in the stripped state.)
    /// </summary>
    public bool ToSavePending(Func<string> postSaveAction, bool saveActionHandlesSaveBook = false)
    {
        try
        {
            switch (_currentState)
            {
                case State.NoPage:
                    _saveActionHandlesSaveBook = saveActionHandlesSaveBook;
                    DoPostSaveAction(null, postSaveAction);
                    return true;
                case State.Editing:
                    _saveActionHandlesSaveBook = saveActionHandlesSaveBook;
                    _postSaveAction = postSaveAction;
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
    /// Source: API call providing content of current page will request this after saving and before executing pending action
    /// (e.g. changing pages)
    /// </summary>
    public bool ToSavedAndStripped(string htmlAndUserStyles)
    {
        try
        {
            switch (_currentState)
            {
                case State.SavePending:
                    Guard.AgainstNull(_postSaveAction, "postSaveAction");
                    LogTransition("saved and stripped", null);
                    _currentState = State.SavedAndStripped;
                    DoPostSaveAction(htmlAndUserStyles, _postSaveAction);
                    _postSaveAction = null;
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
                        _postSaveAction == null,
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
