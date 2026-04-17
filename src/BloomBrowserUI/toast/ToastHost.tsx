import { postJsonAsync } from "../utils/bloomApi";
import * as React from "react";
import WebSocketManager, {
    IBloomWebSocketEvent,
} from "../utils/WebSocketManager";
import { ToastInfo, Toast } from "./Toast";
import { useToastDebugEvents } from "./toastUtils";
import { ThemeProvider } from "@mui/material";
import { lightTheme } from "../bloomMaterialUITheme";
import { ToastContainer, ToastOptions, toast } from "react-toastify";
import "react-toastify/dist/ReactToastify.css";

type ToastShowEvent = IBloomWebSocketEvent & ToastInfo;

const getMessageIdentity = (toast: ToastInfo): string => {
    return toast.l10nId ?? toast.text!;
};

const getToastLifetimeSeconds = (toast: ToastInfo): number => {
    return toast.durationSeconds ?? Number.POSITIVE_INFINITY;
};

const getToastAutoClose = (toastInfo: ToastInfo): number | false => {
    return toastInfo.durationSeconds ? toastInfo.durationSeconds * 1000 : false;
};

export const ToastHost: React.FunctionComponent<{
    dismissMessageKeys?: string[];
}> = (props) => {
    const activeToastsRef = React.useRef<Map<string, ToastInfo>>(new Map());

    const removeToast = React.useCallback((toastToRemove: ToastInfo) => {
        const identity = getMessageIdentity(toastToRemove);
        activeToastsRef.current.delete(identity);
        toast.dismiss(identity);
    }, []);

    const handleAction = React.useCallback(
        (toastInfo: ToastInfo) => {
            if (!toastInfo.actionInfo) {
                return;
            }

            if (toastInfo.actionInfo.callbackId) {
                // Keep the toast visible until the backend confirms it accepted the callback.
                // That way a failed post does not make the action silently disappear.
                void postJsonAsync("toast/performAction", {
                    callbackId: toastInfo.actionInfo.callbackId,
                }).then(() => {
                    removeToast(toastInfo);
                });

                return;
            }

            removeToast(toastInfo);
        },
        [removeToast],
    );

    const getToastOptions = React.useCallback(
        (identity: string, toastInfo: ToastInfo): ToastOptions => {
            return {
                toastId: identity,
                autoClose: getToastAutoClose(toastInfo),
                closeOnClick: false,
                draggable: false,
                icon: false,
                onClose: () => {
                    activeToastsRef.current.delete(identity);
                },
            };
        },
        [],
    );

    const showToast = React.useCallback(
        (toastInfo: ToastInfo) => {
            const identity = getMessageIdentity(toastInfo);
            toast(
                <Toast
                    toast={toastInfo}
                    onClose={removeToast}
                    onAction={handleAction}
                />,
                getToastOptions(identity, toastInfo),
            );

            activeToastsRef.current.set(identity, toastInfo);
        },
        [getToastOptions, handleAction, removeToast],
    );

    const updateToast = React.useCallback(
        (toastInfo: ToastInfo) => {
            const identity = getMessageIdentity(toastInfo);

            if (!activeToastsRef.current.has(identity)) {
                showToast(toastInfo);
                return;
            }

            activeToastsRef.current.set(identity, toastInfo);
            toast.update(identity, {
                ...getToastOptions(identity, toastInfo),
                render: (
                    <Toast
                        toast={toastInfo}
                        onClose={removeToast}
                        onAction={handleAction}
                    />
                ),
            });
        },
        [getToastOptions, handleAction, removeToast, showToast],
    );

    const enqueueToasts = React.useCallback(
        (incomingToasts: ToastInfo[]) => {
            incomingToasts.forEach((incomingToast) => {
                const identity = getMessageIdentity(incomingToast);
                const existingToast = activeToastsRef.current.get(identity);

                if (!existingToast) {
                    showToast(incomingToast);
                    return;
                }

                if (
                    getToastLifetimeSeconds(incomingToast) >
                    getToastLifetimeSeconds(existingToast)
                ) {
                    updateToast({
                        ...existingToast,
                        ...incomingToast,
                    });
                }
            });
        },
        [showToast, updateToast],
    );

    const clearToasts = React.useCallback(() => {
        activeToastsRef.current.clear();
        toast.dismiss();
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

        props.dismissMessageKeys.forEach((messageKey) => {
            if (activeToastsRef.current.has(messageKey)) {
                activeToastsRef.current.delete(messageKey);
                toast.dismiss(messageKey);
            }
        });
    }, [props.dismissMessageKeys]);

    return (
        <ThemeProvider theme={lightTheme}>
            <ToastContainer
                position="bottom-right"
                newestOnTop={true}
                closeButton={false}
                hideProgressBar={true}
                toastStyle={{
                    minHeight: "unset",
                    padding: 0,
                }}
            />
        </ThemeProvider>
    );
};
