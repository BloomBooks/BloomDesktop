/** @jsx jsx **/
import { jsx, css } from "@emotion/react";

import { Button, CircularProgress } from "@mui/material";
import * as React from "react";
import { useEffect, useRef, useState } from "react";
import { post, postJson } from "../../utils/bloomApi";
import WebSocketManager, {
    IBloomWebSocketProgressEvent,
    useSubscribeToWebSocketForEvent,
    useSubscribeToWebSocketForObject
} from "../../utils/WebSocketManager";
import BloomButton from "../bloomButton";
import { ProgressBox } from "./progressBox";
import { kBloomGold, kErrorColor } from "../../bloomMaterialUITheme";
import {
    BloomDialog,
    DialogBottomButtons,
    DialogBottomLeftButtons,
    DialogMiddle,
    DialogTitle
} from "../BloomDialog/BloomDialog";
import {
    DialogCancelButton,
    DialogCloseButton
} from "../BloomDialog/commonDialogComponents";
import { WireUpForWinforms } from "../../utils/WireUpWinform";
import {
    IBloomDialogEnvironmentParams,
    useSetupBloomDialog
} from "../BloomDialog/BloomDialogPlumbing";

export interface IProgressDialogProps {
    title: string;
    titleColor?: string;
    titleIcon?: string;
    titleBackgroundColor?: string;
    // defaults to "never"
    showReportButton?: "always" | "if-error" | "never";
    showCancelButton?: boolean;
    onCancel?: () => void;
    onReadyToReceive?: () => void;

    open?: boolean; // Controls whether or not the dialog is open (visible). If undefined, defaults to dialogEnvironment.initiallyOpen.
    dialogEnvironment?: IBloomDialogEnvironmentParams;
    determinate?: boolean;
    size?: "small"; // For a much smaller dialog, when we only expect a few lines.
}

interface IEmbeddedProgressProps extends IProgressDialogProps {
    which: string; // must match props.which to open
}

