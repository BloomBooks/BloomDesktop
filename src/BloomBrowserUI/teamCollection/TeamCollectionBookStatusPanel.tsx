import * as React from "react";
import theme from "../bloomMaterialUITheme";
import { ThemeProvider } from "@material-ui/styles";
import { useState } from "react";
import ReactDOM = require("react-dom");
import { BloomApi } from "../utils/bloomApi";
import { useL10n } from "../react_components/l10nHooks";
import "./TeamCollectionBookStatusPanel.less";
import { StatusPanelCommon, getLockedInfoChild } from "./statusPanelCommon";
import BloomButton from "../react_components/bloomButton";
import { BloomAvatar } from "../react_components/bloomAvatar";

// The panel that appears at the bottom of the preview in the collection tab in a Team Collection.
// Todo: JohnH wants this component to wrap an iframe that contains the preview,
// rather than just inserting itself below it.

export type LockState =
    | "initializing"
    | "unlocked"
    | "locked"
    | "lockedByMe"
    | "lockedByMeElsewhere";

export const TeamCollectionBookStatusPanel: React.FunctionComponent = props => {
    const [state, setState] = useState<LockState>("initializing");
    const [lockedBy, setLockedBy] = useState("");
    const [lockedByDisplay, setLockedByDisplay] = useState("");
    const [lockedWhen, setLockedWhen] = useState("");
    const [lockedMachine, setLockedMachine] = useState("");
    React.useEffect(() => {
        BloomApi.get("teamCollection/currentBookStatus", data => {
            const bookStatus = data.data;
            if (bookStatus.who) {
                // locked by someone
                setLockedBy(bookStatus.who);
                const lockedByFullName = `${bookStatus.whoFirstName} ${bookStatus.whoSurname}`.trim();
                setLockedByDisplay(lockedByFullName || lockedBy);
                if (
                    bookStatus.who === bookStatus.currentUser &&
                    bookStatus.where === bookStatus.currentMachine
                ) {
                    setState("lockedByMe");
                } else {
                    const isCurrentUser =
                        bookStatus.who === bookStatus.currentUser;
                    if (isCurrentUser) {
                        setState("lockedByMeElsewhere");
                    } else {
                        setState("locked");
                    }
                    setLockedWhen(bookStatus.when);
                    setLockedMachine(bookStatus.where);
                }
            } else {
                setState("unlocked");
            }
        });
    }, []);

    let avatar;
    if (state.startsWith("locked")) {
        avatar = (
            <BloomAvatar
                email={lockedBy}
                name={lockedByDisplay}
                borderColor={
                    state === "lockedByMe" && theme.palette.warning.main
                }
            />
        );
    }

    // Rules of hooks mean we need to useL10N() on ALL of the strings we might use for each lockState.
    // N.B. When placeholders are needed, we use %0 instead of {0}. Why? See BL-9490.
    const mainTitleUnlocked = useL10n(
        "This book is available for editing",
        "TeamCollection.Available",
        undefined,
        undefined,
        undefined,
        true
    );
    const subTitleUnlocked = useL10n(
        "When you check it out, no one on the team will be able to modify it or see your changes until you check it back in.",
        "TeamCollection.AvailableDescription",
        undefined,
        undefined,
        undefined,
        true
    );
    const mainTitleLockedByMe = useL10n(
        "This book is checked out to you",
        "TeamCollection.CheckedOutToYou",
        undefined,
        undefined,
        undefined,
        true
    );
    const subTitleLockedByMe = useL10n(
        "Are you done for now? Click this button to send your changes to your team.",
        "TeamCollection.CheckedOutToYouDescription",
        undefined,
        undefined,
        undefined,
        true
    );
    const mainTitleLocked = useL10n(
        "This book is checked out to %0",
        "TeamCollection.CheckedOutToSomeone",
        "The %0 is the name of the person who checked out the book (or possibly email).",
        lockedByDisplay,
        undefined,
        true
    );
    const subTitleLocked = useL10n(
        "You cannot edit the book until %0 checks it in.",
        "TeamCollection.CheckedOutToSomeoneDescription",
        "The %0 is the name of the person who checked out the book.",
        lockedByDisplay,
        undefined,
        true
    );
    const lockedInfo = useL10n(
        "%0 checked out this book on %1.",
        "TeamCollection.CheckedOutOn",
        "The %0 is a person's name, and the %1 is a date.",
        lockedByDisplay,
        lockedWhen,
        true
    );
    const mainTitleLockedElsewhere = useL10n(
        "This book is checked out to you, but on a different computer",
        "TeamCollection.CheckedOutToYouElsewhere",
        undefined,
        undefined,
        undefined,
        true
    );
    const subTitleLockedElsewhere = useL10n(
        "You cannot edit the book on this computer, until you check it in on %0.",
        "TeamCollection.CheckedOutToYouElsewhereDescription",
        "The %0 is the name of the computer where the book is checked out.",
        lockedMachine,
        undefined,
        true
    );
    const lockedElsewhereInfo = useL10n(
        "You checked out this book on %0.",
        "TeamCollection.YouCheckedOutOn",
        "The %0 is a date.",
        lockedWhen,
        undefined,
        true
    );

    const panelContents = (state: LockState): JSX.Element => {
        switch (state) {
            default:
                return <div />; // just while initializing
            case "unlocked":
                const checkoutHandler = () => {
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
                };

                return (
                    <StatusPanelCommon
                        lockState={state}
                        title={mainTitleUnlocked}
                        subTitle={subTitleUnlocked}
                        icon={<img src={"Available.svg"} alt="available" />}
                        button={getBloomButton(
                            "Check out book",
                            "TeamCollection.Checkout",
                            "checkout-button",
                            "Check Out.svg",
                            checkoutHandler
                        )}
                    />
                );
            case "lockedByMe":
                const checkinHandler = () => {
                    BloomApi.post("teamCollection/checkInCurrentBook", () => {
                        setState("unlocked");
                    });
                };

                return (
                    <StatusPanelCommon
                        lockState={state}
                        title={mainTitleLockedByMe}
                        subTitle={subTitleLockedByMe}
                        icon={avatar}
                        //menu={} // eventually the "About my Avatar..." and "Forget Changes" menu gets passed in here.
                        button={getBloomButton(
                            "Check in book",
                            "TeamCollection.CheckIn",
                            "checkin-button",
                            "Check In.svg",
                            checkinHandler
                        )}
                    />
                );
            case "lockedByMeElsewhere":
                return (
                    <StatusPanelCommon
                        lockState={state}
                        title={mainTitleLockedElsewhere}
                        subTitle={subTitleLockedElsewhere}
                        icon={avatar}
                        children={getLockedInfoChild(lockedElsewhereInfo)}
                    />
                );
            case "locked":
                return (
                    <StatusPanelCommon
                        lockState={state}
                        title={mainTitleLocked}
                        subTitle={subTitleLocked}
                        icon={avatar}
                        children={getLockedInfoChild(lockedInfo)}
                    />
                );
        }
    };

    return <ThemeProvider theme={theme}>{panelContents(state)}</ThemeProvider>;
};

export const getBloomButton = (
    english: string,
    l10nKey: string,
    buttonClass: string,
    icon?: string,
    clickHandler?: () => void
) => (
    <BloomButton
        iconBeforeText={icon ? <img src={icon} /> : <div />}
        l10nKey={l10nKey}
        hasText={true}
        enabled={true}
        className={buttonClass}
        onClick={clickHandler}
        temporarilyDisableI18nWarning={true}
    >
        {english}
    </BloomButton>
);

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
            ReactDOM.render(
                <TeamCollectionBookStatusPanel />,
                teamCollectionRoot
            );
        }
    );
}
