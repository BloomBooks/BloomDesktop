import * as React from "react";
import { useState } from "react";
import ReactDOM = require("react-dom");
import { BloomApi } from "../utils/bloomApi";
import BloomButton from "../react_components/bloomButton";
import { Div } from "../react_components/l10nComponents";
import "./TeamCollectionPanel.less";

// The panel that appears at the bottom of the preview in the collection tab in a Team Collection.
// Todo: UI mockups go far beyond this simple beginning. Also, JohnH wants this component to wrap
// an iframe that contains the preview, rather than just inserting itself below it.

export const TeamCollectionPanel: React.FunctionComponent = props => {
    const [state, setState] = useState<
        // May also need lockedByMeElsewhere
        "initializing" | "lockedByMe" | "locked" | "unlocked"
    >("initializing");
    const [lockedBy, setLockedBy] = useState("");
    const [lockedWhen, setLockedWhen] = useState("");
    React.useEffect(() => {
        BloomApi.get("teamCollection/currentBookStatus", data => {
            const bookStatus = data.data;
            if (bookStatus.who) {
                // locked by someone
                if (
                    bookStatus.who === bookStatus.currentUser &&
                    bookStatus.where === bookStatus.currentMachine
                ) {
                    setState("lockedByMe");
                } else {
                    setState("locked");
                    // Who it is locked by. If that is the current user, but it's not locked here, clarify by
                    // appending the machine name where this user has it locked.
                    setLockedBy(
                        bookStatus.who +
                            (bookStatus.who === bookStatus.currentUser
                                ? " (" + bookStatus.where + ")"
                                : "")
                    );
                    setLockedWhen(bookStatus.when);
                }
            } else {
                setState("unlocked");
            }
        });
    }, []);
    switch (state) {
        default:
            return <div />; // just while initializing
        case "unlocked":
            return (
                <div>
                    <Div
                        l10nKey="TeamCollection.Available"
                        className="teamCollection-heading"
                    >
                        This book is available for editing
                    </Div>
                    <BloomButton
                        l10nKey="TeamCollection.Checkout"
                        //enabledImageFile="/bloom/bookEdit/pageThumbnailList/pageControls/addPage.png"
                        hasText={true}
                        enabled={true}
                        onClick={() => {
                            BloomApi.post(
                                "teamCollection/attemptLockOfCurrentBook",
                                response => {
                                    if (response.data) {
                                        setState("lockedByMe");
                                    } else {
                                        // Todo: fetch teamCollection/currentBookStatus, show who does have it.
                                        alert("Check out failed");
                                    }
                                }
                            );
                        }}
                        className="checkout-button teamCollection-button"
                    >
                        Check out book
                    </BloomButton>
                </div>
            );
        case "lockedByMe":
            return (
                <div>
                    <Div
                        l10nKey="TeamCollection.CheckedOutToYou"
                        className="teamCollection-heading"
                    >
                        This book is checked out to you
                    </Div>
                    <BloomButton
                        l10nKey="TeamCollection.CheckIn"
                        clickApiEndpoint="teamCollection/checkout"
                        //enabledImageFile="/bloom/bookEdit/pageThumbnailList/pageControls/addPage.png"
                        hasText={true}
                        enabled={true}
                        onClick={() => {
                            BloomApi.post(
                                "teamCollection/checkInCurrentBook",
                                response => {
                                    setState("unlocked");
                                }
                            );
                        }}
                        className="checkout-button teamCollection-button"
                    >
                        Check in book
                    </BloomButton>
                </div>
            );
        case "locked":
            return (
                <div>
                    <Div
                        className="teamCollection-heading"
                        l10nKey="TeamCollection.CheckedOutToSomeone"
                        l10nParam0={lockedBy}
                    >
                        This book is checked out to %0
                    </Div>
                    <Div
                        l10nKey="TeamCollection.CheckedOutOn"
                        l10nParam0={lockedBy}
                        l10nParam1={lockedWhen}
                    >
                        %0 checked this book out on %1
                    </Div>
                </div>
            );
    }
};

// This function gets the teamCollection panel going, iff the collection is shared.
// It wraps another div around the whole current contents of the window,
// then adds an instance of TeamCollectionPanel below it.
export function setupTeamCollection() {
    BloomApi.getBoolean(
        "teamCollection/isTeamCollectionEnabled",
        teamCollection => {
            if (!teamCollection) {
                return;
            }
            let teamCollectionRoot = document.getElementById("teamCollection");
            if (!teamCollectionRoot) {
                // Make a wrapper and put the whole original document contents into it.
                // Styles will make the preview take all the viewport except 200px at the bottom.
                var preview = document.createElement("div");
                preview.setAttribute("id", "preview-wrapper");
                Array.from(document.body.childNodes).forEach(e =>
                    preview.appendChild(e)
                );
                document.body.appendChild(preview);

                // Now make the TeamCollectionPanel and let React render it.
                teamCollectionRoot = document.createElement("div");
                teamCollectionRoot.setAttribute("id", "teamCollection");
                document.body.appendChild(teamCollectionRoot);
            }
            ReactDOM.render(<TeamCollectionPanel />, teamCollectionRoot);
        }
    );
}