export const ProgressDialog: React.FunctionComponent<IProgressDialogProps> = props => {
    const {
        showDialog,
        closeDialog,
        propsForBloomDialog
    } = useSetupBloomDialog(props.dialogEnvironment);
    const { onClose, ...propsToPassToBloomDialog } = propsForBloomDialog;

    const isOpen = props.open ?? propsForBloomDialog.open;
    useEffect(() => {
        if (isOpen) {
            showDialog();
        } else {
            closeDialog();
        }
    }, [isOpen, showDialog, closeDialog]); // showDialog and closeDialog should be created using useCallback/useMemo/useRef or something like that to avoid triggering the useEffect unnecessarily.

    const [done, setDone] = useState(false);
    const [sawAnError, setSawAnError] = useState(false);
    const [sawAWarning, setSawAWarning] = useState(false);
    const [sawFatalError, setSawFatalError] = useState(false);
    const [percent, setPercent] = useState(0); // for determinate progress
    const [messagesForErrorReporting, setMessagesForErrorReporting] = useState(
        ""
    );
    const [messages, setMessages] = React.useState<Array<JSX.Element>>([]);

    const [listenerReady, setListenerReady] = useState(false);
    const [progressBoxReady, setProgressBoxReady] = useState(false);
    // TODO: props.onReadyToReceive should theoretically be in dependencies array.
    useEffect(() => {
        if (listenerReady && progressBoxReady && propsForBloomDialog.open) {
            if (props.onReadyToReceive) {
                props.onReadyToReceive();
            } else {
                // Typically we're a top-level dialog; just assume Bloom needs to know.
                post("progress/ready");
            }
        }
    }, [listenerReady, progressBoxReady, propsForBloomDialog.open]);

    // Start off showing the spinner, then stop when we get a "finished" message.
    // (Unless we expect actual percent-done messages, when we show a different thing.)
    const [showSpinner, setShowSpinner] = useState(props.determinate !== true);

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
                setDone(true);
            }
            if (e.id === "percent" && e.percent !== undefined) {
                setPercent(e.percent);
            }
            if (e.id === "message" && e.progressKind === "Error") {
                setSawAnError(true);
            }
            if (e.id === "message" && e.progressKind === "Fatal") {
                setSawAnError(true);
                setSawFatalError(true);
            }
            if (e.id === "message" && e.progressKind === "Warning") {
                setSawAWarning(true);
            }
            if (e.id === "finished") {
                setShowSpinner(false);
            }
        };
        WebSocketManager.addListener("progress", listener);
        setListenerReady(true);
        // cleanup when this dialog unmounts (since this useEffect will only be called once)
        return () => {
            WebSocketManager.removeListener("progress", listener);
        };
    }, []);

    // Any time we are closed and reopened we want to show a new set of messages.
    const everOpened = useRef(false); // we don't need, or want, a change to this to trigger a render.
    useEffect(() => {
        if (propsForBloomDialog.open) {
            everOpened.current = true;
            setPercent(0); // always want to start here
        } else {
            // Once the dialog has been open, the only way this effect runs again is if it
            // it's open state changes. But we don't want this to happen on the initial
            // render, when it is closed and has never been open.
            // (In particular, the post might have unintended consequences. But the other
            // stuff will at least cause useless renders.)
            // (We don't really need to clean it up until it opens again. But cleaning
            // when it closes should free some memory and may help to prevent the old
            // messages flickering into view  when it reopens. Also, the re-renders
            // caused by the changed state are probably less expensive when it is closed.)
            if (everOpened.current) {
                setMessages([]);
                setSawAnError(false);
                setSawAWarning(false);
                setSawFatalError(false);
                setDone(false);
                // not sure why, but if we set percent to zero here, the progress circle
                // moves noticeably backwards before the dialog actually closes.
                setPercent(100);
                post("progress/closed");
            }
        }
    }, [propsForBloomDialog.open]);

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
        <BloomDialog
            {...propsToPassToBloomDialog}
            onClose={(evt, reason) => {
                // Progress dialogs imply some operation is proceeding. It may
                // or may not be possible to cancel it, but we shouldn't just lose
                // the dialog because the user clicked outside it or even pressed Escape.
                if (reason !== "escapeKeyDown" && reason !== "backdropClick") {
                    onClose();
                }
            }}
        >
            {props.determinate && (
                <div
                    css={css`
                        position: absolute;
                        top: 10px;
                        right: 10px;
                    `}
                >
                    <CircularProgress
                        variant="determinate"
                        value={percent}
                        size={40}
                        thickness={5}
                    />
                </div>
            )}
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
                    overflow-y: ${propsForBloomDialog.dialogFrameProvidedExternally
                        ? "auto"
                        : "unset"};
                `}
            >
                <ProgressBox
                    webSocketContext="progress"
                    onReadyToReceive={() => setProgressBoxReady(true)}
                    css={css`
                        // If we have dialogFrameProvidedExternally that means the dialog height is controlled by c#, so let the progress grow to fit it.
                        // Maybe we could have that approach *all* the time?
                        height: ${props.dialogEnvironment
                            ?.dialogFrameProvidedExternally
                            ? "100%"
                            : props.size === "small"
                            ? "80px"
                            : "400px"};
                        min-width: ${props.size === "small"
                            ? "250px"
                            : "540px"};
                    `}
                    // This is utterly bizarre. When not wrapped in a material UI Dialog, ProgressBox happily
                    // keeps track of its own messages. But Dialog repeatedly mounts and unmounts its children,
                    // for no reason I can discover, resulting in loss of their state. So we must keep any state we need
                    // outside the Dialog wrapper.
                    messages={messages}
                    setMessages={setMessages}
                />
            </DialogMiddle>
            <DialogBottomButtons>
                {done ? (
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
                                        postJson("problemReport/showDialog", {
                                            message: messagesForErrorReporting,
                                            shortMessage: `The user reported a problem from "${props.title}".`
                                        });
                                    }}
                                >
                                    Report
                                </BloomButton>
                            </DialogBottomLeftButtons>
                        )}
                        {sawFatalError ? (
                            <BloomButton
                                l10nKey="ReportProblemDialog.Quit"
                                hasText={true}
                                enabled={true}
                                variant="contained"
                                temporarilyDisableI18nWarning={true} // only used in TC context so far
                                onClick={closeDialog}
                            >
                                Quit
                            </BloomButton>
                        ) : (
                            <DialogCloseButton onClick={closeDialog} />
                        )}
                    </React.Fragment>
                ) : // if we're not done, show the cancel button if that was called for...
                props.showCancelButton ? (
                    <DialogCancelButton
                        onClick_DEPRECATED={() => {
                            if (props.onCancel) {
                                props.onCancel();
                            } else {
                                post("progress/cancel");
                            }
                        }}
                    />
                ) : (
                    // ...otherwise show a placeholder to leave room for buttons when the progress is over
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

// Simply render one of these, with no props, at the top level of any document where the
// C# code (or possibly one day JS code??) might want to show a progress dialog. Showing the
// dialog and sending stuff to it is all managed by websocket events, and events initiated
// by buttons in the dialog result in posts. It occupies no space and is invisible until
// an event tells it to show up. See C# BrowserProgressDialog.
export const EmbeddedProgressDialog: React.FunctionComponent<{
    id: string;
}> = props => {
    const [progressProps, setProgressProps] = useState<IEmbeddedProgressProps>({
        which: "",
        // just lets us know something is wrong if it shows up; a real title should
        // be supplied by the code that causes it to become visible.
        title: "This should not be seen",
        dialogEnvironment: {
            initiallyOpen: false,
            dialogFrameProvidedExternally: false
        }
    });
    const openProgress = (args: IEmbeddedProgressProps) => {
        if (args.which !== props.id) {
            return; // message for another progress dialog, typically in another browser instance
        }
        // Note, we can't get the dialog shown here by setting props to
        // something with dialogEnvironment having initiallyOpen true;
        // that initial value gets captured on the first render. We have to use
        // the "open" prop for controlling that instead.
        // args are sent from the C# code that wants to open the dialog.
        setProgressProps({
            ...args,
            open: true
        });
    };
    useSubscribeToWebSocketForObject(
        "progress",
        "open-progress",
        (args: IEmbeddedProgressProps) => openProgress(args)
    );
    useSubscribeToWebSocketForEvent("progress", "close-progress", () =>
        setProgressProps({
            ...progressProps,
            open: false
        })
    );
    return (
        <ProgressDialog
            {...progressProps}
            onReadyToReceive={() => post("progress/ready")}
        />
    );
};

WireUpForWinforms(ProgressDialog);
