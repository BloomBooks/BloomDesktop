/** @jsx jsx **/
import { jsx, css } from "@emotion/core";

import { Button, CircularProgress, Typography } from "@material-ui/core";
import * as React from "react";
import { useRef, useState } from "react";
import { BloomApi } from "../../utils/bloomApi";
import WebSocketManager, {
    IBloomWebSocketProgressEvent
} from "../../utils/WebSocketManager";
import BloomButton from "../bloomButton";
import { Link } from "../link";
import { ProgressBox } from "./progressBox";
import "./ProgressDialog.less";
import { kBloomGold, kErrorColor } from "../../bloomMaterialUITheme";
import {
    BloomDialog,
    DialogBottom,
    DialogMiddle,
    DialogTitle
} from "../BloomDialog/BloomDialog";

// Root element rendered to progress dialog, using ReactDialog in C#
const kDialogPadding = "10px";

export const ProgressDialog: React.FunctionComponent<{
    title: string;
    titleColor?: string;
    titleIcon?: string;
    titleBackgroundColor?: string;

    // true if the caller is wrapping in a winforms dialog already
    omitOuterFrame: boolean;
    // defaults to "never"
    showReportButton?: "always" | "if-error" | "never";

    webSocketContext: string;
    onReadyToReceive?: () => void;
}> = props => {
    const [open, setOpen] = useState(true);
    const [showButtons, setShowButtons] = useState(false);
    const [sawAnError, setSawAnError] = useState(false);
    const [sawAWarning, setSawAWarning] = useState(false);

    // Start off showing the spinner, then stop when we get a "finished" message.
    const [showSpinner, setShowSpinner] = useState(true);

    const progress = useRef("");

    // Note that the embedded ProgressBox is also listening to the same stream of events.
    // Here we are just concerned with events that change the state of our buttons, title bar, etc.
    React.useEffect(() => {
        const listener = (e: IBloomWebSocketProgressEvent) => {
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
        WebSocketManager.addListener(
            props.webSocketContext,
            listener,
            "dialog"
        );
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
        <BloomDialog open={open} omitOuterFrame={props.omitOuterFrame}>
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
                        className={"circle-progress"}
                    />
                )}
            </DialogTitle>
            <DialogMiddle>
                <ProgressBox
                    webSocketContext={props.webSocketContext}
                    // review: I (JH) am not clear what this is here for? Presumably someone had trouble with the whole state/refresh thing?
                    //notifyProgressChange={p => (progress.current = p)}
                    onReadyToReceive={props.onReadyToReceive}
                    css={css`
                        height: 400px;
                        min-width: 540px;
                    `}
                />
            </DialogMiddle>
            <DialogBottom>
                {showButtons ? (
                    <React.Fragment>
                        {buttonForSendingErrorReportIsRelevant && (
                            <Link
                                id="progress-report"
                                color="primary"
                                l10nKey="Common.Report"
                                onClick={() => {
                                    BloomApi.postJson(
                                        "problemReport/showDialog",
                                        {
                                            message: progress.current,
                                            // Enhance: this will need to be configurable if we use this
                                            // dialog for something else...maybe by url param?
                                            shortMessage:
                                                "The user reported a problem in Team Collection Sync"
                                        }
                                    );
                                }}
                            >
                                REPORT
                            </Link>
                        )}
                        <BloomButton
                            id="close-button"
                            l10nKey="Common.Close"
                            hasText={true}
                            enabled={true}
                            onClick={() => {
                                if (props.omitOuterFrame)
                                    BloomApi.post("common/closeReactDialog");
                                else setOpen(false);
                            }}
                            css={css`
                                float: right;
                            `}
                        >
                            Close
                        </BloomButton>
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
            </DialogBottom>
        </BloomDialog>
    );
};

/*  <Dialog
                    PaperProps={{
                        style: {
                            maxHeight: "100%"
                        }
                    }}
                    maxWidth={"md"}
                    open={true}
                    className="foobar"
                > */
