/** @jsx jsx **/
import { jsx, css } from "@emotion/core";
import * as React from "react";
import { useState, useContext } from "react";

import {
    PreviewPanel,
    HelpGroup,
    SettingsPanel,
    PublishPanel
} from "../commonPublish/PublishScreenBaseComponents";
import { PDFPrintFeaturesGroup } from "./PDFPrintFeaturesGroup";
import PublishScreenTemplate from "../commonPublish/PublishScreenTemplate";
import ReactDOM = require("react-dom");
import { ThemeProvider } from "@material-ui/styles";
import { darkTheme, lightTheme } from "../../bloomMaterialUITheme";
import { StorybookContext } from "../../.storybook/StoryBookContext";
import HelpLink from "../../react_components/helpLink";
import { useL10n } from "../../react_components/l10nHooks";
import { Button, Typography } from "@material-ui/core";

export const PDFPrintPublishScreen = () => {
    // When the user changes booklet mode, printshop features, etc., we
    // need to rebuild the book and re-run all of our Bloom API queries.
    // This requires a hard-reset of the whole screen, which we do by
    // incrementing a `key` prop on the core of this screen.
    const [keyForReset, setKeyForReset] = useState(0);
    return (
        <PDFPrintPublishScreenInternal
            key={keyForReset}
            onReset={() => {
                setKeyForReset(keyForReset + 1);
            }}
        />
    );
};

const PDFPrintPublishScreenInternal: React.FunctionComponent<{
    onReset: () => void;
}> = props => {
    const inStorybookMode = useContext(StorybookContext);
    // I left some commented code in here that may be useful in previewing; from Publish -> Android
    // const [heading, setHeading] = useState(
    //     useL10n("Creating Digital Book", "PublishTab.Android.Creating")
    // );
    // const [closePending, setClosePending] = useState(false);
    // const [highlightRefresh, setHighlightRefresh] = useState(false);
    // const [progressState, setProgressState] = useState(ProgressState.Working);

    // bookUrl is expected to be a normal, well-formed URL.
    // (that is, one that you can directly copy/paste into your browser and it would work fine)
    // const [bookUrl, setBookUrl] = useState(
    //     inStorybookMode
    //         ? window.location.protocol +
    //               "//" +
    //               window.location.host +
    //               "/templates/Sample Shells/The Moon and the Cap" // Enhance: provide an actual bloompub in the source tree
    //         : // otherwise, wait for the websocket to deliver a url when the c# has finished creating the bloompub.
    //           //BloomPlayer recognizes "working" as a special value; it will show some spinner or some such.
    //           "working"
    // );

    //const pathToOutputBrowser = inStorybookMode ? "./" : "../../";

    const mainPanel = (
        <React.Fragment>
            <PreviewPanel>
                <ThemeProvider theme={darkTheme}>
                    <Typography
                        css={css`
                            color: white;
                            align-self: center;
                        `}
                    >
                        Temporary placeholder for eventual Preview
                    </Typography>
                </ThemeProvider>
            </PreviewPanel>
            <PublishPanel
                css={css`
                    display: block;
                    flex-grow: 1;
                `}
            ></PublishPanel>
        </React.Fragment>
    );

    const optionsPanel = (
        <SettingsPanel>
            <PDFPrintFeaturesGroup
                onChange={() => {
                    props.onReset();
                }}
            />
            {/* push everything to the bottom */}
            <div
                css={css`
                    margin-top: auto;
                `}
            />
            <HelpGroup>
                <Typography>Not a real "HelpGroup"; needs changing</Typography>
                {/* Replace with links to PDF and Printing help
                <HelpLink
                    l10nKey="PublishTab.Android.AboutBloomPUB"
                    helpId="Tasks/Publish_tasks/Make_a_BloomPUB_file_overview.htm"
                >
                    About BloomPUB
                </HelpLink>
                */}
            </HelpGroup>
        </SettingsPanel>
    );

    const printButtonText = useL10n("Print...", "PublishTab.PrintButton");

    const saveButtonText = useL10n("Save PDF...", "PublishTab.SaveButton");

    const rightSideControls = (
        <React.Fragment>
            <Button onClick={() => {}}>
                <img src="./Print.png" />
                <div
                    css={css`
                        width: 0.5em;
                    `}
                />
                {printButtonText}
            </Button>
            <div
                css={css`
                    width: 1em;
                `}
            />
            <Button onClick={() => {}}>
                <img src="./Usb.png" />
                <div
                    css={css`
                        width: 0.5em;
                    `}
                />
                {saveButtonText}
            </Button>
        </React.Fragment>
    );

    return (
        <React.Fragment>
            <PublishScreenTemplate
                bannerTitleEnglish="Publish to PDF &amp; Print"
                bannerTitleL10nId="PublishTab.PdfPrint.BannerTitle"
                bannerRightSideControls={rightSideControls}
                optionsPanelContents={optionsPanel}
            >
                {mainPanel}
            </PublishScreenTemplate>
            {/* In storybook, there's no bloom backend to run the progress dialog */}
            {/* {inStorybookMode || (
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
            )} */}
        </React.Fragment>
    );
};

// a bit goofy... currently the html loads everything in publishUIBundlejs. So all the publish screens
// get any not-in-a-class code called, including ours. But it only makes sense to get wired up
// if that html has the root page we need.
if (document.getElementById("PdfPrintPublishScreen")) {
    ReactDOM.render(
        <ThemeProvider theme={lightTheme}>
            <PDFPrintPublishScreen />
        </ThemeProvider>,
        document.getElementById("PdfPrintPublishScreen")
    );
}
