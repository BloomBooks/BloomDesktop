import { css } from "@emotion/react";

import { LinearProgress } from "@mui/material";
import * as React from "react";
import { useEffect, useRef, useState } from "react";
import { post, postJson } from "../../utils/bloomApi";
import WebSocketManager, {
    IBloomWebSocketProgressEvent,
    useSubscribeToWebSocketForEvent,
    useSubscribeToWebSocketForObject,
} from "../../utils/WebSocketManager";
import BloomButton from "../bloomButton";
import {
    kBloomBlue,
    kBloomGold,
    kDialogPadding,
    kErrorColor,
} from "../../bloomMaterialUITheme";
import {
    BloomDialog,
    DialogBottomButtons,
    DialogBottomLeftButtons,
    DialogMiddle,
    DialogTitle,
} from "../BloomDialog/BloomDialog";
import { DialogCloseButton } from "../BloomDialog/commonDialogComponents";
import {
    IBloomDialogEnvironmentParams,
    useSetupBloomDialog,
} from "../BloomDialog/BloomDialogPlumbing";
import { useMountEffect } from "../../utils/useMountEffect";

// A progress dialog for a job the user did not ask to watch: one sentence saying what Bloom is
// busy with, and a percent-done bar above it. Nothing else -- no scrolling log.
//
// This is deliberately a separate component from ProgressDialog rather than another set of
// options on it. ProgressDialog exists to show the running commentary of a job the user is
// meant to read (a spreadsheet import, a Team Collection operation): its whole middle is a
// ProgressBox, and the switches for hiding the log, shrinking the box and reserving space for
// buttons had already accumulated to the point where "the small quiet one" was easier to read
// as its own file than as yet another branch through that one. See BL-16893.

// Names the single instance of EmbeddedSimpleProgressDialog that App.tsx renders. C# opens it
// by this name (BookProcessor.kUpdateBookProgressDialogId); keep the two in step.
export const kUpdateBookProgressDialogId = "updateBook";

export interface ISimpleProgressDialogProps {
    title: string;
    titleColor?: string;
    titleBackgroundColor?: string;
    // The single sentence shown under the bar, e.g. "Please wait while Bloom does some
    // housekeeping on your book...". Already localized: like the progress messages themselves,
    // it is composed on the C# side.
    message: string;

    open: boolean; // Controls whether the dialog is visible.
    onClose: () => void; // Fired when the dialog asks to be closed.

    dialogEnvironment?: IBloomDialogEnvironmentParams;
}

// A message worth interrupting the "please wait" for: something went wrong, or nearly did.
interface IProblem {
    text: string;
    kind: "Error" | "Fatal" | "Warning";
}

export const SimpleProgressDialog: React.FunctionComponent<
    ISimpleProgressDialogProps
