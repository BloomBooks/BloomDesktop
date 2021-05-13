/** @jsx jsx **/
import { jsx, css } from "@emotion/core";

import { Button, CircularProgress } from "@material-ui/core";
import * as React from "react";
import { useState } from "react";
import { BloomApi } from "../../utils/bloomApi";
import WebSocketManager, {
    IBloomWebSocketProgressEvent
} from "../../utils/WebSocketManager";
import BloomButton from "../bloomButton";
import { ProgressBox } from "./progressBox";
import { kBloomGold, kErrorColor } from "../../bloomMaterialUITheme";
import {
    BloomDialog,
    DialogBottomButtons,
    DialogBottomLeftButtons,
    DialogMiddle,
    DialogTitle,
    IBloomDialogEnvironmentParams,
    useSetupBloomDialog
} from "../BloomDialog/BloomDialog";
import { DialogCloseButton } from "../BloomDialog/commonDialogComponents";

export const ProgressDialog: React.FunctionComponent<{
    title: string;
    titleColor?: string;
    titleIcon?: string;
    titleBackgroundColor?: string;
    // defaults to "never"
    showReportButton?: "always" | "if-error" | "never";

    webSocketContext: string;
    onReadyToReceive?: () => void;
    dialogEnvironment?: IBloomDialogEnvironmentParams;
}> = props => {
    const {
        showDialog,
        closeDialog,
        propsForBloomDialog
    } = useSetupBloomDialog(props.dialogEnvironment);
    const [showButtons, setShowButtons] = useState(false);
    const [sawAnError, setSawAnError] = useState(false);
    const [sawAWarning, setSawAWarning] = useState(false);
    const [messagesForErrorReporting, setMessagesForErrorReporting] = useState(
        ""
    );

    // Start off showing the spinner, then stop when we get a "finished" message.
    const [showSpinner, setShowSpinner] = useState(true);

    // Note that the embedded ProgressBox is also listening to the same stream of events.
    // Here we are just concerned with events that change the state of our buttons, title bar, etc.
    React.useEffect(() => {
        const listener = (e: IBloomWebSocketProgressEvent) => {
            if (e.id === "message") {
                setMessagesForErrorReporting(
                    current => current + "\r\n" + e.message
                );
            }
            if (e.id === "show-buttons") {
                setShowButtons(true);
            }
            if (e.id === "message" && e.progressKind === "Error") {
                setSawAnError(true);
            }
            if (e.id === "message" && e.progressKind === "Warning") {
                setSawAWarning(true);
            }
            if (e.id === "finished") {
                setShowSpinner(false);
            }
        };
        WebSocketManager.addListener(props.webSocketContext, listener);
        // cleanup when this dialog unmounts (since this useEffect will only be called once)
        return () =>
            WebSocketManager.removeListener(props.webSocketContext, listener);
    }, []);

    const buttonForSendingErrorReportIsRelevant =
        props.showReportButton == "always" ||
        (sawAnError && props.showReportButton == "if-error");

    let titleColor = props.titleColor || "black";
    let titleBackground = props.titleBackgroundColor || "transparent";
    if (sawAWarning) {
        titleBackground = kBloomGold;
        titleColor = "black";
    }
    if (sawAnError) {
        titleBackground = kErrorColor;
        titleColor = "white";
    }

    return (
        <BloomDialog {...propsForBloomDialog}>
            <DialogTitle
                title={props.title}
                icon={props.titleIcon}
                backgroundColor={titleBackground}
                color={titleColor}
            >
                {showSpinner && (
                    <CircularProgress
                        css={css`
                            margin-left: auto;
                            margin-top: auto;
                            margin-bottom: auto;
                            color: ${props.titleColor || "black"} !important;
                        `}
                        size={20}
                    />
                )}
            </DialogTitle>
            <DialogMiddle
                css={css`
                    // I don't actually understand why I had do to this, other than than
                    // I'm hopeless at css sizing stuff. See storybook story ProgressDialog: long.
                    overflow-y: unset;
                `}
            >
                <ProgressBox
                    webSocketContext={props.webSocketContext}
                    onReadyToReceive={props.onReadyToReceive}
                    css={css`
                        // If we have omitOuterFrame that means the dialog height is controlled by c#, so let the progress grow to fit it.
                        // Maybe we could have that approach *all* the time?
                        height: ${props.dialogEnvironment?.omitOuterFrame
                            ? "100%"
                            : "400px"};
                        min-width: 540px;
                    `}
                />
            </DialogMiddle>
            <DialogBottomButtons>
                {showButtons ? (
                    <React.Fragment>
                        {buttonForSendingErrorReportIsRelevant && (
                            <DialogBottomLeftButtons>
                                <BloomButton
                                    id="progress-report"
                                    hasText={true}
                                    enabled={true}
                                    //color="primary"
                                    l10nKey="Common.Report"
                                    variant="text"
                                    onClick={() => {
                                        BloomApi.postJson(
                                            "problemReport/showDialog",
                                            {
                                                message: messagesForErrorReporting,
                                                shortMessage: `The user reported a problem from "${props.title}".`
                                            }
                                        );
                                    }}
                                >
                                    Report
                                </BloomButton>
                            </DialogBottomLeftButtons>
                        )}
                        <DialogCloseButton onClick={closeDialog} />
                    </React.Fragment>
                ) : (
                    // This is an invisible Placeholder used to leave room for buttons when the progress is over
                    <Button
                        variant="contained"
                        css={css`
                            visibility: hidden;
                        `}
                    >
                        placeholder
                    </Button>
                )}
            </DialogBottomButtons>
        </BloomDialog>
    );
};
