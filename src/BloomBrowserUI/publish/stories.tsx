/* eslint-disable @typescript-eslint/no-empty-function */
import { lightTheme } from "../bloomMaterialUITheme";
import * as React from "react";
import { ThemeProvider, StyledEngineProvider } from "@mui/material/styles";
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
    IUploadCollisionDlgData,
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

export default {
    title: "Publish/ProgressDialog"
};

export const _Working = () => (
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
);

export const _Done = () => (
    <div>
        <ProgressDialogInner
            progressState={ProgressState.Done}
            messages={testText}
            onUserClosed={() => {}}
            onUserStopped={() => {}}
        />
    </div>
);

export const Error = () => (
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
);

export default {
    title: "Publish/DeviceFrame"
};

export const DeviceFrameDefaultPortraitRotateAble = () => (
    <DeviceAndControls defaultLandscape={false} canRotate={true} url="">
        Portrait
    </DeviceAndControls>
);

DeviceFrameDefaultPortraitRotateAble.story = {
    name: "DeviceFrame Default Portrait, rotate-able"
};

export const DeviceFrameLandscapeOnlyWithRefreshButton = () => (
    <DeviceAndControls
        defaultLandscape={true}
        canRotate={false}
        url=""
        showPreviewButton={true}
    >
        Landscape
    </DeviceAndControls>
);

DeviceFrameLandscapeOnlyWithRefreshButton.story = {
    name: "DeviceFrame Landscape only with Refresh button"
};

export const DeviceFrameLandscapeOnlyWithHighlightedRefreshButton = () => (
    <DeviceAndControls
        defaultLandscape={true}
        canRotate={false}
        url=""
        showPreviewButton={true}
        highlightPreviewButton={true}
    >
        Landscape
    </DeviceAndControls>
);

DeviceFrameLandscapeOnlyWithHighlightedRefreshButton.story = {
    name: "DeviceFrame Landscape only with highlighted Refresh button"
};

export const DeviceFrameLandscapeRotateAble = () => (
    <DeviceAndControls defaultLandscape={true} canRotate={true} url="">
        Landscape
    </DeviceAndControls>
);

DeviceFrameLandscapeRotateAble.story = {
    name: "DeviceFrame Landscape , rotate-able"
};

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

export default {
    title: "Publish/BaseTemplate"
};

export const _PublishScreenTemplate = () => (
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
);

_PublishScreenTemplate.story = {
    name: "PublishScreenTemplate"
};

export const PublishScreenBannerEPub = () => (
    <PublishScreenBanner
        titleEnglish="Publish as ePUB"
        titleL10nId="PublishTab.Epub.BannerTitle"
    >
        {someButton}
    </PublishScreenBanner>
);

PublishScreenBannerEPub.story = {
    name: "PublishScreenBanner/ePUB"
};

export default {
    title: "Publish/Bloom Reader"
};

export const _ReaderPublishScreen = () => <ReaderPublishScreen />;

_ReaderPublishScreen.story = {
    name: "ReaderPublishScreen"
};

export default {
    title: "Publish/Video"
};

export const _PublishAudioVideo = () => <PublishAudioVideo />;

_PublishAudioVideo.story = {
    name: "PublishAudioVideo"
};

export default {
    title: "Publish/ePUB"
};

export const EpubPublishScreen = () => <EPUBPublishScreen />;

EpubPublishScreen.story = {
    name: "EPUBPublishScreen"
};

export const _BookMetadataDialog = () => (
    <BookMetadataDialog
        startOpen={true}
        onClose={() => alert("BookMetadataDialog closed with OK")}
    />
);

export const _AccessibilityCheckScreen = () => <AccessibilityCheckScreen />;

_AccessibilityCheckScreen.story = {
    name: "AccessibilityCheckScreen"
};

const propsObject: IUploadCollisionDlgData = {
    userEmail: "testEmail@sil.org",
    newTitle: "Title of New Upload",
    newLanguages: ["Sokoro", "English"],
    existingTitle: "Title on BL Server",
    existingBookUrl: "https://dev.bloomlibrary.org/book/ALkGcILEG3",
    existingLanguages: ["English", "French"],
    existingCreatedDate: "10/21/2021",
    existingUpdatedDate: "10/29/2021",
    dialogEnvironment: normalDialogEnvironmentForStorybook,
    count: 1
};

const lotsOfLanguages = ["Sokoro", "English", "Swahili", "Hausa"];

export default {
    title: "Publish/Web"
};

export const _LibraryPublishScreen = () =>
    React.createElement(() => <LibraryPublishScreen />);

_LibraryPublishScreen.story = {
    name: "LibraryPublishScreen"
};

export const UploadCollisionDialog = () =>
    React.createElement(() => (
        <UploadCollisionDlg
            {...propsObject}
            conflictIndex={0}
            setConflictIndex={() => {}}
        />
    ));

export const UploadCollisionDialogLotsOfLanguages = () =>
    React.createElement(() => (
        <UploadCollisionDlg
            {...propsObject}
            newLanguages={lotsOfLanguages}
            conflictIndex={0}
            setConflictIndex={() => {}}
        />
    ));

UploadCollisionDialogLotsOfLanguages.story = {
    name: "Upload Collision Dialog -- lots of languages"
};
