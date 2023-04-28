/* eslint-disable @typescript-eslint/no-empty-function */
import { lightTheme } from "../bloomMaterialUITheme";
import * as React from "react";
import { ThemeProvider, StyledEngineProvider } from "@mui/material/styles";
import { storiesOf } from "@storybook/react";
import { addDecorator } from "@storybook/react";
import { ReaderPublishScreen } from "./ReaderPublish/ReaderPublishScreen";
import { DeviceAndControls } from "./commonPublish/DeviceAndControls";
import { StorybookContext } from "../.storybook/StoryBookContext";
import {
    ProgressDialogInner,
    ProgressState
} from "./commonPublish/PublishProgressDialogInner";
import { loremIpsum } from "lorem-ipsum";
import { withA11y } from "@storybook/addon-a11y";
import { EPUBPublishScreen } from "./ePUBPublish/ePUBPublishScreen";
import BookMetadataDialog from "./metadata/BookMetadataDialog";
import "./storiesApiMocks";
import { AccessibilityCheckScreen } from "./accessibilityCheck/accessibilityCheckScreen";
import { normalDialogEnvironmentForStorybook } from "../react_components/BloomDialog/BloomDialogPlumbing";
import {
    IUploadCollisionDlgProps,
    UploadCollisionDlg
} from "./LibraryPublish/uploadCollisionDlg";
import { PublishAudioVideo } from "./video/PublishAudioVideo";
import PublishScreenTemplate from "./commonPublish/PublishScreenTemplate";
import PublishScreenBanner from "./commonPublish/PublishScreenBanner";
import { Button, Typography } from "@mui/material";
import { LibraryPublishScreen } from "./LibraryPublish/LibraryPublishScreen";

addDecorator(withA11y as any);

addDecorator(storyFn => (
    <StyledEngineProvider injectFirst>
        <ThemeProvider theme={lightTheme}>
            <StorybookContext.Provider value={true}>
                {storyFn()}
            </StorybookContext.Provider>
        </ThemeProvider>
    </StyledEngineProvider>
));

const testText =
    loremIpsum({
        count: 3,
        format: "html",
        units: "paragraphs"
    }) + "<a target='_blank' href='https://google.com'>google.com</a>";

storiesOf("Publish/ProgressDialog", module)
    .add("Working", () => (
        <div>
            <ProgressDialogInner
                progressState={ProgressState.Working}
                messages={testText}
                heading={"Working hard..."}
                instruction={"Just sit there and watch it spin."}
                onUserClosed={() => {}}
                onUserStopped={() => {}}
            />
        </div>
    ))
    .add("Done", () => (
        <div>
            <ProgressDialogInner
                progressState={ProgressState.Done}
                messages={testText}
                onUserClosed={() => {}}
                onUserStopped={() => {}}
            />
        </div>
    ))
    .add("Error", () => (
        <div>
            <ProgressDialogInner
                progressState={ProgressState.Done}
                errorEncountered={true}
                messages={testText}
                heading={"Sky is falling"}
                onUserClosed={() => {}}
                onUserStopped={() => {}}
            />
        </div>
    ));

storiesOf("Publish/DeviceFrame", module)
    .add("DeviceFrame Default Portrait, rotate-able", () => (
        <DeviceAndControls defaultLandscape={false} canRotate={true} url="">
            Portrait
        </DeviceAndControls>
    ))
    .add("DeviceFrame Landscape only with Refresh button", () => (
        <DeviceAndControls
            defaultLandscape={true}
            canRotate={false}
            url=""
            showPreviewButton={true}
        >
            Landscape
        </DeviceAndControls>
    ))
    .add("DeviceFrame Landscape only with highlighted Refresh button", () => (
        <DeviceAndControls
            defaultLandscape={true}
            canRotate={false}
            url=""
            showPreviewButton={true}
            highlightPreviewButton={true}
        >
            Landscape
        </DeviceAndControls>
    ))
    .add("DeviceFrame Landscape , rotate-able", () => (
        <DeviceAndControls defaultLandscape={true} canRotate={true} url="">
            Landscape
        </DeviceAndControls>
    ));

const someButton = (
    <div>
        <Button variant="contained" color="primary">
            Some control 1
        </Button>
        <Button variant="contained" color="primary">
            Some control 2
        </Button>
    </div>
);

const optionHeader = (
    <div>
        <Typography variant="h6">Options Panel here:</Typography>
        <p>{loremIpsum({ count: 5 })}</p>
    </div>
);

const testMainPanelContents = (
    <div>
        <Typography variant="h2">Main Panel Contents here:</Typography>
        <p>{loremIpsum({ count: 5 })}</p>
        <p>{loremIpsum({ count: 7 })}</p>
        <p>{loremIpsum({ count: 3 })}</p>
        <p>{loremIpsum({ count: 4 })}</p>
    </div>
);

storiesOf("Publish/BaseTemplate", module)
    .add("PublishScreenTemplate", () => (
        <PublishScreenTemplate
            bannerTitleEnglish="Publish as Audio or Video"
            bannerTitleL10nId="PublishTab.RecordVideo.BannerTitle"
            bannerDescriptionL10nId="PublishTab.RecordVideo.BannerDescription"
            bannerDescriptionMarkdown="Create video files that you can upload to sites like Facebook and [YouTube](https://www.youtube.com). You can also make videos to share with people who use inexpensive “feature phones” and even audio-only files for listening."
            bannerRightSideControls={someButton}
            optionsPanelContents={optionHeader}
        >
            {testMainPanelContents}
        </PublishScreenTemplate>
    ))
    .add("PublishScreenBanner/ePUB", () => (
        <PublishScreenBanner
            titleEnglish="Publish as ePUB"
            titleL10nId="PublishTab.Epub.BannerTitle"
        >
            {someButton}
        </PublishScreenBanner>
    ));

storiesOf("Publish/Bloom Reader", module).add("ReaderPublishScreen", () => (
    <ReaderPublishScreen />
));

storiesOf("Publish/Video", module).add("PublishAudioVideo", () => (
    <PublishAudioVideo />
));

storiesOf("Publish/ePUB", module)
    .add("EPUBPublishScreen", () => <EPUBPublishScreen />)
    .add("Book Metadata Dialog", () => (
        <BookMetadataDialog
            startOpen={true}
            onClose={() => alert("BookMetadataDialog closed with OK")}
        />
    ))
    .add("AccessibilityCheckScreen", () => <AccessibilityCheckScreen />);

const propsObject: IUploadCollisionDlgProps = {
    userEmail: "testEmail@sil.org",
    newTitle: "Title of New Upload",
    newLanguages: ["Sokoro", "English"],
    existingTitle: "Title on BL Server",
    existingBookUrl: "https://dev.bloomlibrary.org/book/ALkGcILEG3",
    existingLanguages: ["English", "French"],
    existingCreatedDate: "10/21/2021",
    existingUpdatedDate: "10/29/2021",
    dialogEnvironment: normalDialogEnvironmentForStorybook
};

const lotsOfLanguages = ["Sokoro", "English", "Swahili", "Hausa"];

storiesOf("Publish/Web", module)
    .add("LibraryPublishScreen", () =>
        React.createElement(() => <LibraryPublishScreen />)
    )
    .add("Upload Collision Dialog", () =>
        React.createElement(() => <UploadCollisionDlg {...propsObject} />)
    )
    .add("Upload Collision Dialog -- lots of languages", () =>
        React.createElement(() => (
            <UploadCollisionDlg
                {...propsObject}
                newLanguages={lotsOfLanguages}
            />
        ))
    );
