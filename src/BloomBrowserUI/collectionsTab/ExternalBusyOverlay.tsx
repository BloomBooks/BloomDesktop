import { css } from "@emotion/react";
import * as React from "react";
import { useState } from "react";
import { Backdrop, CircularProgress, Paper } from "@mui/material";
import {
    useSubscribeToWebSocketForEvent,
    useSubscribeToWebSocketForObject,
} from "../utils/WebSocketManager";
import { kBloomBlue, kPanelBackground } from "../bloomMaterialUITheme";
import { useL10n } from "../react_components/l10nHooks";

// A modal "busy" dialog shown while Bloom is doing a long-running external operation
// (currently external/process-book, which processes a book off-screen for ~20-30s). The C# side
// drives it over the "externalProcessing" websocket context: a "show" bundle (carrying a message)
// to raise it and a "hide" event to dismiss it.
//
// It is presented as a centered dialog box sitting on a FULLY OPAQUE backdrop, so the collection
// behind is hidden and cannot be clicked while Bloom is busy (the work runs on the UI thread, so
// interacting with the collection underneath would be a bad idea anyway).
//
// Why a websocket-driven curtain rather than a normal progress dialog: process-book runs
// synchronously on the UI thread (it creates off-screen WebView2 controls, which must be on that
// thread) and merely pumps the message loop with Application.DoEvents(). A BackgroundWorker-based
// dialog therefore doesn't fit, but because the UI thread keeps pumping, the main WebView2 keeps
// painting and this dialog's CSS spinner keeps animating (it runs in the renderer process) for
// the whole run.
export const ExternalBusyOverlay: React.FunctionComponent = () => {
    const [open, setOpen] = useState(false);
    const [message, setMessage] = useState<string | undefined>();

    // Localized fallback shown if the C# side ever raises the overlay without supplying a message.
    // Applied at render time (rather than captured in the websocket callback) so it reflects the
    // localized value even though useL10n resolves asynchronously.
    const defaultBusyMessage = useL10n(
        "Bloom is busy, please wait…",
        "Common.BloomIsBusy",
    );

    useSubscribeToWebSocketForObject<{ message?: string }>(
        "externalProcessing",
        "show",
        (data) => {
            setMessage(data.message);
            setOpen(true);
        },
    );
    useSubscribeToWebSocketForEvent("externalProcessing", "hide", () => {
        setOpen(false);
    });

    return (
        <Backdrop
            open={open}
            css={css`
                // Sit above everything else. This overlay is shown on the Collection tab, where the
                // Edit-tab chrome (toolbox at 18000, origami at 60000, etc.) isn't present, but we use
                // a value above those anyway so it stays the topmost layer regardless of context.
                // (MUI modals default to 1300.)
                z-index: 100000;
                // Fully opaque (not the default translucent scrim) so the user can neither see nor
                // click the collection behind while Bloom is busy.
                background-color: ${kPanelBackground};
            `}
        >
            <Paper
                elevation={8}
                css={css`
                    display: flex;
                    flex-direction: column;
                    align-items: center;
                    padding: 30px 40px;
                    min-width: 280px;
                    max-width: 420px;
                `}
            >
                <CircularProgress
                    css={css`
                        color: ${kBloomBlue};
                    `}
                />
                <div
                    css={css`
                        margin-top: 20px;
                        font-size: 16px;
                        text-align: center;
                    `}
                >
                    {message ?? defaultBusyMessage}
                </div>
            </Paper>
        </Backdrop>
    );
};
