// Storybook stories for Team Collection components
import theme from "../bloomMaterialUITheme";
import { ThemeProvider } from "@material-ui/styles";
import * as React from "react";
import { storiesOf, addDecorator } from "@storybook/react";
import Avatar from "react-avatar";
import { StorybookContext } from "../.storybook/StoryBookContext";
import { StatusPanelCommon, getLockedInfoChild } from "./statusPanelCommon";
import { getBloomButton } from "./TeamCollectionBookStatusPanel";
import "./TeamCollectionBookStatusPanel.less";
import { Typography } from "@material-ui/core";

addDecorator(storyFn => (
    <ThemeProvider theme={theme}>
        <StorybookContext.Provider value={true}>
            {storyFn()}
        </StorybookContext.Provider>
    </ThemeProvider>
));

// Try to simulate the environment of the page preview
const wrapperStyles: React.CSSProperties = {
    width: "500px",
    border: "1px solid green",
    backgroundColor: "lightgreen"
};
const pageStyles: React.CSSProperties = {
    height: "100%",
    flexDirection: "column",
    display: "flex",
    width: "750px"
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

const avatar = (
    <React.Suspense fallback={<></>}>
        <Avatar
            md5Email={"a5e59e90237da2c858802c1bb106e56c"}
            size={"48px"}
            round={true}
        />
    </React.Suspense>
);
storiesOf("Team Collection components", module)
    .add("Available", () =>
        testPage(
            <StatusPanelCommon
                lockState="unlocked"
                title="This book is available for editing"
                subTitle="When you check it out, no one on the team will be able to modify it or see your changes until you check it back in."
                icon={<img src={"Available.svg"} alt="available" />}
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
                icon={avatar}
                button={checkinButton}
                children={
                    <div className="userChanges">
                        <Typography align="left" variant="subtitle2">
                            Eventually this will be a change log area.
                        </Typography>
                    </div>
                }
                menu={<div>Menu</div>}
            />
        )
    )
    .add("Checked out by (Fred)", () =>
        testPage(
            <StatusPanelCommon
                lockState="locked"
                title="This book is checked out to Fred"
                subTitle="You cannot edit the book until Fred checks it in."
                icon={avatar}
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
                icon={avatar}
                children={getLockedInfoChild(
                    "You checked out this book on 14 February 2021."
                )}
            />
        )
    );
