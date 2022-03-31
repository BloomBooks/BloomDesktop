import { lightTheme } from "../bloomMaterialUITheme";
import * as React from "react";
import { ThemeProvider } from "@material-ui/styles";
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
import { RecordVideoWindow } from "./video/RecordVideoWindow";

addDecorator(withA11y as any);

addDecorator(storyFn => (
    <ThemeProvider theme={lightTheme}>
        <StorybookContext.Provider value={true}>
            {storyFn()}
        </StorybookContext.Provider>
    </ThemeProvider>
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
                onUserCanceled={() => {}}
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
                onUserCanceled={() => {}}
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
                onUserCanceled={() => {}}
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
            showRefresh={true}
        >
            Landscape
        </DeviceAndControls>
    ))
    .add("DeviceFrame Landscape only with highlighted Refresh button", () => (
        <DeviceAndControls
            defaultLandscape={true}
            canRotate={false}
            url=""
            showRefresh={true}
            highlightRefreshIcon={true}
        >
            Landscape
        </DeviceAndControls>
    ))
    .add("DeviceFrame Landscape , rotate-able", () => (
        <DeviceAndControls defaultLandscape={true} canRotate={true} url="">
            Landscape
        </DeviceAndControls>
    ));

storiesOf("Publish/Bloom Reader", module).add("ReaderPublishScreen", () => (
    <ReaderPublishScreen />
));
storiesOf("Publish/Video", module).add("RecordVideoWindow", () => (
    <RecordVideoWindow />
));

storiesOf("Publish/ePUB", module)
    .add("EPUBPublishScreen", () => <EPUBPublishScreen />)
    .add("Book Metadata Dialog", () => <BookMetadataDialog startOpen={true} />)
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

storiesOf("Publish/Share on the web", module)
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
