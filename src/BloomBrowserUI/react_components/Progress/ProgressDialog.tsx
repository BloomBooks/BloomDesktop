/** @jsx jsx **/
import { jsx, css } from "@emotion/core";

import {
    Button,
    CircularProgress,
    Dialog,
    Typography
} from "@material-ui/core";
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
import theme from "../../bloomMaterialUITheme";
import { ThemeProvider } from "@material-ui/styles";
import {
    kDialogPadding,
    kBloomGold,
    kErrorColor
} from "../../bloomMaterialUITheme";

// Root element rendered to progress dialog, using ReactDialog in C#
const kDialogPadding = "10px";

export const ProgressDialog: React.FunctionComponent<{
    title: string;
    webSocketContext: string;
    onReadyToReceive?: () => void;
    titleBackgroundColor?: string;
    titleColor?: string;
    // defaults to "never"
    showReportButton?: "always" | "if-error" | "never";
    titleIcon?: string;
    // false if the caller is wrapping in a winforms dialog already
    wrapInDialog: boolean;
}> = props => {
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
        WebSocketManager.addListener(props.webSocketContext, listener);
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

    const inner = (
        <div
            id="progress-root"
            css={css`
                height: 600px;
                width: 600px;
                display: flex;
                flex-direction: column;
                padding-left: ${kDialogPadding};
                padding-right: ${kDialogPadding};
                padding-bottom: ${kDialogPadding};
            `}
        >
            <div
                css={css`
                    color: ${titleColor};
                    background-color: ${titleBackground};
                    display: flex;
                    padding: ${kDialogPadding};
                    margin-left: -${kDialogPadding};
                    margin-right: -${kDialogPadding};
                    margin-bottom: ${kDialogPadding};
                `}
            >
                {props.titleIcon && (
                    <img
                        src={props.titleIcon}
                        alt="Decorative Icon"
                        css={css`
                            margin-right: ${kDialogPadding};
                        `}
                    />
                )}
                <Typography variant="h4">{props.title}</Typography>
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
            </div>

            <ProgressBox
                clientContext={props.webSocketContext}
                notifyProgressChange={p => (progress.current = p)}
                onReadyToReceive={props.onReadyToReceive}
            />

            <div
                css={css`
                    margin-top: auto; // push to bottom
                    padding-top: ${kDialogPadding}; // leave room between us and the content above us
                    //height: 42px;
                `}
            >
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
                            l10nKey="ReportProblemDialog.Close" // Should we have Common.Close?
                            hasText={true}
                            enabled={true}
                            onClick={() => {
                                BloomApi.post("common/closeReactDialog");
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
            </div>
        </div>
    );
    return (
        <ThemeProvider theme={theme}>
            {props.wrapInDialog ? (
                <Dialog
                    PaperProps={{
                        style: {
                            maxHeight: "100%"
                        }
                    }}
                    maxWidth={"md"}
                    open={true}
                    className="foobar"
                >
                    {inner}
                </Dialog>
            ) : (
                // will be hosted in a winforms dialog
                inner
            )}
        </ThemeProvider>
    );
};
