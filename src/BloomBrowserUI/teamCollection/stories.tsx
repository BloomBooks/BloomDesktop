// Storybook stories for Team Collection components
import theme from "../bloomMaterialUITheme";
import { ThemeProvider } from "@material-ui/styles";
import * as React from "react";
import { storiesOf, addDecorator } from "@storybook/react";
import { StorybookContext } from "../.storybook/StoryBookContext";
import { StatusPanelCommon, getLockedInfoChild } from "./statusPanelCommon";
import { getBloomButton } from "./TeamCollectionBookStatusPanel";
import "./TeamCollectionBookStatusPanel.less";
import { Typography } from "@material-ui/core";
import { BloomAvatar } from "../react_components/bloomAvatar";
import { JoinTeamCollection } from "./JoinTeamCollection";
import "./JoinTeamCollection.less";
import { TeamCollectionDialog } from "./TeamCollectionDialog";
import { TeamCollectionSettingsPanel } from "./TeamCollectionSettingsPanel";
import { CreateTeamCollection } from "./CreateTeamCollection";
import { ProgressDialog } from "../react_components/IndependentProgressDialog";
import "../react_components/IndependentProgressDialog.less";

addDecorator(storyFn => (
    <ThemeProvider theme={theme}>
        <StorybookContext.Provider value={true}>
            <div id="reactRoot">{storyFn()}</div>
        </StorybookContext.Provider>
    </ThemeProvider>
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

const avatar = (lockedByMe: boolean) => (
    <BloomAvatar
        email={"test@example.com"}
        name={"A B"}
        borderColor={lockedByMe && theme.palette.warning.main}
    />
);

storiesOf("Team Collection components/StatusPanelCommon", module)
    .add("Available", () =>
        testPage(
            <StatusPanelCommon
                lockState="unlocked"
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
    .add("Checked out by me", () =>
        testPage(
            <StatusPanelCommon
                lockState="lockedByMe"
                title="This book is checked out to you"
                subTitle="Are you done for now? Click this button to send your changes to your team."
                icon={avatar(true)}
                button={checkinButton}
                children={
                    <div className="userChanges">
                        <Typography align="left" variant="subtitle2">
                            Eventually this will be a change log area.
                        </Typography>
                    </div>
                }
                menu={<div style={menuStyles}>Menu</div>}
            />
        )
    )
    .add("Checked out by (Fred)", () =>
        testPage(
            <StatusPanelCommon
                lockState="locked"
                title="This book is checked out to Fred"
                subTitle="You cannot edit the book until Fred checks it in."
                icon={avatar(false)}
                children={getLockedInfoChild(
                    "Fred checked out this book on 10 February 2021."
                )}
            />
        )
    )
    .add("Checked out by me on MyTablet", () =>
        testPage(
            <StatusPanelCommon
                lockState="lockedByMeElsewhere"
                title="This book is checked out to you, but on a different computer"
                subTitle="You cannot edit the book on this computer, until you check it in on MyTablet."
                icon={avatar(false)}
                children={getLockedInfoChild(
                    "You checked out this book on 14 February 2021."
                )}
            />
        )
    );

storiesOf("Team Collection components", module).add(
    "JoinTeamCollection",
    () => (
        <div id="reactRoot" className="JoinTeamCollection">
            <JoinTeamCollection />
        </div>
    )
);

storiesOf("Team Collection components", module).add(
    "TeamCollectionDialog",
    () => <TeamCollectionDialog />
);

storiesOf(
    "Team Collection components",
    module
).add("TeamCollectionSettingsPanel", () => <TeamCollectionSettingsPanel />);

storiesOf("Team Collection components", module).add(
    "CreateTeamCollection",
    () => (
        <CreateTeamCollection
            closeDlg={() => {
                alert("close");
            }}
        />
    )
);

storiesOf("ProgressDialog", module).add("ProgressDialog", () => (
    <ProgressDialog />
));
