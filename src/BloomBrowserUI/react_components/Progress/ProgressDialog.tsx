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

    open: boolean; // Controls whether or not the dialog is open (visible). (Theoretically, initial value should match dialogEnvironment.initiallyOpen, but not strictly necessary)
    onClose: () => void; // Callback fired when the component requests to be closed.
    onCancel?: () => void;
    onReadyToReceive?: () => void;

    dialogEnvironment?: IBloomDialogEnvironmentParams;
    determinate?: boolean;
    size?: "small"; // For a much smaller dialog, when we only expect a few lines.
}

export const ProgressDialog: React.FunctionComponent<IProgressDialogProps> = props => {
    const {
        showDialog,
        closeDialog,
        propsForBloomDialog
    } = useSetupBloomDialog(props.dialogEnvironment);
    const {
        onClose: _, // Remove it from propsToPassToBloomDialog, but we don't actually want this variable (causes confusion with props.onClose)
        ...propsToPassToBloomDialog
    } = propsForBloomDialog;

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

    // The parent's mechanism to control opening/closing the dialog via props.open
    // (Same model as MaterialUI's Dialog or Modal components)
    // All our current non-Storybook parents want to have at least partial (if not full) influence
    // over when the dialog is opened/closed anyway.  If the parent wants at least some influence over open/close,
    // it's a lot more idiomatic in React for the state to be in the parent.
    //
    // Under the hood, this is managed by useSetupBloomDialog
    // To hide some complexity / boilerplate from callers,
    // this component manages the useSetupBloomDialog stuff,
    // although one minor side effect of this is that useSetupBloomDialog has a sort of duplicate copy of the state
    // (Exposed via propsForBloomDialog.open)
    //
    // This useEffect is responsible for keeping the two in sync.
    useEffect(() => {
        if (props.open) {
            showDialog(); // Under the hood, modifies propsForBloomDialog.open
        } else {
            closeDialog(); // Under the hood, modifies propsForBloomDialog.open
        }
    }, [props.open, showDialog, closeDialog]); // showDialog and closeDialog should be created using stable references (e.g. useCallback) to avoid triggering the useEffect unnecessarily.

    // TODO: props.onReadyToReceive should theoretically be in dependencies array.
    useEffect(() => {
        // Note: I have this to check for propsForBloomDialog.open rather than props.open,
        // because I (pre-emptively) worry that props.onReadyToReceive will fire while the underlying
        // propsForBloomDialog is still transitioning from closed to open, and maybe it won't actually be ready to receive.
        // So wait for propsForBloomDialog.open to become true, so that we're really sure everything's ready
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
        if (props.open) {
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
    }, [props.open]);

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
                    props.onClose();
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
                                onClick={props.onClose}
                            >
                                Quit
                            </BloomButton>
                        ) : (
                            <DialogCloseButton onClick={props.onClose} />
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

// Same as IProgressDialogProps, except:
//   * Makes dialogEnvironment required
//   * Removes the open prop
type IWinFormsProgressDialogProps = Omit<
    IProgressDialogProps &
        Required<Pick<IProgressDialogProps, "dialogEnvironment">>,
    "open"
>;

/**
 * Designed to be used in conjunction with WireUpForWinforms, which sets initiallyOpen to true.
 * The open state of ProgressDialog is initialized from {dialogEnvironment.initiallyOpen},
 * but then subsequently managed by this component
 */
export const WinFormsProgressDialog: React.FunctionComponent<IWinFormsProgressDialogProps> = props => {
    const [isOpen, setIsOpen] = useState(props.dialogEnvironment.initiallyOpen);
    return (
        <ProgressDialog
            {...props}
            open={isOpen}
            onClose={() => {
                setIsOpen(false);
            }}
        />
    );
};

/**
 * The schema for the websocket data that EmbeddedProgressDialog is expecting
 * Should stay in sync with whatever props JSON that BrowserProgressDialog.cs in API land might generate
 */
interface IEmbeddedProgressDialogConfig {
    which: string; // must match props.which to open
    title: string;
    titleColor?: string;
    titleIcon?: string;
    titleBackgroundColor?: string;
    // defaults means "never"
    showReportButton?: "always" | "if-error" | "never";
    showCancelButton?: boolean;
}

// Simply render one of these, with no props, at the top level of any document where the
// C# code (or possibly one day JS code??) might want to show a progress dialog. Showing the
// dialog and sending stuff to it is all managed by websocket events, and events initiated
// by buttons in the dialog result in posts. It occupies no space and is invisible until
// an event tells it to show up. See C# BrowserProgressDialog.
export const EmbeddedProgressDialog: React.FunctionComponent<{
    id: string;
}> = props => {
    const [isOpen, setIsOpen] = useState(false);
    const [progressConfig, setProgressConfig] = useState<
        IEmbeddedProgressDialogConfig
    >({
        which: "",
        // just lets us know something is wrong if it shows up; a real title should
        // be supplied by the code that causes it to become visible.
        title: "This should not be seen"
    });
    const openProgress = (args: IEmbeddedProgressDialogConfig) => {
        if (args.which !== props.id) {
            return; // message for another progress dialog, typically in another browser instance
        }
        // Note, we can't get the dialog shown here by setting props to
        // something with dialogEnvironment having initiallyOpen true;
        // that initial value gets captured on the first render. We have to use
        // the "open" prop for controlling that instead.
        // args are sent from the C# code that wants to open the dialog.
        setProgressConfig({
            ...args
        });
        setIsOpen(true);
    };
    useSubscribeToWebSocketForObject(
        "progress",
        "open-progress",
        (args: IEmbeddedProgressDialogConfig) => openProgress(args)
    );
    useSubscribeToWebSocketForEvent("progress", "close-progress", () => {
        // Handles "close" initiated from over the websocket
        setIsOpen(false);
    });

    return (
        <ProgressDialog
            {...progressConfig}
            open={isOpen}
            onClose={() => {
                // Handles "close" initiated from React-land (e.g. clicking on a button)
                setIsOpen(false);
            }}
            onReadyToReceive={() => post("progress/ready")}
            dialogEnvironment={{
                initiallyOpen: false,
                dialogFrameProvidedExternally: false
            }}
        />
    );
};

WireUpForWinforms(WinFormsProgressDialog);
