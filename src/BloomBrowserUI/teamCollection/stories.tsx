/** @jsx jsx **/
import { jsx, css } from "@emotion/react";

// Storybook stories for Team Collection components
import { lightTheme, kBloomYellow } from "../bloomMaterialUITheme";
import { ThemeProvider, StyledEngineProvider } from "@mui/material/styles";
import * as React from "react";
import { storiesOf, addDecorator } from "@storybook/react";
import { StorybookContext } from "../.storybook/StoryBookContext";
import { StatusPanelCommon, getLockedInfoChild } from "./statusPanelCommon";
import { getBloomButton } from "./TeamCollectionBookStatusPanel";
import "./TeamCollectionBookStatusPanel.less";
import { BloomAvatar } from "../react_components/bloomAvatar";
import { JoinTeamCollectionDialog } from "./JoinTeamCollectionDialog";
import { TeamCollectionDialogLauncher } from "./TeamCollectionDialog";
import { TeamCollectionSettingsPanel } from "./TeamCollectionSettingsPanel";
import { CreateTeamCollectionDialog } from "./CreateTeamCollection";
import {
    BloomDialog,
    DialogBottomButtons,
    DialogMiddle,
    DialogTitle
} from "../react_components/BloomDialog/BloomDialog";
import { SimpleMenu, SimpleMenuItem } from "../react_components/simpleMenu";
import { DialogCancelButton } from "../react_components/BloomDialog/commonDialogComponents";
import {
    normalDialogEnvironmentForStorybook,
    StorybookDialogWrapper
} from "../react_components/BloomDialog/BloomDialogPlumbing";

addDecorator(storyFn => (
    <StyledEngineProvider injectFirst>
        <ThemeProvider theme={lightTheme}>
            <StorybookContext.Provider value={true}>
                <div id="reactRoot">{storyFn()}</div>
            </StorybookContext.Provider>
        </ThemeProvider>
    </StyledEngineProvider>
));

// Try to simulate the environment of the page preview
const wrapperStyles: React.CSSProperties = {
    height: "300px",
    width: "560px", // imitate A5 page width
    border: "1px solid green",
    backgroundColor: "lightgreen"
};
const pageStyles: React.CSSProperties = {
    height: "100%",
    flexDirection: "column",
    display: "flex",
    width: "100%" // imitate the whole Bloom Edit window
};
const menuStyles: React.CSSProperties = {
    border: "1px solid red"
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
    "Check In.svg"
);

const reloadButton = getBloomButton(
    "Reload",
    "TeamCollection.Reload",
    "reload-button"
);

const avatar = (lockedByMe: boolean) => (
    <BloomAvatar
        email={"test@example.com"}
        name={"A B"}
        borderColor={lockedByMe && (lightTheme.palette.warning.main as any)} // `as any` here patches over a minor typescript typing problem
    />
);

let emptyAvatarForProblemState: JSX.Element;

storiesOf("Team Collection components/StatusPanelCommon", module)
    .add("Available", () =>
        testPage(
            <StatusPanelCommon
                title="This book is available for editing"
                subTitle="When you check it out, no one on the team will be able to modify it or see your changes until you check it back in."
                icon={<img src={"Team Collection.svg"} alt="available" />}
                button={getBloomButton(
                    "Check out book",
                    "TeamCollection.Checkout",
                    "someOtherClass",
                    "Check Out.svg"
                )}
            />
        )
    )
    .add("Checked out by me", () => {
        const messageLogStub = ( // copied from TCBookStatusPanel.tsx
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
            </StatusPanelCommon>
        );
    })
    .add("Checked out by (Fred)", () =>
        testPage(
            <StatusPanelCommon
                title="This book is checked out to Fred"
                subTitle="You cannot edit the book until Fred checks it in."
                icon={avatar(false)}
            >
                {getLockedInfoChild(
                    "Fred checked out this book on 10 February 2021."
                )}
            </StatusPanelCommon>
        )
    )
    .add("Conflicting Change state", () =>
        testPage(
            <StatusPanelCommon
                title="The Team Collection folder received a changed version of the book you were editing."
                subTitle="The Checkin/Checkout system should normally prevent this, but it has happened. Bloom cannot automatically join the work that came in with the work you were doing; you will need Bloom team support for that. Bloom will move your version of the book to the Team Collection Lost & Found when you Reload."
                icon={emptyAvatarForProblemState}
                button={reloadButton}
            >
                {getLockedInfoChild("")}
            </StatusPanelCommon>
        )
    )
    .add("Checked out by me on MyTablet", () =>
        testPage(
            <StatusPanelCommon
                title="This book is checked out to you, but on a different computer"
                subTitle="You cannot edit the book on this computer, until you check it in on MyTablet."
                icon={avatar(false)}
            >
                {getLockedInfoChild(
                    "You checked out this book on 14 February 2021."
                )}
            </StatusPanelCommon>
        )
    );

