import { postJsonAsync } from "../utils/bloomApi";
import * as React from "react";
import WebSocketManager, {
    IBloomWebSocketEvent,
} from "../utils/WebSocketManager";
import { ToastInfo, Toast } from "./Toast";
import { useToastDebugEvents } from "./toastUtils";

type ToastShowEvent = IBloomWebSocketEvent & ToastInfo;

const getMessageIdentity = (toast: ToastInfo): string => {
    return toast.l10nId ?? toast.text!;
};

export const ToastHost: React.FunctionComponent<{
    dismissMessageKeys?: string[];
}> = (props) => {
    const [toasts, setToasts] = React.useState<ToastInfo[]>([]);

    const enqueueToasts = React.useCallback((incomingToasts: ToastInfo[]) => {
        setToasts((currentToasts) => {
            const nextToasts = [...currentToasts];

            incomingToasts.forEach((incomingToast) => {
                const isDuplicate = nextToasts.some(
                    (toast) =>
                        !!getMessageIdentity(incomingToast) &&
                        getMessageIdentity(incomingToast) ===
                            getMessageIdentity(toast),
                );

                if (!isDuplicate) {
                    nextToasts.push(incomingToast);
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
            if (!toast.action) {
                return;
            }

            if (toast.action.url) {
                window.location.href = toast.action.url;
                return;
            }

            if (toast.action.callbackId) {
                void postJsonAsync("toast/performAction", {
                    callbackId: toast.action.callbackId,
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

    return (
        <>
            {toasts.map((toast, index) => (
                <Toast
                    key={getMessageIdentity(toast)}
                    toast={toast}
                    index={index}
                    onClose={removeToast}
                    onAction={handleAction}
                />
            ))}
        </>
    );
};
