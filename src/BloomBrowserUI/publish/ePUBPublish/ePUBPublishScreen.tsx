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
import {
    useWebSocketListenerForOneMessage,
    useWebSocketListenerForOneEvent
} from "../../utils/WebSocketManager";
import BloomButton from "../../react_components/bloomButton";
import { EPUBHelpGroup } from "./ePUBHelpGroup";
import PWithLink from "../../react_components/pWithLink";
import { EPUBSettingsGroup } from "./ePUBSettingsGroup";
import { Typography } from "@material-ui/core";
import { PublishProgressDialog } from "../commonPublish/PublishProgressDialog";
import BookMetadataDialog from "../metadata/BookMetadataDialog";
import { useL10n } from "../../react_components/l10nHooks";
import { ProgressState } from "../commonPublish/ProgressDialog";

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
}> = () => {
    const inStorybookMode = useContext(StorybookContext);
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

    useWebSocketListenerForOneEvent(
        "publish-epub",
        "startingEbookCreation",
        e => {
            setProgressState(ProgressState.Working);
        }
    );

    // The c# api responds to changes of settings by auto-starting a new epub build. When
    // it is done, it calls this (but actually the same url, alas).
    useWebSocketListenerForOneMessage("publish-epub", "newEpubReady", url => {
        // add a random component so that react will reload the iframe
        setBookUrl(url + "&random=" + Math.random().toString());
        setClosePending(true);
    });

    return (
        <>
            <BasePublishScreen className="ePUBPublishScreen">
                <PreviewPanel>
                    <DeviceAndControls
                        defaultLandscape={false}
                        canRotate={false}
                        url={bookUrl}
                    />
                    <Typography className="readium-credit">
                        <PWithLink
                            l10nKey="PublishTab.Epub.ReadiumCredit"
                            href="https://readium.org/"
                        >
                            This ePUB preview is provided by [Readium]. This
                            book may render differently in various ePUB readers.
                        </PWithLink>
                    </Typography>
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

            <PublishProgressDialog
                heading={useL10n("Creating ePUB", "PublishTab.Epub.Creating")}
                webSocketClientContext="publish-epub"
                startApiEndpoint="publish/epub/updatePreview"
                progressState={progressState}
                setProgressState={setProgressState}
                closePending={closePending}
                setClosePending={setClosePending}
            />
            <BookMetadataDialog />
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
