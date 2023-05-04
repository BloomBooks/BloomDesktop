/** @jsx jsx **/
import { jsx, css } from "@emotion/react";

import * as React from "react";
import { lightTheme, kBloomYellow } from "../bloomMaterialUITheme";
import { ThemeProvider, StyledEngineProvider } from "@mui/material/styles";
import { useMemo, useRef, useState } from "react";
import { post, postString } from "../utils/bloomApi";
import { useL10n } from "../react_components/l10nHooks";
import "./TeamCollectionBookStatusPanel.less";
import { StatusPanelCommon, getLockedInfoChild } from "./statusPanelCommon";
import BloomButton from "../react_components/bloomButton";
import { BloomAvatar } from "../react_components/bloomAvatar";
import { useSubscribeToWebSocketForEvent } from "../utils/WebSocketManager";
import { BookProblem } from "../react_components/bookProblem";
import { SimpleMenu, SimpleMenuItem } from "../react_components/simpleMenu";
import { AvatarDialog } from "./AvatarDialog";
import { ForgetChangesDialog } from "./ForgetChangesDialog";
import { createTheme, adaptV4Theme } from "@mui/material/styles";
import WarningIcon from "@mui/icons-material/Warning";
import { IBookTeamCollectionStatus } from "./teamCollectionApi";
import { ForceUnlockDialog } from "./ForceUnlockDialog";
import { kBloomRed } from "../utils/colorUtils";

// The panel that shows the book preview and settings in the collection tab in a Team Collection.

export type StatusPanelState =
    | "initializing" // initial retrieval of IBookTeamCollectionStatus not yet completed
    | "unlocked" // book is available to edit, but not checked out
    | "locked" // book is checked out (to someone else)
    | "lockedByMe" // book is checked out to me, here...I can edit it
    | "lockedByMeElsewhere" // book is checked out to me, but on another computer, so I can't edit it here
    | "needsReload" // the collection needs to be reloaded before we can do anything with this book
    | "problem" // The book has a problem, like a conflict between my changes and someone else's
    | "hasInvalidRepoData" // the book has a catastrophic problem: the repo version is unreadable.
    | "disconnected" // Can't tell what's going on, because we don't have a good connection to the repo
    | "lockedByMeDisconnected" // We're disconnected, but before that happened the book was checked out to me, here
    | "error"; // we couldn't get the IBookTeamCollectionStatus; should never happen.

