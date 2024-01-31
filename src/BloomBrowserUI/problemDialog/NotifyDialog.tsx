import * as React from "react";
import { css } from "@emotion/react";
import {
    Dialog,
    DialogActions,
    DialogContent,
    DialogContentText,
    DialogTitle,
    Typography
} from "@mui/material";
import { post, postString } from "../utils/bloomApi";
import { ThemeProvider, StyledEngineProvider } from "@mui/material/styles";
import "./ProblemDialog.less";
import BloomButton from "../react_components/bloomButton";
import { makeTheme, kindParams } from "./theme";
import { useL10n } from "../react_components/l10nHooks";
import { ProblemKind } from "./ProblemDialog";
import { hookupLinkHandler } from "../utils/linkHandler";
import { kFormBackground } from "../utils/colorUtils";

export const NotifyDialog: React.FunctionComponent<{
    reportLabel?: string | null;
    secondaryLabel?: string | null;
    message: string | null;
    detailsBoxText?: string | null;
    level?: ProblemKind | null;
    titleOverride?: string | null;
    titleL10nKeyOverride?: string | null;
}> = props => {
    const kind = props.level || ProblemKind.NonFatal;

    const theme = makeTheme(kind);

    const englishTitle = props.titleOverride ?? kindParams[kind].title;
    const titleKey = props.titleL10nKeyOverride ?? kindParams[kind].l10nKey;
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
                            dangerouslySetInnerHTML={{
                                __html: props.message || ""
                            }}
                        />
                        {props.detailsBoxText && 
                            <DialogContentText
                                css={css`background-color: ${kFormBackground}; padding: 10px; margin-top: 20px; margin-bottom: 20px;`}
                                dangerouslySetInnerHTML={{
                                    __html: props.detailsBoxText || ""
                                }}
                            />
                        }
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
                                    "closedByAlternateButton" // The close source; informs HtmlErrorReporter what to do
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
                                    "closedByReportButton" // The close source; informs HtmlErrorReporter what to do
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
        const buttonLabel = kind === ProblemKind.Fatal ? "Quit" : "Close";
        const l10nKey =
                kind === ProblemKind.Fatal
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
