import { postJsonAsync } from "../utils/bloomApi";
import * as React from "react";
import { css } from "@emotion/react";
import WebSocketManager, {
    IBloomWebSocketEvent,
} from "../utils/WebSocketManager";
import { ToastInfo, Toast } from "./Toast";
import { useToastDebugEvents } from "./toastUtils";
import { ThemeProvider } from "@mui/material";
import { lightTheme } from "../bloomMaterialUITheme";

type ToastShowEvent = IBloomWebSocketEvent & ToastInfo;

const getMessageIdentity = (toast: ToastInfo): string => {
    return toast.l10nId ?? toast.text!;
};

const getToastLifetimeSeconds = (toast: ToastInfo): number => {
    return toast.durationSeconds ?? Number.POSITIVE_INFINITY;
};

export const ToastHost: React.FunctionComponent<{
    dismissMessageKeys?: string[];
}> = (props) => {
    const [toasts, setToasts] = React.useState<ToastInfo[]>([]);

    const enqueueToasts = React.useCallback((incomingToasts: ToastInfo[]) => {
        setToasts((currentToasts) => {
            const nextToasts = [...currentToasts];

            incomingToasts.forEach((incomingToast) => {
                const duplicateIndex = nextToasts.findIndex(
                    (toast) =>
                        !!getMessageIdentity(incomingToast) &&
                        getMessageIdentity(incomingToast) ===
                            getMessageIdentity(toast),
                );

                if (duplicateIndex < 0) {
                    nextToasts.push(incomingToast);
                    return;
                }

                const existingToast = nextToasts[duplicateIndex];
                if (
                    getToastLifetimeSeconds(incomingToast) >
                    getToastLifetimeSeconds(existingToast)
                ) {
                    nextToasts[duplicateIndex] = {
                        ...existingToast,
                        ...incomingToast,
                    };
                }
            });

            return nextToasts;
        });
    }, []);

    const removeToast = React.useCallback((toastToRemove: ToastInfo) => {
        setToasts((currentToasts) =>
            currentToasts.filter(
                (toast) =>
                    getMessageIdentity(toast) !==
                    getMessageIdentity(toastToRemove),
            ),
        );
    }, []);

    const handleAction = React.useCallback(
        (toast: ToastInfo) => {
            if (!toast.actionInfo) {
                return;
            }

            if (toast.actionInfo.callbackId) {
                // Keep the toast visible until the backend confirms it accepted the callback.
                // That way a failed post does not make the action silently disappear.
                void postJsonAsync("toast/performAction", {
                    callbackId: toast.actionInfo.callbackId,
                }).then(() => {
                    removeToast(toast);
                });

                return;
            }

            removeToast(toast);
        },
        [removeToast],
    );

    const clearToasts = React.useCallback(() => {
        setToasts([]);
    }, []);

    // Subscribe once to backend websocket toast show events so the host can render toasts.
    React.useEffect(() => {
        const listener = (event: IBloomWebSocketEvent) => {
            if (event.id === "show") {
                enqueueToasts([{ ...(event as ToastShowEvent) }]);
            }
        };

        WebSocketManager.addListener("toast", listener);
        return () => WebSocketManager.removeListener("toast", listener);
    }, [enqueueToasts]);

    // A utility to help developers test toasts without needing to trigger them from the backend.
    useToastDebugEvents(enqueueToasts, clearToasts);

    // Dismiss selected dedupe-key toasts when container UI state says they are no longer relevant.
    React.useEffect(() => {
        if (
            !props.dismissMessageKeys ||
            props.dismissMessageKeys.length === 0
        ) {
            return;
        }

        setToasts((currentToasts) =>
            currentToasts.filter(
                (toast) =>
                    !props.dismissMessageKeys?.includes(
                        getMessageIdentity(toast) || "",
                    ),
            ),
        );
    }, [props.dismissMessageKeys]);

    // Each Toast positions itself above the previous one using padding.
    // Note that I decided, for now, it isn't worth the complication of ensuring that variable-height
    // toasts are perfectly stacked with equal spacing.
    // I.e. the bottom of each toast is equally spaced from the bottom of the next toast.
    // I also haven't tried to properly handle more toasts than can fit on the screen.
    return (
        <div
            css={css`
                position: relative;
                z-index: 4;
            `}
        >
            <ThemeProvider theme={lightTheme}>
                {toasts.map((toast, index) => (
                    <Toast
                        key={getMessageIdentity(toast)}
                        toast={toast}
                        index={index}
                        onClose={removeToast}
                        onAction={handleAction}
                    />
                ))}
            </ThemeProvider>
        </div>
    );
};