> = (props) => {
    const { showDialog, closeDialog, propsForBloomDialog } =
        useSetupBloomDialog(props.dialogEnvironment);
    const {
        onClose: _, // remove it, so it can't be confused with props.onClose
        ...propsToPassToBloomDialog
    } = propsForBloomDialog;

    const [percent, setPercent] = useState(0);
    const [problems, setProblems] = useState<IProblem[]>([]);
    const [done, setDone] = useState(false);
    const [socketReady, setSocketReady] = useState(false);
    // Every message of the run, shown or not, for the Report button to send.
    const messagesForErrorReporting = useRef<string[]>([]);

    const sawFatalError = problems.some((p) => p.kind === "Fatal");
    const sawAnError = problems.some(
        (p) => p.kind === "Error" || p.kind === "Fatal",
    );
    const sawAWarning = problems.some((p) => p.kind === "Warning");

    // True exactly while this dialog is the one showing. The listener below consults it because
    // "progress" is a shared channel: every progress dialog in the document, and in Bloom's other
    // browsers, hears every event on it. Without this we would quietly record somebody else's
    // percent, errors and show-buttons while closed, and then open showing them -- a full bar, a
    // red title, and a live Close button, before our own job had done anything.
    const isShowing = useRef(false);

    // This effect is required because the websocket is an external subscription outside React,
    // and it must be listening before C# starts sending (hence the progress/ready handshake below).
    useMountEffect(() => {
        const listener = (e: IBloomWebSocketProgressEvent) => {
            if (!isShowing.current) {
                return; // not our job; see isShowing
            }
            if (e.id === "percent" && e.percent !== undefined) {
                setPercent(e.percent);
            }
            if (e.id === "message") {
                // Everything goes into the problem report, so that a user who presses Report
                // sends the whole story rather than only the part we chose to show.
                messagesForErrorReporting.current.push(e.message ?? "");
                // On screen, though, ordinary progress messages are deliberately ignored:
                // props.message is the whole story we want to tell. A warning or error is news.
                if (
                    e.progressKind === "Error" ||
                    e.progressKind === "Fatal" ||
                    e.progressKind === "Warning"
                ) {
                    setProblems((current) => [
                        ...current,
                        { text: e.message ?? "", kind: e.progressKind! },
                    ]);
                }
            }
            if (e.id === "show-buttons") {
                setDone(true);
            }
        };
        WebSocketManager.addListener("progress", listener);
        WebSocketManager.notifyReady("progress", () => setSocketReady(true));
        return () => {
            WebSocketManager.removeListener("progress", listener);
        };
    });

    // Keep the dialog-plumbing's idea of open/closed in sync with our controlling prop, the same
    // way ProgressDialog does. This effect is required because showDialog/closeDialog drive state
    // that lives inside useSetupBloomDialog rather than here.
    useEffect(() => {
        if (props.open) {
            showDialog();
        } else {
            closeDialog();
        }
    }, [props.open, showDialog, closeDialog]);

    // Tell C# when we are ready to receive progress, and when we have gone away. The worker that
    // does the job waits for progress/ready before it starts, so that nothing is sent into the void
    // -- hence waiting for the socket itself to be open, not just for our listener to be hooked up.
    // progress/closed is what lets the caller run whatever it wanted to do afterwards.
    // This effect is required because both are notifications to the outside world (the C# server),
    // not rendering.
    const everOpened = useRef(false);
    useEffect(() => {
        if (props.open) {
            // Start listening before we say we are ready, never the other way round.
            isShowing.current = true;
            if (!socketReady) {
                return; // we'll be back as soon as the socket opens
            }
            everOpened.current = true;
            post("progress/ready");
        } else {
            isShowing.current = false;
            if (everOpened.current) {
                // Clear up as we go, rather than as we open, so that nothing from the last run
                // flickers into view while the next one is opening. (The embedded dialog is
                // mounted once and opened again and again.)
                setPercent(0);
                setProblems([]);
                setDone(false);
                messagesForErrorReporting.current = [];
                post("progress/closed");
            }
        }
    }, [props.open, socketReady]);

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
                // Something is going on behind this dialog, so don't let a stray click outside
                // it, or an Escape, make it disappear.
                if (reason !== "escapeKeyDown" && reason !== "backdropClick") {
                    props.onClose();
                }
            }}
        >
            <DialogTitle
                title={props.title}
                backgroundColor={titleBackground}
                color={titleColor}
            />
            <DialogMiddle>
                <div
                    css={css`
                        display: flex;
                        align-items: center;
                        gap: ${kDialogPadding};
                    `}
                >
                    <LinearProgress
                        variant="determinate"
                        value={percent}
                        css={css`
                            flex: 1;
                            height: 10px;
                            border-radius: 5px;
                        `}
                    />
                    <div
                        css={css`
                            min-width: 35px;
                            text-align: right;
                            color: ${kBloomBlue};
                        `}
                    >
                        {`${percent}%`}
                    </div>
                </div>
                <div
                    css={css`
                        margin-top: 20px;
                        font-size: 16px;
                    `}
                >
                    {props.message}
                </div>
                {problems.map((problem, index) => (
                    <div
                        // The list only grows, and nothing reorders it, so the index is a stable key.
                        key={index}
                        css={css`
                            margin-top: ${kDialogPadding};
                            color: ${problem.kind === "Warning"
                                ? kBloomGold
                                : kErrorColor};
                        `}
                    >
                        {problem.text}
                    </div>
                ))}
            </DialogMiddle>
            {/* Until the job is over there is nothing to click, and we want the dialog no taller
                than its one sentence, so we don't reserve space for buttons. */}
            {done && (
                <DialogBottomButtons>
                    {sawAnError && (
                        <DialogBottomLeftButtons>
                            <BloomButton
                                id="progress-report"
                                hasText={true}
                                enabled={true}
                                l10nKey="Common.Report"
                                variant="text"
                                onClick={() => {
                                    postJson("problemReport/showDialog", {
                                        message:
                                            messagesForErrorReporting.current.join(
                                                "\r\n",
                                            ),
                                        shortMessage: `The user reported a problem from "${props.title}".`,
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
                            temporarilyDisableI18nWarning={true}
                            onClick={props.onClose}
                        >
                            Quit
                        </BloomButton>
                    ) : (
                        <DialogCloseButton onClick={props.onClose} />
                    )}
                </DialogBottomButtons>
            )}
        </BloomDialog>
    );
};

/**
 * The websocket payload that EmbeddedSimpleProgressDialog is expecting. Should stay in sync with
 * the props C# sends to open it (BookProcessor.MakeUpdateBookProgressProps).
 */
interface IEmbeddedSimpleProgressDialogConfig {
    which: string; // must match props.id to open
    title: string;
    titleColor?: string;
    titleBackgroundColor?: string;
    message: string;
}

/**
 * Render one of these, with an id, at the top level of a document whose C# side may want to show
 * a SimpleProgressDialog without a window of its own. It takes up no space and is invisible until
 * an "open-progress" event names its id. The counterpart of EmbeddedProgressDialog, which does the
 * same for the full ProgressDialog; the two can coexist in one document, each ignoring events
 * addressed to the other.
 */
export const EmbeddedSimpleProgressDialog: React.FunctionComponent<{
    id: string;
}> = (props) => {
    const [isOpen, setIsOpen] = useState(false);
    const [config, setConfig] = useState<IEmbeddedSimpleProgressDialogConfig>({
        which: "",
        // Only visible if something is wrong; the C# that opens the dialog supplies the real ones.
        title: "This should not be seen",
        message: "",
    });
    useSubscribeToWebSocketForObject(
        "progress",
        "open-progress",
        (args: IEmbeddedSimpleProgressDialogConfig) => {
            if (args.which !== props.id) {
                return; // meant for some other progress dialog
            }
            setConfig({ ...args });
            setIsOpen(true);
        },
    );
    useSubscribeToWebSocketForEvent("progress", "close-progress", () => {
        setIsOpen(false);
    });

    return (
        <SimpleProgressDialog
            {...config}
            open={isOpen}
            onClose={() => {
                setIsOpen(false);
            }}
            dialogEnvironment={{
                initiallyOpen: false,
                dialogFrameProvidedExternally: false,
            }}
        />
    );
};
