import * as React from "react";
import theme from "../bloomMaterialUITheme";
import { ThemeProvider } from "@material-ui/styles";
import { useState } from "react";
import { BloomApi } from "../utils/bloomApi";
import { useL10n } from "../react_components/l10nHooks";
import "./TeamCollectionBookStatusPanel.less";
import { StatusPanelCommon, getLockedInfoChild } from "./statusPanelCommon";
import BloomButton from "../react_components/bloomButton";
import { BloomAvatar } from "../react_components/bloomAvatar";
import { useWebSocketListenerForOneEvent } from "../utils/WebSocketManager";

// The panel that shows the book preview and settings in the collection tab in a Team Collection.

export type LockState =
    | "initializing"
    | "unlocked"
    | "locked"
    | "lockedByMe"
    | "lockedByMeElsewhere"
    | "needsReload"
    | "problem"
    | "disconnected"
    | "lockedByMeDisconnected"
    | "error";

export const TeamCollectionBookStatusPanel: React.FunctionComponent = props => {
    const [state, setState] = useState<LockState>("initializing");
    const [lockedBy, setLockedBy] = useState("");
    const [lockedByDisplay, setLockedByDisplay] = useState("");
    const [lockedWhen, setLockedWhen] = useState("");
    const [lockedMachine, setLockedMachine] = useState("");
    const [reload, setReload] = useState(0);
    const [error, setError] = useState("");
    React.useEffect(() => {
        var lockedByMe = false;
        BloomApi.get(
            "teamCollection/currentBookStatus",
            data => {
                const bookStatus = data.data;
                if (bookStatus.problem) {
                    setState("problem");
                } else if (bookStatus.changedRemotely) {
                    setState("needsReload");
                } else if (bookStatus.who) {
                    // locked by someone
                    setLockedBy(bookStatus.who);
                    const lockedByFullName = `${bookStatus.whoFirstName} ${bookStatus.whoSurname}`.trim();
                    setLockedByDisplay(lockedByFullName || lockedBy);
                    if (
                        bookStatus.who === bookStatus.currentUser &&
                        bookStatus.where === bookStatus.currentMachine
                    ) {
                        setState("lockedByMe");
                        lockedByMe = true;
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
                if (bookStatus.disconnected) {
                    if (lockedByMe) {
                        setState("lockedByMeDisconnected");
                    } else {
                        setState("disconnected");
                    }
                }
            },
            err => {
                // something went wrong. Maybe not registered. Already reported to Sentry, we don't need another throw
                // here, with less information. Displaying the message may tell the user something. I don't think it's
                // worth localizing the fallback message here, which is even less likely to be seen.
                // Enhance: we could display a message telling them to register and perhaps a link to the registration dialog, if the error is 'not registered'.
                setError(
                    err?.response?.statusText ??
                        "Bloom could not determine the status of this book"
                );
                setState("error");
            }
        );
    }, [reload]);

    useWebSocketListenerForOneEvent("bookStatus", "reload", () =>
        setReload(oldValue => oldValue + 1)
    );

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

    // Also used for problem.
    const mainTitleNeedsReload = useL10n(
        "The Team Collection folder received a changed version of the book you were editing.",
        "TeamCollection.NeedsReload",
        "",
        undefined,
        undefined,
        true
    );

    const subTitleHasProblem = useL10n(
        "The Checkin/Checkout system should normally prevent this, but it has happened. Bloom cannot automatically join the work that came in with the work you were doing; you will need Bloom team support for that. Bloom will move your version of the book to the Team Collection Lost & Found when you Reload.",
        "TeamCollection.ConflictingChangeDetails",
        "",
        undefined,
        undefined,
        true
    );

    const subTitleNeedsReload = useL10n(
        "You need to reload the collection to get the latest version before you can check out and edit",
        "TeamCollection.YouShouldReload",
        "",
        undefined,
        undefined,
        true
    );

    const mainTitleDisconnected = useL10n(
        "Disconnected",
        "TeamCollection.Disconnected",
        "",
        undefined,
        undefined,
        true
    );

    const subTitleDisconnected = useL10n(
        "You cannot check out this book while disconnected.",
        "TeamCollection.CannotCheckoutDisconnected",
        "",
        undefined,
        undefined,
        true
    );

    const subTitleDisconnectedCheckedOut = useL10n(
        "You can edit this book, but you will need to reconnect in order to send your changes to your team.",
        "TeamCollection.DisconnectedCheckedOut",
        "",
        undefined,
        undefined,
        true
    );

    const panelContents = (state: LockState): JSX.Element => {
        switch (state) {
            default:
                return <div />; // just while initializing
            case "error":
                // This is just a fallback, which hopefully will never be seen.
                return (
                    <StatusPanelCommon
                        lockState={state}
                        title={error}
                        subTitle=""
                        icon={
                            // not sure this is the best image to use, but it might help convey that things are not set up right.
                            <img src={"Disconnected.svg"} alt="error" />
                        }
                    />
                );
            case "unlocked":
                const checkoutHandler = () => {
                    BloomApi.post(
                        "teamCollection/attemptLockOfCurrentBook",
                        response => {
                            // nothing to do. Change of state is handled by websocket notifications.
                            // We want to keep it that way, so we don't have to worry about here about
                            // whether the checkout attempt succeeded or not.
                        }
                    );
                };

                return (
                    <StatusPanelCommon
                        lockState={state}
                        title={mainTitleUnlocked}
                        subTitle={subTitleUnlocked}
                        icon={
                            <img src={"Team Collection.svg"} alt="available" />
                        }
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
                        // nothing to do. Change of state is handled by websocket notifications.
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
            case "problem":
                return (
                    <StatusPanelCommon
                        lockState={state}
                        title={mainTitleNeedsReload}
                        subTitle={subTitleHasProblem}
                        icon={avatar}
                        children={getLockedInfoChild("")}
                        button={getBloomButton(
                            "Reload",
                            "TeamCollection.Reload",
                            "reload-button",
                            undefined,
                            () => BloomApi.post("common/reloadCollection")
                        )}
                    />
                );
            case "needsReload":
                return (
                    <StatusPanelCommon
                        lockState={state}
                        title={mainTitleNeedsReload}
                        subTitle={subTitleNeedsReload}
                        icon={avatar}
                        children={getLockedInfoChild("")}
                        button={getBloomButton(
                            "Reload",
                            "TeamCollection.Reload",
                            "reload-button",
                            undefined,
                            () => BloomApi.post("common/reloadCollection")
                        )}
                    />
                );
            case "disconnected":
                return (
                    <StatusPanelCommon
                        lockState={state}
                        title={mainTitleDisconnected}
                        subTitle={subTitleDisconnected}
                        icon={
                            <img src={"Disconnected.svg"} alt="disconnected" />
                        }
                    />
                );
            case "lockedByMeDisconnected":
                return (
                    <StatusPanelCommon
                        lockState={state}
                        title={mainTitleLockedByMe}
                        subTitle={subTitleDisconnectedCheckedOut}
                        icon={avatar}
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
