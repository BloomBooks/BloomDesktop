/** @jsx jsx **/
import { jsx, css } from "@emotion/core";

import * as React from "react";
import { useState, useContext } from "react";
import {
    PreviewPanel,
    PublishPanel,
    SettingsPanel
} from "../commonPublish/PublishScreenBaseComponents";
import PublishScreenTemplate from "../commonPublish/PublishScreenTemplate";
import { DeviceAndControls } from "../commonPublish/DeviceAndControls";
import * as ReactDOM from "react-dom";
import ThemeProvider from "@material-ui/styles/ThemeProvider";
import { lightTheme } from "../../bloomMaterialUITheme";
import { StorybookContext } from "../../.storybook/StoryBookContext";
import {
    useSubscribeToWebSocketForStringMessage,
    useSubscribeToWebSocketForEvent
} from "../../utils/WebSocketManager";
import BloomButton from "../../react_components/bloomButton";
import { EPUBHelpGroup } from "./ePUBHelpGroup";
import { PWithLink } from "../../react_components/pWithLink";
import { EPUBSettingsGroup } from "./ePUBSettingsGroup";
import Typography from "@material-ui/core/Typography";
import { PublishProgressDialog } from "../commonPublish/PublishProgressDialog";
import BookMetadataDialog from "../metadata/BookMetadataDialog";
import { useL10n } from "../../react_components/l10nHooks";
import { ProgressState } from "../commonPublish/PublishProgressDialogInner";
import { BloomApi } from "../../utils/bloomApi";

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
                  "/templates/Sample Shells/The Moon and the Cap" // Enhance: provide an actual epub in the source tree
            : "" // otherwise, wait for the websocket to deliver a url when the c# has finished creating the epub
    );

    const [landscape] = BloomApi.useApiBoolean("publish/epub/landscape", false);

    useSubscribeToWebSocketForEvent(
        "publish-epub",
        "startingEbookCreation",
        e => {
            setProgressState(ProgressState.Working);
        }
    );

    // The c# api responds to changes of settings by auto-starting a new epub build. When
    // it is done, it calls this (but actually the same url, alas).
    useSubscribeToWebSocketForStringMessage(
        "publish-epub",
        "newEpubReady",
        url => {
            // add a random component so that react will reload the iframe
            setBookUrl(url + "&random=" + Math.random().toString());
            setClosePending(true);
        }
    );
    const isLicenseOK = BloomApi.useWatchBooleanEvent(
        true,
        "publish-epub",
        "publish/licenseOK"
    );

    const mainPanel = (
        <div className="ePUBPublishScreen">
            <PreviewPanel>
                <DeviceAndControls
                    defaultLandscape={landscape}
                    canRotate={false}
                    url={bookUrl}
                />
                <Typography
                    css={css`
                        color: whitesmoke;
                        width: 200px;
                        margin-top: auto !important; // The two "!important"s here are to override
                        margin-bottom: 20px !important; // MUI Typography's default margins.
                    `}
                >
                    <PWithLink
                        css={css`
                            a {
                                display: inline;
                            }
                            a:link,
                            a:visited {
                                color: whitesmoke;
                                text-decoration: underline;
                            }
                        `}
                        l10nKey="PublishTab.Epub.ReadiumCredit"
                        href="https://readium.org/"
                    >
                        This ePUB preview is provided by [Readium]. This book
                        may render differently in various ePUB readers.
                    </PWithLink>
                </Typography>
            </PreviewPanel>
            <PublishPanel>
                <div
                    css={css`
                        margin-top: 30px;
                        margin-left: 176px;
                    `}
                >
                    <BloomButton
                        css={css`
                            // without this, it grows to the width of the column
                            align-self: flex-start;
                        `}
                        enabled={isLicenseOK}
                        clickApiEndpoint={"publish/epub/save"}
                        hasText={true}
                        l10nKey="PublishTab.Save"
                    >
                        Save...
                    </BloomButton>
                </div>
            </PublishPanel>
        </div>
    );

    const optionsPanel = (
        <SettingsPanel>
            <EPUBSettingsGroup />
            {/* push everything below this to the bottom */}
            <div
                css={css`
                    margin-top: auto;
                `}
            />
            <EPUBHelpGroup />
        </SettingsPanel>
    );

    return (
        <React.Fragment>
            <PublishScreenTemplate
                bannerTitleEnglish="Publish as ePUB"
                bannerTitleL10nId="PublishTab.Epub.BannerTitle"
                bannerDescriptionMarkdown="Make an electronic book that can be read in EPUB readers on all devices."
                bannerDescriptionL10nId="PublishTab.Epub.BannerDescription"
                optionsPanelContents={optionsPanel}
            >
                {mainPanel}
            </PublishScreenTemplate>
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
        </React.Fragment>
    );
};

// a bit goofy... currently the html loads everything in publishUIBundlejs. So all the publish screens
// get any not-in-a-class code called, including ours. But it only makes sense to get wired up
// if that html has the root page we need.
if (document.getElementById("ePUBPublishScreen")) {
    ReactDOM.render(
        <ThemeProvider theme={lightTheme}>
            <EPUBPublishScreen />
        </ThemeProvider>,
        document.getElementById("ePUBPublishScreen")
    );
}
