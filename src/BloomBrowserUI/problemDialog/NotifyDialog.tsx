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
import { makeStyles, ThemeProvider } from "@material-ui/styles";
import "./ProblemDialog.less";
import BloomButton from "../react_components/bloomButton";
import { makeTheme, kindParams } from "./theme";
import { useL10n } from "../react_components/l10nHooks";
import { ProblemKind } from "./ProblemDialog";
import { formatForHtml } from "../utils/encodingUtils";

const kEdgePadding = "24px";
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
                onClose={() => BloomApi.post("dialog/close")}
            >
                {/* The whole disableTypography and Typography thing gets around Material-ui putting the
                    Close icon inside of the title's Typography element, where we don't have control over its CSS. */}
                <DialogTitle
                    className={`dialog-title allowSelect ${
                        useTitleStyle().root
                    }`}
                    disableTypography={true}
                >
                    <Typography variant="h6">{localizedDlgTitle}</Typography>
                    {/* We moved the X up to the winforms dialog so that it is draggable
                         <Close
                        className="close-in-title"
                        onClick={() => BloomApi.post("dialog/close")}
                    /> */}
                </DialogTitle>
                <DialogContent className={useContentStyle().root}>
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

    const useTitleStyle = makeStyles({
        root: {
            padding: `6px ${kEdgePadding}`
        }
    });

    const useContentStyle = makeStyles({
        root: {
            padding: `27px ${kEdgePadding}`
        }
    });

    // Shows the action buttons, as appropriate.
    const getDialogActionButtons = (): JSX.Element => {
        const useTwoColumnHolderStyle = makeStyles({
            root: {
                display: "flex",
                // Use space-between so that when we have both #left and #right, they are split out to the outside edges
                justifyContent: "space-between",
                // Use row-reverse instead of reverse so that when not reportable, the only DialogActions group
                // will be placed at the start (that is, the right). In standard "row", the start is the left
                // but that's not where we want it to go.
                flexDirection: "row-reverse",

                paddingLeft: kEdgePadding,
                paddingRight: kEdgePadding
            }
        });
        const useRightStyle = makeStyles({
            // So that the right edge of the "Close" button will line up with the right edge of the ContentText
            root: {
                paddingRight: "0px"
            }
        });

        const useLeftStyle = makeStyles({
            // So that the left edge of the "Report" button will line up with the left edge of the ContentText
            root: {
                paddingLeft: "0px"
            },
            // So that the left edge of the "Report" text will line up with the left edge of the button
            text: {
                paddingLeft: "0px"
            }
        });

        return (
            <div className={useTwoColumnHolderStyle().root}>
                {/* Note: twoColumnHolder is a flexbox with row-reverse, so the 1st one is the right-most.
                        Using row-reverse allows us to skip putting an empty leftActions, which is theoretically one less thing to render
                    */}
                <DialogActions className={useRightStyle().root}>
                    {props.secondaryLabel && (
                        <BloomButton
                            enabled={true}
                            l10nKey=""
                            alreadyLocalized={true}
                            hasText={true}
                            variant="text"
                            onClick={() => {
                                BloomApi.postData("dialog/close", {
                                    source: "alternate"
                                });
                            }}
                        >
                            {props.secondaryLabel}
                        </BloomButton>
                    )}
                    {getCloseButton()}
                </DialogActions>
                {props.reportLabel && (
                    <DialogActions className={useLeftStyle().root}>
                        <BloomButton
                            className={`errorReportButton ${
                                useLeftStyle().text
                            }`}
                            enabled={true}
                            l10nKey=""
                            alreadyLocalized={true}
                            hasText={true}
                            variant="text"
                            onClick={() => {
                                BloomApi.postData("dialog/close", {
                                    source: "report"
                                });
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
                    BloomApi.post("dialog/close");
                }}
            >
                Close
            </BloomButton>
        );
    };

    return <ThemeProvider theme={theme}>{getDialog()}</ThemeProvider>;
};
