import * as React from "react";
import { css } from "@emotion/react";
import {
    Dialog,
    DialogActions,
    DialogContent,
    DialogContentText,
    DialogTitle,
    Typography,
} from "@mui/material";
import { post, postString } from "../utils/bloomApi";
import { ThemeProvider, StyledEngineProvider } from "@mui/material/styles";
import "./ProblemDialog.less";
import BloomButton from "../react_components/bloomButton";
import { makeTheme, kindParams } from "./theme";
import { useL10n } from "../react_components/l10nHooks";
import { ProblemKind } from "./ProblemDialog";
import { hookupLinkHandler } from "../utils/linkHandler";
import { kBloomBlue, kFormBackground } from "../utils/colorUtils";

export interface INotifyDialogProps {
    message?: string | null; // The localized message to notify the user about.
    reportLabel?: string | null; // The localized text that goes on the Report button. Omit or pass "" to disable Report button.
    secondaryLabel?: string | null; // The localized text that goes on the secondary action button. Omit or pass "" to disable the secondary action button.
    detailsBoxText?: string | null; // Localized text to go into a grey details box under the message. Omit or pass "" to not show a details box.
    titleOverride?: string | null; // If present, wil be used in place of the dialog title defined for this level in themes.ts
    titleL10nKeyOverride?: string | null; // The L10nKey for the titleOverride, if present.
    themeOverride?: ProblemKind | null; // If present, will be used in place of the dialog theme defined for this level in themes.ts
}

export const NotifyDialog: React.FC<INotifyDialogProps> = (props) => {
    const theme = makeTheme(props.themeOverride || ProblemKind.NonFatal);

    const englishTitle =
        props.titleOverride ?? kindParams[ProblemKind.NonFatal].title;
    const titleKey =
        props.titleL10nKeyOverride ?? kindParams[ProblemKind.NonFatal].l10nKey;
    const localizedDlgTitle = useL10n(englishTitle, titleKey);

    React.useEffect(() => hookupLinkHandler(), []);

    const getDialog = () => {
        return (
            <Dialog
                className="problem-dialog"
                open={true}
                // the behavior of fullWidth/maxWidth is very strange
                //fullWidth={true}
                maxWidth={"md"}
                fullScreen={true}
                onClose={() => post("common/closeReactDialog")}
                css={css`
                    a {
                        color: ${kBloomBlue};
                    }
                `}
            >
                <DialogTitle className={"dialog-title allowSelect"}>
                    <Typography variant="h6">{localizedDlgTitle}</Typography>
                    {/* We moved the X up to the winforms dialog so that it is draggable
                         <Close
                        className="close-in-title"
                        onClick={() => post("common/closeReactDialog")}
                    /> */}
                </DialogTitle>
                <DialogContent className={"dialog-content"}>
                    {/* InnerHTML is used so that we can insert markup like <br> into the message. */}
                    <DialogContentText
                        className="allowSelect"
                        css={css`
                            color: rgba(0, 0, 0, 0.87);
                        `}
                        dangerouslySetInnerHTML={{
                            __html: props.message || "",
                        }}
                    />
                    {props.detailsBoxText && (
                        <DialogContentText
                            css={css`
                                    background-color: ${kFormBackground}; 
                                    color: rgba(0, 0, 0, 0.87);
                                    padding: 10px; 
                                    margin-top: 20px; 
                                    margin-bottom: 20px; 
                                    font-family: courier;
                                }`}
                            dangerouslySetInnerHTML={{
                                __html: props.detailsBoxText || "",
                            }}
                        />
                    )}
                </DialogContent>
                {getDialogActionButtons()}
            </Dialog>
        );
    };

    // Shows the action buttons, as appropriate.
    const getDialogActionButtons = (): JSX.Element => {
        return (
            <div className={"twoColumnHolder"}>
                {/* Note: twoColumnHolder is a flexbox with row-reverse, so the 1st one is the right-most.
                        Using row-reverse allows us to skip putting an empty leftActions, which is theoretically one less thing to render
                    */}
                <DialogActions id="rightColumn">
                    {props.secondaryLabel && (
                        <BloomButton
                            enabled={true}
                            l10nKey=""
                            alreadyLocalized={true}
                            hasText={true}
                            variant="text"
                            onClick={() => {
                                postString(
                                    "common/closeReactDialog",
                                    "closedByAlternateButton", // The close source; informs HtmlErrorReporter what to do
                                );
                            }}
                        >
                            {props.secondaryLabel}
                        </BloomButton>
                    )}
                    {getCloseButton()}
                </DialogActions>
                {props.reportLabel && (
                    <DialogActions id="leftColumn">
                        <BloomButton
                            id="errorReportButton"
                            enabled={true}
                            l10nKey=""
                            alreadyLocalized={true}
                            hasText={true}
                            variant="text"
                            onClick={() => {
                                postString(
                                    "common/closeReactDialog",
                                    "closedByReportButton", // The close source; informs HtmlErrorReporter what to do
                                );
                            }}
                        >
                            {props.reportLabel}
                        </BloomButton>
                    </DialogActions>
                )}
            </div>
        );
    };

    const getCloseButton = (): JSX.Element | null => {
        const buttonLabel =
            props.themeOverride === ProblemKind.Fatal ? "Quit" : "Close";
        const l10nKey =
            props.themeOverride === ProblemKind.Fatal
                ? "ReportProblemDialog.Quit"
                : "Common.Close";
        return (
            <BloomButton
                enabled={true}
                l10nKey={l10nKey}
                hasText={true}
                onClick={() => {
                    post("common/closeReactDialog");
                }}
            >
                {buttonLabel}
            </BloomButton>
        );
    };

    return (
        <StyledEngineProvider injectFirst>
            <ThemeProvider theme={theme}>{getDialog()}</ThemeProvider>
        </StyledEngineProvider>
    );
};
