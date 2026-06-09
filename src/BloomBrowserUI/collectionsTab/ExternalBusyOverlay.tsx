import { css } from "@emotion/react";
import * as React from "react";
import { useState } from "react";
import { Backdrop, CircularProgress } from "@mui/material";
import {
    useSubscribeToWebSocketForEvent,
    useSubscribeToWebSocketForObject,
} from "../utils/WebSocketManager";
import { kBloomBlue } from "../bloomMaterialUITheme";

// A full-window "busy" curtain shown while Bloom is doing a long-running external operation
// (currently external/process-book, which processes a book off-screen for ~20-30s). The C# side
// drives it over the "externalProcessing" websocket context: a "show" bundle (carrying a message)
// to raise it and a "hide" event to dismiss it.
//
// Why a websocket-driven overlay rather than a normal progress dialog: process-book runs
// synchronously on the UI thread (it creates off-screen WebView2 controls, which must be on that
// thread) and merely pumps the message loop with Application.DoEvents(). A BackgroundWorker-based
// dialog therefore doesn't fit, but because the UI thread keeps pumping, the main WebView2 keeps
// painting and this overlay's CSS spinner keeps animating (it runs in the renderer process) for
// the whole run.
export const ExternalBusyOverlay: React.FunctionComponent = () => {
    const [message, setMessage] = useState<string | undefined>();

    useSubscribeToWebSocketForObject<{ message?: string }>(
        "externalProcessing",
        "show",
        (data) => {
            setMessage(data.message ?? "Bloom is busy, please wait…");
        },
    );
    useSubscribeToWebSocketForEvent("externalProcessing", "hide", () => {
        setMessage(undefined);
    });

    return (
        <Backdrop
            open={message !== undefined}
            // Above everything else in the tab (MUI modals default to 1300).
            css={css`
                z-index: 5000;
                color: white;
                flex-direction: column;
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
                    max-width: 400px;
                    text-align: center;
                `}
            >
                {message}
            </div>
        </Backdrop>
    );
};
