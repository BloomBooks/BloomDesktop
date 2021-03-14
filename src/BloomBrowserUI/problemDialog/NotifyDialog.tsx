import * as React from "react";
import {
    Dialog,
    DialogActions,
    DialogContent,
    DialogContentText,
    DialogTitle,
    Typography
} from "@material-ui/core";
import { BloomApi } from "../utils/bloomApi";
import { ThemeProvider } from "@material-ui/styles";
import "./ProblemDialog.less";
import BloomButton from "../react_components/bloomButton";
import { makeTheme, kindParams } from "./theme";
import { useL10n } from "../react_components/l10nHooks";
import { ProblemKind } from "./ProblemDialog";
import { formatForHtml } from "../utils/encodingUtils";

export const NotifyDialog: React.FunctionComponent<{
    reportLabel: string | null;
    secondaryLabel: string | null;
    messageParam: string | null;
}> = props => {
    const theme = makeTheme(ProblemKind.NonFatal);

    const englishTitle = kindParams[ProblemKind.NonFatal].title;
    const titleKey = kindParams[ProblemKind.NonFatal].l10nKey;
    const localizedDlgTitle = useL10n(englishTitle, titleKey);

    const [message] = BloomApi.useApiString(
        "problemReport/notify/message",
        props.messageParam || "",
        () => {
            return props.messageParam === null;
        }
    );

    const getDialog = () => {
        return (
            <Dialog
                className="problem-dialog"
                open={true}
                // the behavior of fullWidth/maxWidth is very strange
                //fullWidth={true}
                maxWidth={"md"}
                fullScreen={true}
                onClose={() => BloomApi.post("common/closeReactDialog")}
            >
                {/* The whole disableTypography and Typography thing gets around Material-ui putting the
                    Close icon inside of the title's Typography element, where we don't have control over its CSS. */}
                <DialogTitle
                    className={"dialog-title allowSelect"}
                    disableTypography={true}
                >
                    <Typography variant="h6">{localizedDlgTitle}</Typography>
                    {/* We moved the X up to the winforms dialog so that it is draggable
                         <Close
                        className="close-in-title"
                        onClick={() => BloomApi.post("common/closeReactDialog")}
                    /> */}
                </DialogTitle>
                <DialogContent className={"dialog-content"}>
                    {/* InnerHTML is used so that we can insert <br> entities into the message. */}
                    <DialogContentText
                        className="allowSelect"
                        dangerouslySetInnerHTML={{
                            __html: formatForHtml(message)
                        }}
                    ></DialogContentText>
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
                                BloomApi.postString(
                                    "common/closeReactDialog",
                                    "closedByAlternateButton" // The value is the close source
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
                                BloomApi.postString(
                                    "common/closeReactDialog",
                                    "closedByReportButton" // The value is the close source
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
        return (
            <BloomButton
                enabled={true}
                l10nKey={"ReportProblemDialog.Close"}
                hasText={true}
                onClick={() => {
                    BloomApi.post("common/closeReactDialog");
                }}
            >
                Close
            </BloomButton>
        );
    };

    return <ThemeProvider theme={theme}>{getDialog()}</ThemeProvider>;
};
