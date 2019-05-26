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
import { PublishFeaturesGroup } from "../commonPublish/PublishFeaturesGroup";
import { ThumbnailGroup } from "../commonPublish/ThumbnailGroup";
import "./ReaderPublish.less";
import { DeviceAndControls } from "../commonPublish/DeviceAndControls";
import ReactDOM = require("react-dom");
import { ThemeProvider } from "@material-ui/styles";
import theme from "../../bloomMaterialUITheme";
import { StorybookContext } from "../../.storybook/StoryBookContext";
import { useWebSocketListenerForOneMessage } from "../../utils/WebSocketManager";
import { ReaderPublishProgressDialog } from "./ReaderPublishProgressDialog";
import { BloomApi } from "../../utils/bloomApi";
import HelpLink from "../../react_components/helpLink";
import HtmlHelpLink from "../../react_components/htmlHelpLink";
import Link from "../../react_components/link";

export const ReaderPublishScreen = () => {
    const inStorybookMode = useContext(StorybookContext);
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
                    />
                </PreviewPanel>
                <PublishPanel>
                    <MethodChooser />
                </PublishPanel>
                <SettingsPanel>
                    <PublishFeaturesGroup />
                    <ThumbnailGroup />
                    <HelpGroup>
                        <HelpLink
                            l10nKey="PublishTab.Android.AboutBloomReader"
                            helpId="Concepts/Bloom_Reader_App.htm"
                        >
                            About Bloom Reader
                        </HelpLink>
                        <HtmlHelpLink
                            l10nKey="PublishTab.Android.Troubleshooting"
                            fileid="Publish-Android-Troubleshooting"
                        >
                            Troubleshooting Tips
                        </HtmlHelpLink>
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
            {inStorybookMode || <ReaderPublishProgressDialog />}
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
