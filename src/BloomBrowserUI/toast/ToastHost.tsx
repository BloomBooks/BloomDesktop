import { postJsonAsync } from "../utils/bloomApi";
import * as React from "react";
import WebSocketManager, {
    IBloomWebSocketEvent,
} from "../utils/WebSocketManager";
import { ToastInfo, Toast, ToastInfoAction, ToastSeverity } from "./Toast";

type ToastShowEvent = IBloomWebSocketEvent & ToastInfo;
type DebugToastInput =
    | {
          text: string;
          l10nId?: string;
          severity?: ToastSeverity;
          durationSeconds?: number;
          action?: ToastInfoAction;
          toastId?: string;
      }
    | {
          l10nId: string;
          text?: string;
          severity?: ToastSeverity;
          durationSeconds?: number;
          action?: ToastInfoAction;
          toastId?: string;
      };

const kToastDebugShowEvent = "bloom-toast-debug-show";
const kToastDebugClearEvent = "bloom-toast-debug-clear";

const ensureToastId = (toast: DebugToastInput): ToastInfo => {
    return {
        ...toast,
        severity: toast.severity ?? "notice",
        toastId:
            toast.toastId ??
            `debug-toast-${Date.now().toString()}-${Math.random().toString(36).slice(2)}`,
    };
};

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
                        toast.toastId === incomingToast.toastId ||
                        (!!getMessageIdentity(incomingToast) &&
                            getMessageIdentity(incomingToast) ===
                                getMessageIdentity(toast)),
                );

                if (!isDuplicate) {
                    nextToasts.push(incomingToast);
                }
            });

            return nextToasts;
        });
    }, []);

    const removeToast = React.useCallback((toastId: string) => {
        setToasts((currentToasts) =>
            currentToasts.filter((toast) => toast.toastId !== toastId),
        );
    }, []);

    const handleAction = React.useCallback(
        (toast: ToastInfo) => {
            if (!toast.action) {
                return;
            }

            if (toast.action.url) {
                window.location.href = toast.action.url;
                removeToast(toast.toastId);
                return;
            }

            if (toast.action.callbackId) {
                void postJsonAsync("toast/performAction", {
                    callbackId: toast.action.callbackId,
                }).then(() => {
                    removeToast(toast.toastId);
                });

                return;
            }

            removeToast(toast.toastId);
        },
        [removeToast],
    );

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

    // Subscribe to root-window debug events so developers can inject and clear toasts
    // without needing to hit every backend scenario manually.
    React.useEffect(() => {
        const handleDebugShow = (event: Event) => {
            const customEvent = event as CustomEvent<
                DebugToastInput | DebugToastInput[]
            >;
            const requestedToasts = Array.isArray(customEvent.detail)
                ? customEvent.detail
                : [customEvent.detail];

            enqueueToasts(requestedToasts.map(ensureToastId));
        };

        const handleDebugClear = () => {
            setToasts([]);
        };

        window.addEventListener(kToastDebugShowEvent, handleDebugShow);
        window.addEventListener(kToastDebugClearEvent, handleDebugClear);

        return () => {
            window.removeEventListener(kToastDebugShowEvent, handleDebugShow);
            window.removeEventListener(kToastDebugClearEvent, handleDebugClear);
        };
    }, [enqueueToasts]);

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
                    key={toast.toastId}
                    toast={toast}
                    index={index}
                    onClose={removeToast}
                    onAction={handleAction}
                />
            ))}
        </>
    );
};

export const toastDebugEvents = {
    clear: kToastDebugClearEvent,
    show: kToastDebugShowEvent,
};
