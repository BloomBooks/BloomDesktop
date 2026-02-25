import { postJson } from "../utils/bloomApi";
import * as React from "react";
import WebSocketManager, {
    IBloomWebSocketEvent,
} from "../utils/WebSocketManager";
import { IToast, Toast } from "./Toast";

type IToastShowEvent = IBloomWebSocketEvent & IToast;

export const ToastHost: React.FunctionComponent<{
    dismissDedupeKeys?: string[];
}> = (props) => {
    const [toasts, setToasts] = React.useState<IToast[]>([]);

    const removeToast = React.useCallback((toastId: string) => {
        setToasts((currentToasts) =>
            currentToasts.filter((toast) => toast.toastId !== toastId),
        );
    }, []);

    const handleAction = React.useCallback(
        (toast: IToast) => {
            if (!toast.action) {
                return;
            }

            if (toast.action.url) {
                window.location.href = toast.action.url;
                removeToast(toast.toastId);
                return;
            }

            if (toast.action.callbackId) {
                postJson("toast/performAction", {
                    callbackId: toast.action.callbackId,
                });
            }

            removeToast(toast.toastId);
        },
        [removeToast],
    );

    // Subscribe once to backend websocket toast show events so the host can render toasts.
    React.useEffect(() => {
        const listener = (event: IBloomWebSocketEvent) => {
            if (event.id === "show") {
                const showEvent = event as IToastShowEvent;
                setToasts((currentToasts) => {
                    if (
                        currentToasts.some(
                            (toast) =>
                                toast.toastId === showEvent.toastId ||
                                (!!showEvent.dedupeKey &&
                                    showEvent.dedupeKey === toast.dedupeKey),
                        )
                    ) {
                        return currentToasts;
                    }

                    return [...currentToasts, { ...showEvent }];
                });
            }
        };

        WebSocketManager.addListener("toast", listener);
        return () => WebSocketManager.removeListener("toast", listener);
    }, []);

    // Dismiss selected dedupe-key toasts when container UI state says they are no longer relevant.
    React.useEffect(() => {
        if (!props.dismissDedupeKeys || props.dismissDedupeKeys.length === 0) {
            return;
        }

        setToasts((currentToasts) =>
            currentToasts.filter(
                (toast) =>
                    !props.dismissDedupeKeys?.includes(toast.dedupeKey || ""),
            ),
        );
    }, [props.dismissDedupeKeys]);

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
