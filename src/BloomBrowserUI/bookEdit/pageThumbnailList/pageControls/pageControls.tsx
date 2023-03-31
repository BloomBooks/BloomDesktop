import { get, post } from "../../../utils/bloomApi";
import * as React from "react";
import * as ReactDOM from "react-dom";
import BloomButton from "../../../react_components/bloomButton";
import WebSocketManager from "../../../utils/WebSocketManager";
import "./pageControls.less";
import "errorHandler";

// This is one of the root files for our webpack build, the root from which
// pageControlsBundle.js is built. Currently, contrary to our usual practice,
// this bundle is one of two loaded by pageThumbnailList.pug. It is NOT the last
// bundle loaded. As a result, anything exported in this file will NOT be
// accessible through editTabBundle, because this bundle's editTabBundle is
// replaced by the pageControlsBundle one. We do need something from that
// editTabBundle, so if we one day need something exported from this, we will
// have to either combine the two into a single bundle, or use a technique
// hinted at in webpack.config.js to give each bundle a different root name
// for its exports.

const kPageControlsContext = "pageThumbnailList-pageControls";

interface IPageControlsState {
    canAddState: boolean;
    canDuplicateState: boolean;
    canDeleteState: boolean;
}

// This is a small area of controls at the bottom of the webThumbnailList that gives the user controls
// for adding/duplicating/deleting pages in a book and temporarily unlocking/locking the book.
class PageControls extends React.Component<{}, IPageControlsState> {
    // set a initial state
    public readonly state: IPageControlsState = {
        canAddState: true,
        canDeleteState: false,
        canDuplicateState: false
    };

    constructor(props) {
        super(props);

        // For some reason setting the callback to "this.updateStateForEvent" calls updateStateForEvent()
        // with "this" set to the button, not this overall control.
        // See https://medium.com/@rjun07a/binding-callbacks-in-react-components-9133c0b396c6
        this.updateStateForEvent = this.updateStateForEvent.bind(this);

        // Listen for changes to state from C#-land
        WebSocketManager.addListener(kPageControlsContext, e => {
            if (e.id === "edit/pageControls/state" && e.message) {
                this.updateStateForEvent(e.message);
            }
        });
    }

    public componentDidMount() {
        window.addEventListener("beforeunload", this.componentCleanup);
        // Get the initial state from C#-land, now that we're ready for it.
        get("edit/pageControls/requestState", result => {
            const jsonObj = result.data; // Axios apparently recognizes the JSON and parses it automatically.
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

    public componentCleanup() {
        post("edit/pageControls/cleanup", result => {
            WebSocketManager.closeSocket(kPageControlsContext);
        });
    }

    public updateStateForEvent(s: string): void {
        const state = JSON.parse(s);
        this.setPageControlState(state);
        //console.log("this.state is " + JSON.stringify(this.state));
    }

    public setPageControlState(data: any): void {
        this.setState({
            canAddState: data.CanAddPages,
            canDeleteState: data.CanDeletePage,
            canDuplicateState: data.CanDuplicatePage
        });
        //console.log("this.state is " + JSON.stringify(this.state));
    }

    public render() {
        return (
            <div id="pageControlsRoot">
                <div>
                    <BloomButton
                        transparent={true}
                        l10nKey="EditTab.AddPageDialog.AddPageButton"
                        l10nComment="This is for the button that LAUNCHES the dialog, not the \'Add this page\' button that is IN the dialog."
                        enabled={this.state.canAddState}
                        clickApiEndpoint="edit/pageControls/addPage"
                        mightNavigate={true}
                        enabledImageFile="/bloom/bookEdit/pageThumbnailList/pageControls/addPage.png"
                        disabledImageFile="/bloom/bookEdit/pageThumbnailList/pageControls/addPageDisabled.png"
                        hasText={true}
                    >
                        Add Page
                    </BloomButton>
                </div>
                <div id="row2">
                    <BloomButton
                        transparent={true}
                        enabled={this.state.canDuplicateState}
                        l10nKey="EditTab.DuplicatePageButton"
                        l10nComment="Button that tells Bloom to duplicate the currently selected page."
                        clickApiEndpoint="edit/pageControls/duplicatePage"
                        mightNavigate={true}
                        enabledImageFile="/bloom/bookEdit/pageThumbnailList/pageControls/duplicatePage.svg"
                        disabledImageFile="/bloom/bookEdit/pageThumbnailList/pageControls/duplicatePageDisabled.svg"
                        hasText={false}
                        l10nTipEnglishEnabled="Insert a new page which is a duplicate of this one"
                        l10nTipEnglishDisabled="This page cannot be duplicated"
                    />
                    <BloomButton
                        l10nKey="EditTab.DeletePageButton"
                        transparent={true}
                        l10nComment="Button that tells Bloom to delete the currently selected page."
                        enabled={this.state.canDeleteState}
                        clickApiEndpoint="edit/pageControls/deletePage"
                        mightNavigate={true}
                        enabledImageFile="/bloom/bookEdit/pageThumbnailList/pageControls/deletePage.svg"
                        disabledImageFile="/bloom/bookEdit/pageThumbnailList/pageControls/deletePageDisabled.svg"
                        hasText={false}
                        l10nTipEnglishEnabled="Remove this page from the book"
                        l10nTipEnglishDisabled="This page cannot be removed"
                    />
                </div>
            </div>
        );
    }
}

ReactDOM.render(<PageControls />, document.getElementById("PageControls"));
