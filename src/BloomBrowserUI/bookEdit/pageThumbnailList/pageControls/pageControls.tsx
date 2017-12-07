import axios from "axios";
import * as React from "react";
import * as ReactDOM from "react-dom";
import BloomButton from "../../../react_components/bloomButton";
import WebSocketManager from "../../../utils/WebSocketManager";

const kWebSocketLifetime = "pageThumbnailList-pageControls";

interface IPageControlsState {
    canAddState: boolean;
    canDuplicateState: boolean;
    canDeleteState: boolean;
    lockState: string; // BookLocked, BookUnlocked, or OriginalBookMode
}

// This is a small area of controls at the bottom of the webThumbnailList that gives the user controls
// for adding/duplicating/deleting pages in a book and temporarily unlocking/locking the book.
class PageControls extends React.Component<{}, IPageControlsState> {
    constructor(props) {
        super(props);

        // set a default state
        this.state = { canAddState: true, canDeleteState: false, canDuplicateState: false, lockState: "OriginalBookMode" };

        // (Comment copied from androidPublishUI.tsx)
        // For some reason setting the callback to "this.updateStateForEvent" calls updateStateForEvent()
        // with "this" set to the button, not this overall control.
        // See https://medium.com/@rjun07a/binding-callbacks-in-react-components-9133c0b396c6
        this.updateStateForEvent = this.updateStateForEvent.bind(this);

        // Listen for changes to state from C#-land
        WebSocketManager.addListener(kWebSocketLifetime, event => {
            var e = JSON.parse(event.data);
            if (e.id === "edit/pageControls/state") {
                this.updateStateForEvent(e.payload);
            }
        });
    }

    public componentDidMount() {
        window.addEventListener("beforeunload", this.componentCleanup);
        // Get the initial state from C#-land, now that we're ready for it.
        axios.get("/bloom/api/edit/pageControls/requestState").then(result => {
            var jsonObj = result.data; // Axios apparently recognizes the JSON and parses it automatically.
            // something like: {"CanAddPages":true,"CanDeletePage":true,"CanDuplicatePage":true,"BookLockedState":"OriginalBookMode"}
            this.setPageControlState(jsonObj);
        });
    }

    // Apparently, we have to rely on the window event when closing or refreshing the page.
    // componentWillUnmount will not get called in those cases.
    public componentWillUnmount() {
        window.removeEventListener("beforeunload", this.componentCleanup);
        this.componentCleanup();
    }

    componentCleanup() {
        axios.post("/bloom/api/edit/pageControls/cleanup").then(result => {
            WebSocketManager.closeSocket(kWebSocketLifetime);
        });
    }

    updateStateForEvent(s: string): void {
        var state = JSON.parse(s);
        this.setPageControlState(state);
        //console.log("this.state is " + JSON.stringify(this.state));
    }

    setPageControlState(data: any): void {
        this.setState({
            canAddState: data.CanAddPages,
            canDeleteState: data.CanDeletePage,
            canDuplicateState: data.CanDuplicatePage,
            lockState: data.BookLockedState
        });
        //console.log("this.state is " + JSON.stringify(this.state));
    }

    render() {
        return (
            <div>
                <div>
                    <BloomButton
                        l10nKey="EditTab.AddPageDialog.AddPageButton"
                        l10nComment=
                        "This is for the button that LAUNCHES the dialog, not the \'Add this page\' button that is IN the dialog."
                        enabled={this.state.canAddState}
                        clickEndpoint="edit/pageControls/addPage"
                        enabledImageFile="/bloom/bookEdit/pageThumbnailList/pageControls/addPage.png"
                        disabledImageFile="/bloom/bookEdit/pageThumbnailList/pageControls/addPageDisabled.png"
                        hasText={true}>
                        Add Page
                    </BloomButton>
                </div>
                <div id="row2">
                    <BloomButton
                        enabled={this.state.canDuplicateState}
                        l10nKey="EditTab.DuplicatePageButton"
                        l10nComment="Button that tells Bloom to duplicate the currently selected page."
                        clickEndpoint="edit/pageControls/duplicatePage"
                        enabledImageFile="/bloom/bookEdit/pageThumbnailList/pageControls/duplicatePage.svg"
                        disabledImageFile="/bloom/bookEdit/pageThumbnailList/pageControls/duplicatePageDisabled.svg"
                        hasText={false}
                        l10nTipEnglishEnabled="Insert a new page which is a duplicate of this one"
                        l10nTipEnglishDisabled="This page cannot be duplicated">
                    </BloomButton>
                    <BloomButton
                        l10nKey="EditTab.DeletePageButton"
                        l10nComment="Button that tells Bloom to delete the currently selected page."
                        enabled={this.state.canDeleteState}
                        clickEndpoint="edit/pageControls/deletePage"
                        enabledImageFile="/bloom/bookEdit/pageThumbnailList/pageControls/deletePage.svg"
                        disabledImageFile="/bloom/bookEdit/pageThumbnailList/pageControls/deletePageDisabled.svg"
                        hasText={false}
                        l10nTipEnglishEnabled="Remove this page from the book"
                        l10nTipEnglishDisabled="This page cannot be removed">
                    </BloomButton>
                    {this.state.lockState !== "OriginalBookMode" &&
                        <span>
                            {this.state.lockState === "BookLocked" &&
                                <BloomButton
                                    l10nKey="EditTab.UnlockBook"
                                    l10nComment=
                                    "Button that tells Bloom to temporarily unlock a shell book for editing other than translation."
                                    enabled={true}
                                    clickEndpoint="edit/pageControls/unlockBook"
                                    enabledImageFile="/bloom/bookEdit/pageThumbnailList/pageControls/lockedPage.svg"
                                    hasText={false}
                                    l10nTipEnglishEnabled=
                                    "This book is in translate-only mode. If you want to make other changes, click this to temporarily unlock the book.">
                                </BloomButton>
                            }
                            {this.state.lockState === "BookUnlocked" &&
                                <BloomButton
                                    l10nKey="EditTab.LockBook"
                                    l10nComment=
                                    "Button that tells Bloom to re-lock a shell book so it can't be modified (other than translation)."
                                    enabled={true}
                                    clickEndpoint="edit/pageControls/lockBook"
                                    enabledImageFile="/bloom/bookEdit/pageThumbnailList/pageControls/unlockedPage.svg"
                                    hasText={false}
                                    l10nTipEnglishEnabled="This book is temporarily unlocked.">
                                </BloomButton>
                            }
                        </span>
                    }
                </div>
            </div>
        );
    }
}

ReactDOM.render(
    <PageControls />,
    document.getElementById("PageControls")
);
