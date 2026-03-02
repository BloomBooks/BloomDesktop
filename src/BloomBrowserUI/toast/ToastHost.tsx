import { postJson } from "../utils/bloomApi";
import * as React from "react";
import WebSocketManager, {
    IBloomWebSocketEvent,
} from "../utils/WebSocketManager";
import { IToast, Toast } from "./Toast";

type IToastShowEvent = IBloomWebSocketEvent & IToast;

const getMessageIdentity = (toast: IToast): string | undefined => {
    if (toast.l10nId) {
        return toast.l10nId;
    }

    if (toast.text) {
        return toast.text;
    }

    return toast.l10nDefaultText;
};

export const ToastHost: React.FunctionComponent<{
    dismissMessageKeys?: string[];
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
                                (!!getMessageIdentity(showEvent) &&
                                    getMessageIdentity(showEvent) ===
                                        getMessageIdentity(toast)),
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
