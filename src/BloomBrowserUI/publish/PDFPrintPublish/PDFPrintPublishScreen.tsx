/** @jsx jsx **/
import { jsx, css } from "@emotion/react";
import * as React from "react";
import { useState, useContext, useEffect, useRef } from "react";

import {
    PreviewPanel,
    HelpGroup,
    SettingsPanel,
    PreviewPublishPanel
} from "../commonPublish/PublishScreenBaseComponents";
import { PDFPrintFeaturesGroup } from "./PDFPrintFeaturesGroup";
import PublishScreenTemplate from "../commonPublish/PublishScreenTemplate";
import ReactDOM = require("react-dom");
import { ThemeProvider, StyledEngineProvider } from "@mui/material/styles";
import { darkTheme, lightTheme } from "../../bloomMaterialUITheme";
import { StorybookContext } from "../../.storybook/StoryBookContext";
import { useL10n } from "../../react_components/l10nHooks";
import Typography from "@mui/material/Typography";
import Button from "@mui/material/Button";
import {
    CircularProgress,
    Dialog,
    DialogContent,
    Paper,
    PaperProps
} from "@mui/material";
import { getString, post, useWatchString } from "../../utils/bloomApi";
import WebSocketManager, {
    IBloomWebSocketEvent
} from "../../utils/WebSocketManager";
import { Div, Span } from "../../react_components/l10nComponents";
import { ApiCheckbox } from "../../react_components/ApiCheckbox";
import { DialogOkButton } from "../../react_components/BloomDialog/commonDialogComponents";
import { DialogBottomButtons } from "../../react_components/BloomDialog/BloomDialog";
import Draggable from "react-draggable";