storiesOf("Team Collection components/JoinTeamCollection", module)
    .add("new collection", () => (
        <div id="reactRoot" className="JoinTeamCollection">
            <JoinTeamCollectionDialog
                collectionName="foobar"
                existingCollection={false}
                isAlreadyTcCollection={false}
                isCurrentCollection={false}
                isSameCollection={false}
                existingCollectionFolder=""
                conflictingCollection=""
                dialogEnvironment={normalDialogEnvironmentForStorybook}
            />
        </div>
    ))
    .add("existing collection", () => (
        <div id="reactRoot" className="JoinTeamCollection">
            <JoinTeamCollectionDialog
                collectionName="foobar"
                existingCollection={true}
                isAlreadyTcCollection={false}
                isCurrentCollection={false}
                isSameCollection={false}
                existingCollectionFolder="somewhere"
                conflictingCollection=""
                dialogEnvironment={normalDialogEnvironmentForStorybook}
            />
        </div>
    ))
    .add("existing TC collection, same location and guid", () => (
        <div id="reactRoot" className="JoinTeamCollection">
            <JoinTeamCollectionDialog
                collectionName="foobar"
                existingCollection={true}
                isAlreadyTcCollection={true}
                isCurrentCollection={true}
                isSameCollection={true}
                existingCollectionFolder="some good place"
                conflictingCollection=""
                dialogEnvironment={normalDialogEnvironmentForStorybook}
            />
        </div>
    ))
    .add("existing TC collection, different location same guid", () => (
        <div id="reactRoot" className="JoinTeamCollection">
            <JoinTeamCollectionDialog
                collectionName="foobar"
                existingCollection={true}
                isAlreadyTcCollection={true}
                isCurrentCollection={false}
                isSameCollection={true}
                existingCollectionFolder="some good place"
                conflictingCollection="some bad place"
                dialogEnvironment={normalDialogEnvironmentForStorybook}
            />
        </div>
    ))
    .add("existing TC collection, different location and guid", () => (
        <div id="reactRoot" className="JoinTeamCollection">
            <JoinTeamCollectionDialog
                collectionName="foobar"
                existingCollection={true}
                isAlreadyTcCollection={true}
                isCurrentCollection={false}
                isSameCollection={false}
                existingCollectionFolder="some good place"
                conflictingCollection="some bad place"
                dialogEnvironment={normalDialogEnvironmentForStorybook}
            />
        </div>
    ))
    .add("existing collection, bare frame", () => (
        <div id="reactRoot" className="JoinTeamCollection">
            <JoinTeamCollectionDialog
                collectionName="foobar"
                existingCollection={true}
                isAlreadyTcCollection={false}
                isCurrentCollection={false}
                isSameCollection={false}
                existingCollectionFolder="somewhere"
                conflictingCollection=""
                dialogEnvironment={{
                    dialogFrameProvidedExternally: true,
                    initiallyOpen: true
                }}
            />
        </div>
    ));

storiesOf("Team Collection components/TeamCollectionDialog", module)
    .add("With reload button", () => (
        <StorybookDialogWrapper
            id="TeamCollectionDialog"
            params={{ showReloadButton: true }}
        >
            <TeamCollectionDialogLauncher />
        </StorybookDialogWrapper>
    ))
    .add("Without reload button", () => (
        <StorybookDialogWrapper
            id="TeamCollectionDialog"
            params={{ showReloadButton: false }}
        >
            <TeamCollectionDialogLauncher />
        </StorybookDialogWrapper>
    ));

storiesOf(
    "Team Collection components",
    module
).add("TeamCollectionSettingsPanel", () => <TeamCollectionSettingsPanel />);

storiesOf("Team Collection components/CreateTeamCollection", module)
    .add("CreateTeamCollection Dialog", () => (
        <CreateTeamCollectionDialog
            dialogEnvironment={normalDialogEnvironmentForStorybook}
        />
    ))
    .add("CreateTeamCollection Dialog showing path", () => (
        <CreateTeamCollectionDialog
            dialogEnvironment={normalDialogEnvironmentForStorybook}
            defaultRepoFolder="z:\Enim aute dolore ex voluptate commodo\"
        />
    ))
    .add("CreateTeamCollection Dialog showing error", () => (
        <CreateTeamCollectionDialog
            dialogEnvironment={normalDialogEnvironmentForStorybook}
            errorForTesting="Commodo veniam laboris ut ut ea laboris Lorem Lorem laborum enim minim velit."
        />
    ));

const menuItems: (SimpleMenuItem | "-")[] = [
    {
        text: "About my Avatar...",
        l10nKey: "TeamCollection.AboutAvatar",
        action: () => undefined
    }
];
const menuBoxStyles: React.CSSProperties = {
    display: "flex",
    justifyContent: "flex-end",
    border: "1px solid red",
    padding: 20,
    backgroundColor: "black",
    width: 150
};

storiesOf("Team Collection components/Menu component", module).add(
    "SimpleMenu test",
    () => (
        <div style={menuBoxStyles}>
            <SimpleMenu
                text="..."
                l10nKey="Common.Ellipsis"
                temporarilyDisableI18nWarning={true}
                items={menuItems}
            ></SimpleMenu>
        </div>
    )
);

storiesOf("BloomDialog", module).add("Test drag & resize", () => (
    <BloomDialog onClose={() => undefined} open={true}>
        <DialogTitle title="Drag Me" />
        <DialogMiddle>
            <p>Blah</p>
            <p>Blah</p>
            <p>
                Lorem ipsum dolor sit amet, consectetur adipiscing elit.
                Curabitur in felis feugiat est pellentesque bibendum. Maecenas
                non sem a augue vulputate ultricies. In hac habitasse platea
                dictumst. Quisque augue quam, facilisis in laoreet ac,
                consectetur luctus lectus. Cras eu condimentum sem.
            </p>
            <p>Blah</p>
        </DialogMiddle>
        <DialogBottomButtons>
            <DialogCancelButton onClick_DEPRECATED={() => undefined} />
        </DialogBottomButtons>
    </BloomDialog>
));
