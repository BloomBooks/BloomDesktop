using System;
using System.Diagnostics;
using SIL.Code;

// Diagram: https://www.tldraw.com/r/WDLCDLfNbcDZW1kSXZVli?v=-441,-130,2813,1522&p=page
public enum State
{
    NoPage,
    Navigating,
    Editing,
    SavePending,
    SavedAndStripped
}

public class EditingStateMachine
{
    private Action<string /* pageId*/
    > _postSaveAction;
    private State _currentState;
    private string _pageId;
    private Action<string> _navigate;
    private Action<string /* pageId*/
    > _requestPageSave;

    public EditingStateMachine(Action<string> navigate, Action<string> requestPageSave)
    {
        _currentState = State.NoPage;
        this._navigate = navigate;
        this._requestPageSave = requestPageSave;
    }

    public bool ToNoPage()
    {
        switch (_currentState)
        {
            case State.NoPage:
                LogIgnore("empty page");
                return true;
            case State.Navigating:
                LogShortcut("empty page");
                // TODO: Navigate to about.blank or whatever it is we do
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
                return true;
            default:
                throw new InvalidOperationException(
                    "Unknown state In emptyPage(): " + _currentState.ToString()
                );
        }
    }

    public bool ToNavigating(string pageId)
    {
        switch (_currentState)
        {
            case State.NoPage:
                _currentState = State.Navigating;
                // todo: start navigating
                LogTransition("navigating", pageId);
                return true;
            case State.Navigating:
                if (this._pageId == pageId)
                {
                    LogIgnore("navigate");
                    return true; // we're already headed there
                }
                else
                {
                    LogTransition("navigating", pageId);
                    this._pageId = pageId;
                    this._navigate(pageId);
                    return true;
                }
            case State.Editing:
                LogError("navigate");
                throw new InvalidOperationException("Cannot navigate while editing");
            case State.SavePending:
                LogIgnore("navigate");
                return false;
            case State.SavedAndStripped:
                LogTransition("navigating", pageId);
                return true;
            default:
                throw new InvalidOperationException(
                    "Unknown state in ToNavigating(): " + _currentState.ToString()
                );
        }
    }

    // Called after we hear from the browser JS that the dom is finished loading
    public bool ToEditing(string pageId)
    {
        switch (_currentState)
        {
            case State.Navigating:
                if (this._pageId == pageId)
                {
                    _currentState = State.Editing;
                    LogTransition("editing", pageId);
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

    public bool ToSavePending(Action<string> postSaveAction)
    {
        switch (_currentState)
        {
            case State.NoPage:
                postSaveAction(
                    null /* no page id*/
                ); // review
                return true;
            case State.Editing:
                this._postSaveAction = postSaveAction;
                _currentState = State.SavePending;
                _requestPageSave(this._pageId);
                LogTransition("savePending", null);
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

    // Source: API call providing content of current page will request this after saving and before executing pending action
    // E.g. changing pages
    public bool ToSavedAndStripped(string pageContent)
    {
        switch (_currentState)
        {
            case State.SavePending:
                _currentState = State.SavedAndStripped;
                Guard.AgainstNull(this._postSaveAction, "postSaveAction");

                // TODO should this save or the caller (editing model)

                //                this._requestPageSave(this._pageId, pageContent);
                this._postSaveAction(this._pageId);
                this._postSaveAction = null;
                LogTransition("saved and stripped", null);
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

    // Various (and growing) list of Javascript methods that gather the html to save and call Api:______(html-to-save, post-save-action)
    // E.g. Delete would call this with null content
    //
    // TODO: we decided to make ChangePicture not use saving at all. 1) Post to api with the id of the image and current src 2) server runs JS like SetImage(id, newSrc, newImageMetadata)
    public bool ToSavedAndStripped(Action postSaveAction, string pageContentOrNull = null)
    {
        switch (_currentState)
        {
            case State.Editing:
                _currentState = State.SavedAndStripped;
                Guard.AssertThat(
                    this._postSaveAction == null,
                    "stored this.postSaveAction should be null, we're going to use the parameter instead."
                );
                Guard.AgainstNull(postSaveAction, "postSaveAction");
                // TODO: how does Delete() actually work
                //this._requestPageSave(
                //    this._pageId,
                //    pageContentOrNull /* if we're deleting the page, this will be null */
                //);
                this._postSaveAction(this._pageId);
                LogTransition("saved and stripped", null);
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

    private void Log(string message)
    {
        Console.WriteLine("[EditingStateMachine] " + message);
    }

    private void LogTransition(string nextState, string nextPageId)
    {
        Log($"{_currentState}({this._pageId}) --> {nextState}({nextPageId})");
    }

    private void LogError(string transitionRequest)
    {
        Log($"Error: Cannot {transitionRequest} while in {_currentState} state");
    }

    private void LogIgnore(string transitionRequest, string nextPageId = null)
    {
        Log(
            $"Ignoring {transitionRequest}({nextPageId}) request while in {_currentState}({this._pageId}) state"
        );
    }

    private void LogShortcut(string transitionRequest)
    {
        Log($"Shortcutting {transitionRequest} request while in {_currentState} state");
    }
}