export const TeamCollectionBookStatusPanel: React.FunctionComponent<IBookTeamCollectionStatus> = props => {
    const [tcPanelState, setTcPanelState] = useState<StatusPanelState>(
        "initializing"
    );
    // Indicates how far along the checkin bar should be (0-100).
    // Non-zero value, when in lockedByMe state, also indicates that we should
    // show the bar and make other alterations in the panel layout.
    // Only applicable in the lockedByMe state.
    const [checkinProgress, setCheckinProgress] = useState(0);

    const [busy, setBusy] = useState(false);
    const [checkinFailed, setCheckinFailed] = useState(false);
    const [avatarDialogOpen, setAvatarDialogOpen] = useState(false);
    const [forgetDialogOpen, setForgetDialogOpen] = useState(false);
    const [forceUnlockDialogOpen, setForceUnlockDialogOpen] = useState(false);
    const [message, setMessage] = useState(props.checkinMessage);
    const messageInput = useRef<HTMLInputElement>(null);

    const lockedByMe =
        props.who !== "" &&
        props.who === props.currentUser &&
        props.where === props.currentMachine;
    const lockedByFullName = `${props.whoFirstName} ${props.whoSurname}`.trim();
    const lockedByDisplay =
        lockedByFullName !== "" ? lockedByFullName : props.who;

    // Calculate panel state
    React.useEffect(() => {
        if (props.disconnected) {
            setTcPanelState(
                lockedByMe ? "lockedByMeDisconnected" : "disconnected"
            );
        } else if (props.hasInvalidRepoData) {
            setTcPanelState("hasInvalidRepoData");
        } else if (props.hasAProblem) {
            setTcPanelState("problem");
        } else if (props.changedRemotely) {
            setTcPanelState("needsReload");
        } else if (props.who) {
            // locked by someone
            if (lockedByMe) {
                setTcPanelState("lockedByMe");
            } else {
                setTcPanelState(
                    props.who === props.currentUser
                        ? "lockedByMeElsewhere"
                        : "locked"
                );
            }
        } else {
            setTcPanelState("unlocked");
        }
    }, [
        props.disconnected,
        props.hasAProblem,
        props.changedRemotely,
        props.who,
        lockedByMe,
        props.currentUser,
        props.hasInvalidRepoData
    ]);

    React.useEffect(() => {
        setMessage(props.checkinMessage);
        // This typically happens when a button in the collection tab is clicked.
        // The button gets focus, and we think that's right...a user might want to
        // manipulate it or switch buttons by keyboard. But, probably because for now
        // it's in a separate browser control, the input's caret may continue to flash
        // even though it's not focused. Then the user may wonder why typing does
        // nothing. Blurring it gets rid of the caret until it is clicked.
        messageInput?.current?.blur();
        // I'm not clear why we need tcPanelState here, but without it, the old
        // message came back after a forget-changes and fresh checkout.
    }, [props.checkinMessage, tcPanelState]);

    React.useEffect(() => {
        // When we start a checkin, progress becomes non-zero, which makes the "checking in "
        // message and bar show up. We want to keep it showing until checkin is complete,
        // at which point we switch to some other main state, typically unlocked. Then, it
        // needs to go back to zero so that we won't be in the "checking in" mode the next
        // time the lockedByMe state happens.
        if (tcPanelState != "lockedByMe" && checkinProgress != 0) {
            setCheckinProgress(0);
        }
    }, [tcPanelState]);

    useSubscribeToWebSocketForEvent(
        "checkinProgress",
        "progress",
        e => setCheckinProgress((e as any).fraction),
        false
    );

    let avatar: JSX.Element;
    if (tcPanelState.startsWith("locked")) {
        avatar = (
            <BloomAvatar
                email={props.who ?? ""}
                name={lockedByDisplay}
                borderColor={
                    tcPanelState === "lockedByMe" &&
                    (lightTheme.palette.warning.main as any) // `as any` here patches over a minor typescript typing problem
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
    const checkingIn = useL10n(
        "Checking in...",
        "TeamCollection.CheckingIn",
        undefined,
        undefined,
        undefined,
        true
    );
    const whatChanges = useL10n(
        "What changes did you make?",
        "TeamCollection.WhatChanges",
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
        props.when,
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
        props.where,
        undefined,
        true
    );
    const lockedElsewhereInfo = useL10n(
        "You checked out this book on %0.",
        "TeamCollection.YouCheckedOutOn",
        "The %0 is a date.",
        props.when,
        undefined,
        true
    );

    // Also used for problem.
    const mainTitleNeedsReload = useL10n(
        "The Team Collection folder received a changed version of the book you have selected.",
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

    const subTitleCheckinFailed = useL10n(
        "Checkin failed. You may need to check your network connection and reload the collection.",
        "TeamCollection.CheckinFailed",
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

    const menuItems: (SimpleMenuItem | "-")[] = [
        {
            text: "About my Avatar...",
            l10nKey: "TeamCollection.AboutAvatar",
            action: () => setAvatarDialogOpen(true)
        }
    ];

    if (tcPanelState == "lockedByMe") {
        menuItems.push("-");
        menuItems.push({
            text: "Forget Changes & Check in Book...",
            l10nKey: "TeamCollection.ForgetChangesMenuItem",
            action: () => setForgetDialogOpen(true),
            disabled: props.newLocalBook
        });
    }

    if (tcPanelState == "locked" || tcPanelState == "lockedByMeElsewhere") {
        menuItems.push("-");
        menuItems.push({
            text: "Force Unlock (Administrator Only)...",
            l10nKey: "TeamCollection.ForceUnlockMenuItem",
            action: () => setForceUnlockDialogOpen(true),
            disabled: !props.isUserAdmin,
            icon: (
                <WarningIcon
                    css={css`
                        color: ${kBloomRed};
                    `}
                ></WarningIcon>
            )
        });
    }

    const menu = (
        <SimpleMenu
            text="..."
            l10nKey="Common.Ellipsis"
            temporarilyDisableI18nWarning={true}
            items={menuItems}
        ></SimpleMenu>
    );

    // We want the checkin button in an orange color that isn't one of the two(!)
    // colors that a material UI theme can have. So we make another theme just to
    // show the button. Next version of Material should be able to do more theme colors.
    const dangerTheme = useMemo(
        () =>
            createTheme(
                adaptV4Theme({
                    palette: {
                        primary: {
                            main: kBloomYellow
                        }
                    }
                })
            ),
        []
    );

    const panelContents = (state: StatusPanelState): JSX.Element => {
        switch (state) {
            default:
                return <div />; // just while initializing
            case "error":
                // This is just a fallback, which hopefully will never be seen.
                return (
                    <StatusPanelCommon
                        lockState={state}
                        title={props.error}
                        subTitle=""
                        icon={
                            // not sure this is the best image to use, but it might help convey that things are not set up right.
                            <img
                                src={"/bloom/images/Disconnected.svg"}
                                alt="error"
                            />
                        }
                    />
                );
            case "unlocked": {
                const checkoutHandler = () => {
                    setBusy(true);
                    post(
                        "teamCollection/attemptLockOfCurrentBook",
                        response => {
                            // Not much to do. Change of state is handled by websocket notifications.
                            // We want to keep it that way, so we don't have to worry here about
                            // whether the checkout attempt succeeded or not.
                            setBusy(false);
                        },
                        error => {
                            setBusy(false);
                        }
                    );
                };

                return (
                    <StatusPanelCommon
                        css={css`
                            ${busy &&
                                "cursor: progress; .checkout-button{cursor:progress;}"}
                        `}
                        lockState={state}
                        title={mainTitleUnlocked}
                        subTitle={subTitleUnlocked}
                        icon={
                            <img
                                src={
                                    "/bloom/teamCollection/Team Collection.svg"
                                }
                                alt="available"
                            />
                        }
                        button={getBloomButton(
                            "Check out book",
                            "TeamCollection.Checkout",
                            "checkout-button",
                            "/bloom/teamCollection/Check Out.svg",
                            checkoutHandler
                        )}
                        menu={menu}
                    />
                );
            }
            case "lockedByMe": {
                const checkinHandler = () => {
                    setBusy(true);
                    setCheckinProgress(0.0001); // just enough to show the bar at once
                    post(
                        "teamCollection/checkInCurrentBook",
                        () => {
                            // not much to do. Most change of state is handled by websocket notifications.
                            setCheckinFailed(false); // in case of previous failure, but it will change to "checked in" anyway.
                            setBusy(false);
                        },
                        // failure handler
                        () => {
                            setBusy(false);
                            setCheckinFailed(true);
                            setCheckinProgress(0); // Should be redundant, but makes sure.
                        }
                    );
                };

                return (
                    <StatusPanelCommon
                        css={css`
                            ${busy &&
                                "cursor: progress; .checkin-button{cursor:progress;}"};
                            .panel-children {
                                margin-top: 10px; // leaves some extra space for the "What changes did you make" overlay
                            }
                        `}
                        lockState={state}
                        title={
                            checkinProgress === 0
                                ? mainTitleLockedByMe
                                : checkingIn
                        }
                        subTitle={
                            checkinFailed
                                ? subTitleCheckinFailed
                                : checkinProgress === 0
                                ? subTitleLockedByMe
                                : ""
                        }
                        icon={avatar}
                        //menu={} // eventually the "About my Avatar..." and "Forget Changes" menu gets passed in here.
                        button={
                            <StyledEngineProvider injectFirst>
                                <ThemeProvider theme={dangerTheme}>
                                    {getBloomButton(
                                        "Check in book",
                                        "TeamCollection.CheckIn",
                                        "checkin-button",
                                        "/bloom/teamCollection/Check In.svg",
                                        checkinHandler,
                                        checkinProgress > 0,
                                        "primary"
                                    )}
                                </ThemeProvider>
                            </StyledEngineProvider>
                        }
                        menu={menu}
                    >
                        {checkinProgress === 0 ? (
                            <div
                                css={css`
                                    width: 320px;
                                `}
                            >
                                <div
                                    css={css`
                                        font-size: 11px;
                                    `}
                                >
                                    {whatChanges}
                                </div>
                                <input
                                    ref={messageInput}
                                    css={css`
                                        background-color: transparent;
                                        color: ${kBloomYellow};
                                        width: 100%;
                                        border: 1px solid #ffffffcc;
                                        border-radius: 4px;
                                        height: 36px;
                                    `}
                                    type="text"
                                    value={message}
                                    autoFocus={true}
                                    key="message"
                                    onChange={e => {
                                        setMessage(e.target.value);
                                        postString(
                                            "teamCollection/checkinMessage",
                                            e.target.value
                                        );
                                    }}
                                />
                            </div>
                        ) : (
                            <div
                                css={css`
                                    height: 10px;
                                    background-color: transparent;
                                    width: 100%;
                                    border: 1px solid ${kBloomYellow};
                                `}
                            >
                                <div
                                    css={css`
                                        height: 10px;
                                        background-color: ${kBloomYellow};
                                        width: ${checkinProgress * 100}%;
                                    `}
                                ></div>
                            </div>
                        )}
                    </StatusPanelCommon>
                );
            }
            case "lockedByMeElsewhere":
                return (
                    <StatusPanelCommon
                        lockState={state}
                        title={mainTitleLockedElsewhere}
                        subTitle={subTitleLockedElsewhere}
                        icon={avatar}
                        menu={menu}
                    >
                        {getLockedInfoChild(lockedElsewhereInfo)}
                    </StatusPanelCommon>
                );
            case "locked":
                return (
                    <StatusPanelCommon
                        lockState={state}
                        title={mainTitleLocked}
                        subTitle={subTitleLocked}
                        icon={avatar}
                        menu={menu}
                    >
                        {getLockedInfoChild(lockedInfo)}
                    </StatusPanelCommon>
                );
            case "problem":
                return (
                    <StatusPanelCommon
                        lockState={state}
                        title={mainTitleNeedsReload}
                        subTitle={subTitleHasProblem}
                        icon={avatar}
                        button={getBloomButton(
                            "Reload",
                            "TeamCollection.Reload",
                            "reload-button",
                            undefined,
                            () => post("common/reloadCollection")
                        )}
                        menu={menu}
                    >
                        {getLockedInfoChild("")}
                    </StatusPanelCommon>
                );
            case "hasInvalidRepoData":
                return (
                    <BookProblem
                        css={css`
                            max-width: 560px;
                        `} // to match StatusPanelCommon
                        errorMessage={props.hasInvalidRepoData}
                        clickHereArg={props.clickHereArg}
                    />
                );
            case "needsReload":
                return (
                    <StatusPanelCommon
                        lockState={state}
                        title={mainTitleNeedsReload}
                        subTitle={subTitleNeedsReload}
                        icon={avatar}
                        button={getBloomButton(
                            "Reload",
                            "TeamCollection.Reload",
                            "reload-button",
                            undefined,
                            () => post("common/reloadCollection")
                        )}
                        menu={menu}
                    >
                        {getLockedInfoChild("")}
                    </StatusPanelCommon>
                );
            case "disconnected":
                return (
                    <StatusPanelCommon
                        lockState={state}
                        title={mainTitleDisconnected}
                        subTitle={subTitleDisconnected}
                        icon={
                            <img
                                src={"/bloom/images/Disconnected.svg"}
                                alt="disconnected"
                            />
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
                        menu={menu}
                    />
                );
        }
    };

    return (
        <StyledEngineProvider injectFirst>
            <ThemeProvider theme={lightTheme}>
                {panelContents(tcPanelState)}
                <AvatarDialog
                    open={avatarDialogOpen}
                    close={() => setAvatarDialogOpen(false)}
                    currentUser={props.currentUser}
                    currentUserName={props.currentUserName}
                ></AvatarDialog>
                <ForgetChangesDialog
                    open={forgetDialogOpen}
                    close={() => setForgetDialogOpen(false)}
                ></ForgetChangesDialog>
                <ForceUnlockDialog
                    open={forceUnlockDialogOpen}
                    close={() => setForceUnlockDialogOpen(false)}
                ></ForceUnlockDialog>
            </ThemeProvider>
        </StyledEngineProvider>
    );
};

export const getBloomButton = (
    english: string,
    l10nKey: string,
    buttonClass: string,
    icon?: string,
    clickHandler?: () => void,
    disabled?: boolean,
    color?: "primary" | "secondary" | undefined
) => (
    <BloomButton
        iconBeforeText={icon ? <img src={icon} /> : <div />}
        l10nKey={l10nKey}
        hasText={true}
        enabled={!disabled}
        className={buttonClass}
        onClick={clickHandler}
        temporarilyDisableI18nWarning={true}
        color={color || "primary"}
    >
        {english}
    </BloomButton>
);
