import { css } from "@emotion/react";
import * as React from "react";
import { useState, useRef } from "react";

import {
    PreviewPanel,
    HelpGroup,
    SettingsPanel,
} from "../commonPublish/PublishScreenBaseComponents";
import { PDFPrintFeaturesGroup } from "./PDFPrintFeaturesGroup";
import PublishScreenTemplate from "../commonPublish/PublishScreenTemplate";
import { ThemeProvider, StyledEngineProvider } from "@mui/material/styles";
import { darkTheme } from "../../bloomMaterialUITheme";
import { useL10n } from "../../react_components/l10nHooks";
import Button from "@mui/material/Button";
import { post, useApiBoolean } from "../../utils/bloomApi";
import { ProgressDialog } from "../../react_components/Progress/ProgressDialog";
import HelpLink from "../../react_components/helpLink";
import { RequiresSubscriptionDialog } from "../../react_components/requiresSubscription";
import { useMountEffect } from "../../utils/useMountEffect";

// The common behavior of the Print and Save buttons.
// There is probably some way to get this look out of BloomButton,
// but it seems more trouble than it's worth.
const PrintSaveButton: React.FunctionComponent<{
    onClick: () => void;
    label: string;
    l10nId: string;
    imgSrc: string;
    enabled: boolean;
}> = (props) => {
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
    // We need a ref to the real DOM object of the iframe that holds the print preview
    // so we can actually tell it to print.
    const iframeRef = useRef<HTMLIFrameElement>(null);

    const progressHeader = useL10n("Progress", "Common.Progress");

    const [allowPublishing, _setAllowPublishing] = useApiBoolean(
        "publish/pdf/licenseOK",
        false,
    );
    const [isPlaygroundBook, setIsPlaygroundBook] = useApiBoolean(
        "publish/isPlaygroundBook",
        true,
    );

    const [isProgressDialogOpen, setIsProgressDialogOpen] = useState(false);

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
                    // Print instructions from an earlier print are for the old mode's PDF.
                    post("publish/pdf/closePrintInstructions");
                    setIsProgressDialogOpen(allowPublishing);
                    setBookletMode(newMode);
                }}
                onGotPdf={(path) => {
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

    // If the user leaves this screen while the print instructions window is open, close it.
    useMountEffect(() => () => post("publish/pdf/closePrintInstructions"));

    const printNow = () => {
        if (iframeRef.current) {
            // In WebView2 this returns at once, while the print dialog is still open, and nothing
            // (not even afterprint) tells us when the dialog closes. So the print instructions
            // window stays until the user closes it, prints again, or leaves this screen.
            iframeRef.current.contentWindow?.print();
            // Unfortunately, we have no way to know whether the user really
            // printed, or canceled in the browser print dialog.
            post("publish/pdf/printAnalytics");
        }
    };

    const handlePrint = () => {
        if (bookletMode === "cover" || bookletMode === "pages") {
            // The print dialog covers this whole page, so Bloom shows the localized
            // settings for the current page size in a window of its own.
            post("publish/pdf/showPrintInstructions", printNow);
        } else {
            printNow();
        }
    };

    const rightSideControls = (
        <React.Fragment>
            <PrintSaveButton
                onClick={handlePrint}
                enabled={!!path && !isPlaygroundBook}
                l10nId="PublishTab.PrintButton"
                imgSrc="/bloom/publish/PDFPrintPublish/Print.png"
                label="Print..."
            />
            <PrintSaveButton
                onClick={() => {
                    post("publish/pdf/save");
                }}
                enabled={!!path && !isPlaygroundBook}
                l10nId="PublishTab.SaveButton"
                imgSrc="/bloom/publish/PDFPrintPublish/Usb.png"
                label="Save PDF..."
            />
        </React.Fragment>
    );

    return (
        <React.Fragment>
            <div
                css={css`
                    height: 100%;
                `}
            >
                <PublishScreenTemplate
                    bannerTitleEnglish="Publish to PDF &amp; Print"
                    bannerTitleL10nId="PublishTab.PdfPrint.BannerTitle"
                    bannerRightSideControls={rightSideControls}
                    optionsPanelContents={optionsPanel}
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
            <RequiresSubscriptionDialog />
        </React.Fragment>
    );
};
