import * as React from "react";
import { useState, useContext } from "react";

import {
    BasePublishScreen,
    PreviewPanel,
    PublishPanel,
    HelpGroup,
    SettingsPanel
} from "../commonPublish/BasePublishScreen";
import { MethodChooser } from "./MethodChooser";
import { PublishFeaturesGroup } from "./PublishFeaturesGroup";
import { ThumbnailGroup } from "./ThumbnailGroup";
import "./ReaderPublish.less";
import { DeviceAndControls } from "../commonPublish/DeviceAndControls";
import ReactDOM = require("react-dom");
import { ThemeProvider } from "@material-ui/styles";
import theme from "../../bloomMaterialUITheme";
import { StorybookContext } from "../../.storybook/StoryBookContext";
import {
    useWebSocketListenerForOneMessage,
    useWebSocketListenerForOneEvent
} from "../../utils/WebSocketManager";
import { BloomApi } from "../../utils/bloomApi";
import HelpLink from "../../react_components/helpLink";
import HtmlHelpLink from "../../react_components/htmlHelpLink";
import Link from "../../react_components/link";
import { PublishProgressDialog } from "../commonPublish/PublishProgressDialog";
import { useL10n } from "../../react_components/l10nHooks";
import { ProgressState } from "../commonPublish/ProgressDialog";
import { PublishLanguagesGroup } from "./PublishLanguagesGroup";

export const ReaderPublishScreen = () => {
    // When the user changes some features, included languages, etc., we
    // need to rebuild the book and re-run all of our Bloom API queries.
    // This requires a hard-reset of the whole screen, which we do by
    // incrementing a `key` prop on the core of this screen.
    const [keyForReset, setKeyForReset] = useState(0);
    return (
        <ReaderPublishScreenInternal
            key={keyForReset}
            onReset={() => {
                setKeyForReset(keyForReset + 1);
            }}
        />
    );
};

const ReaderPublishScreenInternal: React.FunctionComponent<{
    onReset: () => void;
}> = props => {
    const inStorybookMode = useContext(StorybookContext);
    const [heading, setHeading] = useState(
        useL10n("Creating Digital Book", "PublishTab.Android.Creating")
    );
    const [closePending, setClosePending] = useState(false);
    const [progressState, setProgressState] = useState(ProgressState.Working);
    const [bookUrl, setBookUrl] = useState(
        inStorybookMode
            ? window.location.protocol +
                  "//" +
                  window.location.host +
                  "/templates/Sample Shells/The Moon and the Cap" // Enhance: provide an actual bloomd in the source tree
            : "" // otherwise, wait for the websocket to deliver a url when the c# has finished creating the bloomd
    );

    const [defaultLandscape] = BloomApi.useApiBoolean(
        "publish/android/defaultLandscape",
        false
    );
    const [canRotate] = BloomApi.useApiBoolean(
        "publish/android/canRotate",
        false
    );
    useWebSocketListenerForOneMessage(
        "publish-android",
        "androidPreview",
        url => {
            setBookUrl(url);
        }
    );
    const pathToOutputBrowser = inStorybookMode ? "./" : "../../";
    const usbWorking = useL10n("Publishing", "PublishTab.Common.Publishing");
    const wifiWorking = useL10n("Publishing", "PublishTab.Common.Publishing");

    useWebSocketListenerForOneEvent(
        "publish-android",
        "publish/android/state",
        e => {
            switch (e.message) {
                case "stopped":
                    setClosePending(true);
                    break;
                case "UsbStarted":
                    setClosePending(false);
                    setHeading(usbWorking);
                    setProgressState(ProgressState.Serving);
                    break;
                case "ServingOnWifi":
                    setClosePending(false);
                    setHeading(wifiWorking);
                    setProgressState(ProgressState.Serving);
                    break;
                default:
                    throw new Error(
                        "Method Chooser does not understand the state: " +
                            e.message
                    );
            }
        }
    );

    return (
        <>
            <BasePublishScreen className="ReaderPublishScreen">
                <PreviewPanel>
                    <DeviceAndControls
                        defaultLandscape={defaultLandscape}
                        canRotate={canRotate}
                        url={
                            pathToOutputBrowser +
                            "bloom-player/dist/bloomplayer.htm?url=" +
                            bookUrl
                        }
                        showRefresh={true}
                        onRefresh={() => props.onReset()}
                    />
                </PreviewPanel>
                <PublishPanel>
                    <MethodChooser />
                </PublishPanel>
                <SettingsPanel>
                    <PublishFeaturesGroup
                        onChange={() => {
                            props.onReset();
                        }}
                    />
                    <ThumbnailGroup onChange={() => props.onReset()} />
                    <PublishLanguagesGroup />
                    <HelpGroup>
                        <HelpLink
                            l10nKey="PublishTab.Android.AboutBookFeatures"
                            helpId="Tasks/Publish_tasks/Features.htm"
                        >
                            About Book Features
                        </HelpLink>
                        <HtmlHelpLink
                            l10nKey="PublishTab.Android.Troubleshooting"
                            fileid="Publish-Android-Troubleshooting"
                        >
                            Troubleshooting Tips
                        </HtmlHelpLink>
                        <HelpLink
                            l10nKey="PublishTab.Android.AboutBloomReader"
                            helpId="Concepts/Bloom_Reader_App.htm"
                        >
                            About Bloom Reader
                        </HelpLink>
                        <div className="icon-link-row get-bloom-reader">
                            <a href="https://play.google.com/store/search?q=%22sil%20international%22%2B%22bloom%20reader%22&amp;c=apps">
                                <img
                                    className="playIcon"
                                    src="Google_Play_symbol_2016.svg"
                                />
                            </a>
                            <Link
                                id="getBloomReaderLink"
                                href="https://play.google.com/store/search?q=%22sil%20international%22%2B%22bloom%20reader%22&amp;c=apps"
                                l10nKey="PublishTab.Android.GetBloomReader"
                                l10nComment="Link to find Bloom Reader on Google Play Store"
                            >
                                Get Bloom Reader App
                            </Link>
                        </div>
                    </HelpGroup>
                </SettingsPanel>
            </BasePublishScreen>
            {/* In storybook, there's no bloom backend to run the progress dialog */}
            {inStorybookMode || (
                <PublishProgressDialog
                    heading={heading}
                    startApiEndpoint="publish/android/updatePreview"
                    webSocketClientContext="publish-android"
                    progressState={progressState}
                    setProgressState={setProgressState}
                    closePending={closePending}
                    setClosePending={setClosePending}
                    onUserStopped={() => {
                        BloomApi.postData("publish/android/usb/stop", {});
                        BloomApi.postData("publish/android/wifi/stop", {});
                        setClosePending(true);
                    }}
                />
            )}
        </>
    );
};

// a bit goofy... currently the html loads everything in publishUIBundlejs. So all the publish screens
// get any not-in-a-class code called, including ours. But it only makes sense to get wired up
// if that html has the root page we need.
if (document.getElementById("BloomReaderPublishScreen")) {
    ReactDOM.render(
        <ThemeProvider theme={theme}>
            <ReaderPublishScreen />
        </ThemeProvider>,
        document.getElementById("BloomReaderPublishScreen")
    );
}
