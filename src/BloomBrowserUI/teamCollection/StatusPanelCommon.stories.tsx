import { css } from "@emotion/react";

// Storybook stories for Team Collection components
import { lightTheme, kBloomYellow } from "../bloomMaterialUITheme";
import * as React from "react";
import { StatusPanelCommon, getLockedInfoChild } from "./statusPanelCommon";
import { getBloomButton } from "./TeamCollectionBookStatusPanel";
import "./TeamCollectionBookStatusPanel.less";
import { BloomAvatar } from "../react_components/bloomAvatar";

// Try to simulate the environment of the page preview
const wrapperStyles: React.CSSProperties = {
    height: "300px",
    width: "560px", // imitate A5 page width
    border: "1px solid green",
    backgroundColor: "lightgreen",
};
const pageStyles: React.CSSProperties = {
    height: "100%",
    flexDirection: "column",
    display: "flex",
    width: "100%", // imitate the whole Bloom Edit window
};
const menuStyles: React.CSSProperties = {
    border: "1px solid red",
};

const testPage = (statusPanel: JSX.Element) => (
    <div style={pageStyles}>
        <div id="preview-wrapper" style={wrapperStyles}>
            Book Preview here...
        </div>
        <div id="teamCollection">{statusPanel}</div>
    </div>
);

const checkinButton = getBloomButton(
    "Check in book",
    "TeamCollection.Checkin",
    "checkin-button",
    "Check In.svg",
);

const reloadButton = getBloomButton(
    "Reload",
    "TeamCollection.Reload",
    "reload-button",
);

const avatar = (lockedByMe: boolean) => (
    <BloomAvatar
        email={"test@example.com"}
        name={"A B"}
        borderColor={lockedByMe && (lightTheme.palette.warning.main as any)} // `as any` here patches over a minor typescript typing problem
    />
);

// JT: previously was just left uninitialized, but more recent typescript complains.
// I think the test that uses it may be delibereately testing what the method does with
// an undefined input.
const emptyAvatarForProblemState: JSX.Element = undefined as any as JSX.Element;

export default {
    title: "Team Collection components/StatusPanelCommon",
};

export const Available = () =>
    testPage(
        <StatusPanelCommon
            title="This book is available for editing"
            subTitle="When you check it out, no one on the team will be able to modify it or see your changes until you check it back in."
            icon={<img src={"Team Collection.svg"} alt="available" />}
            button={getBloomButton(
                "Check out book",
                "TeamCollection.Checkout",
                "someOtherClass",
                "Check Out.svg",
            )}
        />,
    );

export const CheckedOutByMe = () => {
    const messageLogStub = // copied from TCBookStatusPanel.tsx
        (
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
                    {"What changes did you make?"}
                </div>
                <input
                    css={css`
                        background-color: transparent;
                        color: ${kBloomYellow};
                        width: 100%;
                        border: 1px solid #ffffffcc;
                        border-radius: 4px;
                        height: 36px;
                    `}
                    type="text"
                    value={
                        "test checkin message that's actually quite longish."
                    }
                    autoFocus={true}
                    key="message"
                />
            </div>
        );

    return testPage(
        <StatusPanelCommon
            title="This book is checked out to you"
            subTitle="Are you done for now? Click this button to send your changes to your team."
            icon={avatar(true)}
            button={checkinButton}
            useWarningColorForButton={true}
            menu={<div style={menuStyles}>Menu</div>}
        >
            {messageLogStub}
        </StatusPanelCommon>,
    );
};

CheckedOutByMe.story = {
    name: "Checked out by me",
};

export const CheckedOutByFred = () =>
    testPage(
        <StatusPanelCommon
            title="This book is checked out to Fred"
            subTitle="You cannot edit the book until Fred checks it in."
            icon={avatar(false)}
        >
            {getLockedInfoChild(
                "Fred checked out this book on 10 February 2021.",
            )}
        </StatusPanelCommon>,
    );

CheckedOutByFred.story = {
    name: "Checked out by (Fred)",
};

export const ConflictingChangeState = () =>
    testPage(
        <StatusPanelCommon
            title="The Team Collection folder received a changed version of the book you were editing."
            subTitle="The Checkin/Checkout system should normally prevent this, but it has happened. Bloom cannot automatically join the work that came in with the work you were doing; you will need Bloom team support for that. Bloom will move your version of the book to the Team Collection Lost & Found when you Reload."
            icon={emptyAvatarForProblemState}
            button={reloadButton}
        >
            {getLockedInfoChild("")}
        </StatusPanelCommon>,
    );

ConflictingChangeState.story = {
    name: "Conflicting Change state",
};

export const CheckedOutByMeOnMyTablet = () =>
    testPage(
        <StatusPanelCommon
            title="This book is checked out to you, but on a different computer"
            subTitle="You cannot edit the book on this computer, until you check it in on MyTablet."
            icon={avatar(false)}
        >
            {getLockedInfoChild(
                "You checked out this book on 14 February 2021.",
            )}
        </StatusPanelCommon>,
    );

CheckedOutByMeOnMyTablet.story = {
    name: "Checked out by me on MyTablet",
};
