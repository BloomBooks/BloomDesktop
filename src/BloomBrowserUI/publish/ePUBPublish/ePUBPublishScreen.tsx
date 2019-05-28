import * as React from "react";
import { useState, useContext } from "react";
import {
    BasePublishScreen,
    PreviewPanel,
    PublishPanel,
    SettingsPanel
} from "../commonPublish/BasePublishScreen";
import "./ePUBPublish.less";
import { DeviceAndControls } from "../commonPublish/DeviceAndControls";
import ReactDOM = require("react-dom");
import { ThemeProvider } from "@material-ui/styles";
import theme from "../../bloomMaterialUITheme";
import { StorybookContext } from "../../.storybook/StoryBookContext";
import { useWebSocketListenerForOneMessage } from "../../utils/WebSocketManager";
import { BloomApi } from "../../utils/bloomApi";
import BloomButton from "../../react_components/bloomButton";
import { EPUBHelpGroup } from "./ePUBHelpGroup";
import PWithLink from "../../react_components/pWithLink";
import { EPUBSettingsGroup } from "./ePUBSettingsGroup";

export const EPUBPublishScreen = () => {
    // When the user changes some features, included languages, etc., we
    // need to rebuild the book and re-run all of our Bloom API queries.
    // This requires a hard-reset of the whole screen, which we do by
    // incrementing a `key` prop on the core of this screen.
    const [keyForReset, setKeyForReset] = useState(0);
    return (
        <EPUBPublishScreenInternal
            key={keyForReset}
            onReset={() => {
                setKeyForReset(keyForReset + 1);
            }}
        />
    );
};

const EPUBPublishScreenInternal: React.FunctionComponent<{
    onReset: () => void;
}> = props => {
    const inStorybookMode = useContext(StorybookContext);
    const [bookUrl, setBookUrl] = useState(
        inStorybookMode
            ? window.location.protocol +
                  "//" +
                  window.location.host +
                  "/templates/Sample Shells/The Moon and the Cap" // Enhance: provide an actual bloomd in the source tree
            : "" // otherwise, wait for the websocket to deliver a url when the c# has finished creating the bloomd
    );

    // TODO: generalize this backend call out of android
    const [defaultLandscape] = BloomApi.useApiBoolean(
        "publish/android/defaultLandscape",
        false
    );
    // TODO: generalize this backend call out of android
    const [canRotate] = BloomApi.useApiBoolean(
        "publish/android/canRotate",
        false
    );
    // TODO: get epub preview
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

                    <PWithLink
                        className="readium-credit"
                        l10nKey="PublishTab.Epub.ReadiumCredit"
                        href="https://readium.org/"
                    >
                        This ePUB preview is provided by [Readium]. This book
                        may render differently in various ePUB readers.
                    </PWithLink>
                </PreviewPanel>
                <PublishPanel>
                    <BloomButton
                        className="save-button"
                        enabled={true}
                        clickApiEndpoint={"publish/epub/save"}
                        hasText={true}
                        l10nKey="PublishTab.Save"
                    >
                        Save...
                    </BloomButton>
                </PublishPanel>
                <SettingsPanel>
                    <EPUBSettingsGroup />
                    <EPUBHelpGroup />
                </SettingsPanel>
            </BasePublishScreen>
            {/* In storybook, there's no bloom backend to run the progress dialog */}
            {/* {inStorybookMode || <ReaderPublishProgressDialog />} */}
        </>
    );
};

// a bit goofy... currently the html loads everything in publishUIBundlejs. So all the publish screens
// get any not-in-a-class code called, including ours. But it only makes sense to get wired up
// if that html has the root page we need.
if (document.getElementById("ePUBPublishScreen")) {
    ReactDOM.render(
        <ThemeProvider theme={theme}>
            <EPUBPublishScreen />
        </ThemeProvider>,
        document.getElementById("ePUBPublishScreen")
    );
}
