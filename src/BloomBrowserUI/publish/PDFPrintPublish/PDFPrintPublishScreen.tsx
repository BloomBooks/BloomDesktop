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
import { get, post } from "../../utils/bloomApi";
import { ProgressDialog } from "../../react_components/Progress/ProgressDialog";
import HelpLink from "../../react_components/helpLink";
import { RequiresBloomEnterpriseDialog } from "../../react_components/requiresBloomEnterprise";
import { kBloomBlue } from "../../bloomMaterialUITheme";
import InfoOutlinedIcon from "@mui/icons-material/InfoOutlined";
import CloseIcon from "@mui/icons-material/Close";

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
    const [bookletMode, setBookletMode] = useState("");
    const [helpVisible, setHelpVisible] = useState(false);
    const [bookletPrintHelp, setBookletPrintHelp] = useState([""]);
    const [bookletPrintNote, setBookletPrintNote] = useState("");
    // We need a ref to the real DOM object of the iframe that holds the print preview
    // so we can actually tell it to print.
    const iframeRef = useRef<HTMLIFrameElement>(null);

    const progressHeader = useL10n("Progress", "Common.Progress");

    const [isProgressDialogOpen, setIsProgressDialogOpen] = useState(false);

    const settingsHelp = bookletPrintHelp?.map((help, index) => (
        <p
            key={index}
            css={css`
                margin-block-start: 0px;
                margin-block-end: 2px;
            `}
        >
            {help}
        </p>
    ));
    const settingsNote = bookletPrintNote ? (
        <p
            css={css`
                margin-block-start: 0px;
                margin-block-end: 2px;
                column-span: all;
            `}
        >
            {bookletPrintNote}
        </p>
    ) : (
        ""
    );

    const helpHeader = useL10n(
        "Here are the settings you need for printing this booklet:",
        "PublishTab.PDF.Booklet.HelpHeader"
    );
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
                        {path && (
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
                        )}
                    </ThemeProvider>
                </StyledEngineProvider>
            </PreviewPanel>
        </React.Fragment>
    );

    const optionsPanel = (
        <SettingsPanel>
            <PDFPrintFeaturesGroup
                onChange={(newMode: string) => {
                    setIsProgressDialogOpen(true);
                    setBookletMode(newMode);
                }}
                onGotPdf={path => {
                    setPath(path);
                    setIsProgressDialogOpen(false);
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
                    About PDF & Print
                </HelpLink>
            </HelpGroup>
        </SettingsPanel>
    );

    const printNow = () => {
        if (iframeRef.current) {
            setHelpVisible(bookletMode === "cover" || bookletMode === "pages");
            iframeRef.current.contentWindow?.print();
            // Unfortunately, we have no way to know whether the user really
            // printed, or canceled in the browser print dialog.
            post("publish/pdf/printAnalytics");
        }
    };

    const handlePrint = () => {
        get("publish/pdf/printSettingsHelp", response => {
            // This causes the localized instructions to be displayed for
            // the current page size.
            setBookletPrintHelp(response.data.helps);
            setBookletPrintNote(response.data.note);
            printNow();
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

    const bottomBanner = (
        <div
            css={css`
                display: ${helpVisible ? "block" : "none"};
                background-color: ${kBloomBlue};
                color: white;
                position: sticky;
                border-radius: 4px;
            `}
        >
            <div
                css={css`
                    font-weight: 600;
                    padding-top: 5px;
                    padding-left: 15px;
                `}
            >
                <InfoOutlinedIcon
                    css={css`
                        padding-right: 5px;
                        position: relative;
                        top: 4px;
                    `}
                />
                {helpHeader}
            </div>
            <CloseIcon
                css={css`
                    position: absolute;
                    top: 5px;
                    right: 5px;
                `}
                onClick={() => setHelpVisible(false)}
            />
            <div
                css={css`
                    column-count: 2;
                    padding-left: 20px;
                    padding-top: 10px;
                    padding-bottom: 10px;
                `}
            >
                {settingsHelp}
                {settingsNote}
            </div>
        </div>
    );

    return (
        <React.Fragment>
            <div
                onClick={() => setHelpVisible(false)}
                css={css`
                    height: 100%;
                `}
            >
                <PublishScreenTemplate
                    bannerTitleEnglish="Publish to PDF &amp; Print"
                    bannerTitleL10nId="PublishTab.PdfPrint.BannerTitle"
                    bannerRightSideControls={rightSideControls}
                    optionsPanelContents={optionsPanel}
                    bottomBanner={bottomBanner}
                >
                    {mainPanel}
                </PublishScreenTemplate>
            </div>

            <ProgressDialog
                title={progressHeader}
                determinate={true}
                size="small"
                showCancelButton={true}
                onCancel={() => {
                    post("publish/pdf/cancel");
                    setIsProgressDialogOpen(false);
                    setPath("");
                }}
                open={isProgressDialogOpen}
                onClose={() => {
                    setIsProgressDialogOpen(false);
                }}
            />
            <RequiresBloomEnterpriseDialog />
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
