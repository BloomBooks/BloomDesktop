/** @jsx jsx **/
import { jsx, css } from "@emotion/react";
import * as React from "react";
import { useState, useRef } from "react";

import {
    PreviewPanel,
    HelpGroup,
    SettingsPanel
} from "../commonPublish/PublishScreenBaseComponents";
import { PDFPrintFeaturesGroup } from "./PDFPrintFeaturesGroup";
import PublishScreenTemplate from "../commonPublish/PublishScreenTemplate";
import ReactDOM = require("react-dom");
import { ThemeProvider, StyledEngineProvider } from "@mui/material/styles";
import { darkTheme, lightTheme, kBannerGray } from "../../bloomMaterialUITheme";
import { useL10n } from "../../react_components/l10nHooks";
import Typography from "@mui/material/Typography";
import Button from "@mui/material/Button";
import ArrowForwardRounded from "@mui/icons-material/ArrowForwardRounded";
import { getString, post } from "../../utils/bloomApi";

import { Div } from "../../react_components/l10nComponents";
import { ApiCheckbox } from "../../react_components/ApiCheckbox";
import { DialogOkButton } from "../../react_components/BloomDialog/commonDialogComponents";
import {
    BloomDialog,
    DialogBottomButtons,
    DialogMiddle
} from "../../react_components/BloomDialog/BloomDialog";
import { ProgressDialog } from "../../react_components/Progress/ProgressDialog";
import HelpLink from "../../react_components/helpLink";

// The common behavior of the Print and Save buttons.
// There is probably some way to get this look out of BloomButton,
// but it seems more trouble than it's worth.
const PrintSaveButton: React.FunctionComponent<{
    onClick: () => void;
    label: string;
    l10nId: string;
    imgSrc: string;
    enabled: boolean;
}> = props => {
    const label = useL10n(props.label, props.l10nId);
    return (
        <Button
            css={css`
                opacity: ${props.enabled ? "100%" : "25%"};
            `}
            onClick={props.onClick}
            disabled={!props.enabled}
        >
            <img src={props.imgSrc} />
            <span
                css={css`
                    color: black;
                    margin-left: 0.5em;
                    text-transform: none !important;
                `}
            >
                {label}
            </span>
        </Button>
    );
};

export const PDFPrintPublishScreen = () => {
    const [path, setPath] = useState("");
    const [printSettings, setPrintSettings] = useState("");
    // We need a ref to the real DOM object of the iframe that holds the print preview
    // so we can actually tell it to print.
    const iframeRef = useRef<HTMLIFrameElement>(null);

    const progressHeader = useL10n("Progress", "Common.Progress");
    const formatModeText = useL10n(
        "Bloom can format your PDF in several ways.",
        "PublishTab.PdfMaker.ClickToStart"
    );
    const chooseModeText = useL10n(
        "Choose one here:",
        "PublishTab.PdfMaker.ClickToStart2"
    );
    const showProgress = useRef<() => void | undefined>();
    const closeProgress = useRef<() => void | undefined>();

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
                                    box-sizing: border-box;
                                    // By default in WV2, an iframe has a 2px inset border,
                                    // which against our background only shows up on the top and left.
                                    border-color: gray;
                                    border-style: solid;
                                `}
                                src={path}
                            />
                        ) : (
                            <div
                                css={css`
                                    background-color: ${kBannerGray};
                                    display: flex;
                                    flex: 1;
                                `}
                            >
                                <div
                                    css={css`
                                        display: flex;
                                        flex: 1;
                                        flex-direction: row;
                                        align-items: center;
                                        justify-content: center;
                                        padding: 0 20px 75px;
                                    `}
                                >
                                    <div
                                        css={css`
                                            display: flex;
                                            flex-direction: column;
                                        `}
                                    >
                                        <Typography
                                            color="primary"
                                            fontSize={42}
                                            fontWeight="bold"
                                        >
                                            {formatModeText}
                                        </Typography>
                                        <Typography
                                            color="primary"
                                            fontSize={42}
                                            fontWeight="bold"
                                        >
                                            {chooseModeText}
                                        </Typography>{" "}
                                    </div>
                                    <ArrowForwardRounded
                                        color="primary"
                                        fontWeight="bold"
                                        css={css`
                                            font-size: 90pt;
                                        `}
                                    />
                                </div>
                            </div>
                        )}
                    </ThemeProvider>
                </StyledEngineProvider>
            </PreviewPanel>
        </React.Fragment>
    );

    const optionsPanel = (
        <SettingsPanel>
            <PDFPrintFeaturesGroup
                onChange={() => {
                    showProgress.current?.();
                }}
                onGotPdf={path => {
                    setPath(path);
                    closeProgress.current?.();
                }}
            />
            {/* push everything to the bottom */}
            <div
                css={css`
                    margin-top: auto;
                `}
            />
            <HelpGroup>
                <HelpLink
                    l10nKey="PublishTab.PdfPrint.AboutPdfPrint"
                    // Wants to be PDF_and_Print.htm when that gets written
                    helpId="Tasks/Publish_tasks/PDF_and_Print.htm"
                    temporarilyDisableI18nWarning={true}
                >
                    About PDF and Print
                </HelpLink>
            </HelpGroup>
        </SettingsPanel>
    );

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
            <PrintSaveButton
                onClick={handlePrint}
                enabled={!!path}
                l10nId="PublishTab.PrintButton"
                imgSrc="./Print.png"
                label="Print..."
            />
            <PrintSaveButton
                onClick={() => {
                    post("publish/pdf/save");
                }}
                enabled={!!path}
                l10nId="PublishTab.SaveButton"
                imgSrc="./Usb.png"
                label="Save PDF..."
            />
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

            <ProgressDialog
                title={progressHeader}
                determinate={true}
                size="small"
                showCancelButton={true}
                onCancel={() => {
                    post("publish/pdf/cancel");
                    closeProgress.current?.();
                    setPath("");
                }}
                setShowDialog={showFunc => (showProgress.current = showFunc)}
                setCloseDialog={closeFunc =>
                    (closeProgress.current = closeFunc)
                }
            />

            <BloomDialog
                open={!!printSettings}
                // eslint-disable-next-line @typescript-eslint/no-empty-function
                onClose={() => {}}
            >
                <DialogMiddle>
                    <Div l10nKey="SamplePrintNotification.PleaseNotice">
                        Please notice the sample printer settings below. Use
                        them as a guide while you set up the printer.
                    </Div>
                    <ApiCheckbox
                        english="I get it. Do not show this again."
                        l10nKey="SamplePrintNotification.IGetIt"
                        apiEndpoint="publish/pdf/dontShowSamplePrint"
                    ></ApiCheckbox>
                </DialogMiddle>
                <DialogBottomButtons>
                    <DialogOkButton
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
            </BloomDialog>
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

// a bit goofy... currently the html loads everything in publishUIBundlejs. So all the publish screens
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