export const PDFPrintPublishScreen = () => {
    const readable = new ReadableStream();
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

    const [path, setPath] = useState("");
    const [progressOpen, setProgressOpen] = useState(false);
    const progress = useWatchString("Making PDF", "publish", "progress");
    const [progressTask, setProgressTask] = useState("");
    const [progressContent, setProgressContent] = useState<string[]>([]);
    const [percent, setPercent] = useState(0);
    const [printSettings, setPrintSettings] = useState("");
    // We need a ref to the real DOM object of the iframe that holds the print preview
    // so we can actually tell it to print.
    const iframeRef = useRef<HTMLIFrameElement>(null);
    useEffect(() => {
        // We receive as progress messages each line of output that the PdfMaker program
        // outputs to standard output. The ones that we are interested in look like
        // Making PDF|Percent: 10
        // When we get one of those, update our progress display.
        const parts = progress.split("|");
        if (parts.length > 1) {
            // We typically get a succession of messages with the same thing before
            // the vertical bar. When we get a new one, add a line to progress content.
            if (progressTask !== parts[0]) {
                setProgressTask(parts[0]);
                setProgressContent(oldContent => [...oldContent, parts[0]]);
            }
            if (parts[1].startsWith("Percent: ")) {
                setPercent(
                    parseInt(parts[1].substring("Percent: ".length), 10)
                );
            }
        }
    }, [progress, progressTask]);
    const progressHeader = useL10n("Progress", "Common.Progress");

    // Using this as the 'PaperContent' property of a MaterialUI Dialog
    // (with a couple of other tricks) makes the whole dialog draggable.
    // The handle specifies the elements within the dialog by which it
    // can be dragged (in this case the whole content).
    function PaperComponentForDraggableDialog(props: PaperProps) {
        return (
            <Draggable handle="#draggable-handle">
                <Paper {...props} />
            </Draggable>
        );
    }

    const mainPanel = (
        <React.Fragment>
            <PreviewPanel
                // This panel has a black background. If it is visible, it looks odd combined with
                // the grey background (which we can't change) that WebView2 shows when previewing a PDF.
                css={css`
                    padding: 0;
                `}
            >
                <StyledEngineProvider injectFirst>
                    <ThemeProvider theme={darkTheme}>
                        {path ? (
                            <iframe
                                ref={iframeRef}
                                css={css`
                                    height: 100%;
                                    width: 100%;
                                `}
                                src={path}
                            />
                        ) : (
                            <Typography
                                css={css`
                                    color: white;
                                    align-self: center;
                                    margin-left: 20px;
                                `}
                            >
                                <Span l10nKey="PublishTab.PdfMaker.ClickToStart">
                                    "Click a button on the right to start
                                    creating PDF."
                                </Span>
                            </Typography>
                        )}
                    </ThemeProvider>
                </StyledEngineProvider>
            </PreviewPanel>
            <PreviewPublishPanel
                css={css`
                    display: block;
                    flex-grow: 1;
                `}
            ></PreviewPublishPanel>
        </React.Fragment>
    );

    const optionsPanel = (
        <SettingsPanel>
            <PDFPrintFeaturesGroup
                onChange={() => {
                    setProgressContent([]); // clean up anything from previous run
                    setPercent(0);
                    setProgressOpen(true);
                }}
                onGotPdf={path => {
                    setPath(path);
                    setProgressOpen(false);
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

    const printNow = () => {
        if (iframeRef.current) {
            iframeRef.current.contentWindow?.print();
            // Unfortunately, we have no way to know whether the user really
            // printed, or canceled in the browser print dialog.
            post("publish/pdf/printAnalytics");
        }
    };

    const handlePrint = () => {
        getString("publish/pdf/printSettingsPath", instructions => {
            if (instructions) {
                // This causes the instructions image to be displayed along with a dialog
                // in which the user can continue (or set a checkbox to prevent this
                // happening again.)
                setPrintSettings(instructions);
            } else {
                printNow();
            }
        });
    };

    const rightSideControls = (
        <React.Fragment>
            <Button onClick={handlePrint} disabled={!path}>
                <img src="./Print.png" />
                <div
                    css={css`
                        width: 0.5em;
                    `}
                />
                <span
                    css={css`
                        color: black;
                        opacity: ${path ? "100%" : "38%"};
                        text-transform: none !important;
                    `}
                >
                    {printButtonText}
                </span>
            </Button>
            <div
                css={css`
                    width: 1em;
                `}
            />
            <Button
                onClick={() => {
                    post("publish/pdf/save");
                }}
                disabled={!path}
            >
                <img src="./Usb.png" />
                <div
                    css={css`
                        width: 0.5em;
                    `}
                />
                <span
                    css={css`
                        color: black;
                        opacity: ${path ? "100%" : "38%"};
                        text-transform: none !important;
                    `}
                >
                    {saveButtonText}
                </span>
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
            <Dialog open={progressOpen}>
                <div
                    css={css`
                        height: 200px;
                        width: 300px;
                        position: relative;
                        padding: 10px;
                    `}
                >
                    <div
                        css={css`
                            position: absolute;
                            top: 10px;
                            right: 10px;
                        `}
                    >
                        <CircularProgress
                            variant="determinate"
                            value={percent}
                            size={40}
                            thickness={5}
                        />
                    </div>
                    <div>
                        <div
                            css={css`
                                font-weight: bold;
                                margin-bottom: 15px;
                            `}
                        >
                            {progressHeader}
                        </div>
                        {progressContent.map(s => (
                            <p
                                key={s}
                                css={css`
                                    margin: 0;
                                `}
                            >
                                {s}
                            </p>
                        ))}
                    </div>
                </div>
            </Dialog>
            <Dialog
                open={!!printSettings}
                // These two lines, plus the definition of PaperComponentForDraggableDialog above,
                // combined with putting the draggable-handle id on the DialogContent, are the magic
                // that makes the dialog draggable.
                PaperComponent={PaperComponentForDraggableDialog}
                aria-labelledby="draggable-handle"
            >
                <DialogContent id="draggable-handle" style={{ cursor: "move" }}>
                    <Div l10nKey="SamplePrintNotification.PleaseNotice">
                        Please notice the sample printer settings below. Use
                        them as a guide while you set up the printer.
                    </Div>
                    <ApiCheckbox
                        english="I get it. Do not show this again."
                        l10nKey="SamplePrintNotification.IGetIt"
                        apiEndpoint="publish/pdf/dontShowSamplePrint"
                    ></ApiCheckbox>
                </DialogContent>
                <DialogBottomButtons
                    css={css`
                        box-sizing: border-box;
                        padding: 0 15px 10px 0;
                    `}
                >
                    <DialogOkButton
                        css={css`
                            margin: 20px;
                        `}
                        onClick={() => {
                            printNow();
                            // This is unfortunate. It will hide not only this dialog but the image that
                            // shows how to set things. The call to printNow will initially show the dialog that
                            // the print settings are supposed to help with. We'd like to have it
                            // visible until the user clicks Print or Cancel. But
                            // - We can't find any way to find out when the user clicks Print or Cancel,
                            //   so we'd have to leave it up to the user to click some Close control to get rid
                            //   of the settings (we could of course get rid of this dialog right away).
                            // - The print dialog occupies more-or-less the entire WebView2 control; there's
                            //   nowhere left to show the recommendations.
                            // So we just have to hope the user can remember them.
                            setPrintSettings("");
                        }}
                    ></DialogOkButton>
                </DialogBottomButtons>
            </Dialog>
            <div
                css={css`
                    position: absolute;
                    bottom: 0;
                    right: 0;
                    display: ${printSettings ? "block" : "none"};
                `}
            >
                <img src={printSettings} />
            </div>
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
                        postData("publish/android/usb/stop", {});
                        postData("publish/android/wifi/stop", {});
                        setClosePending(true);
                    }}
                />
            )} */}
        </React.Fragment>
    );
};

// a bit goofy... currently the html loads everything in pdf. So all the publish screens
// get any not-in-a-class code called, including ours. But it only makes sense to get wired up
// if that html has the root page we need.
if (document.getElementById("PdfPrintPublishScreen")) {
    ReactDOM.render(
        <StyledEngineProvider injectFirst>
            <ThemeProvider theme={lightTheme}>
                <PDFPrintPublishScreen />
            </ThemeProvider>
        </StyledEngineProvider>,
        document.getElementById("PdfPrintPublishScreen")
    );
}
