import { css } from "@emotion/react";
import { Alert, Button, Snackbar } from "@mui/material";
import * as React from "react";
import { useL10n } from "../l10nHooks";
import WebSocketManager, {
    IBloomWebSocketEvent,
} from "../../utils/WebSocketManager";
import { postJson } from "../../utils/bloomApi";

type ToastSeverity = "error" | "warning" | "notice";

interface IToastAction {
    label?: string;
    l10nId?: string;
    kind?: "restart" | "navigate" | "openErrorDialog" | "callback";
    url?: string;
    callbackId?: string;
}

interface IToast {
    toastId: string;
    severity: ToastSeverity;
    text?: string;
    l10nId?: string;
    l10nDefaultText?: string;
    autoDismiss: boolean;
    durationMs?: number;
    dedupeKey?: string;
    action?: IToastAction;
}

interface IToastShowEvent extends IBloomWebSocketEvent {
    toastId: string;
    severity: ToastSeverity;
    text?: string;
    l10nId?: string;
    l10nDefaultText?: string;
    autoDismiss: boolean;
    durationMs?: number;
    dedupeKey?: string;
    action?: IToastAction;
}

interface IToastDismissEvent extends IBloomWebSocketEvent {
    toastId: string;
}

const getMuiSeverity = (severity: ToastSeverity) => {
    if (severity === "error") return "error";
    if (severity === "warning") return "warning";
    return "info";
};

const ToastItem: React.FunctionComponent<{
    toast: IToast;
    index: number;
    onClose: (toastId: string) => void;
    onAction: (toast: IToast) => void;
}> = (props) => {
    const localizedMessage = useL10n(
        props.toast.l10nDefaultText || props.toast.text || "",
        props.toast.l10nId ?? null,
    );
    const localizedActionLabel = useL10n(
        props.toast.action?.label || "",
        props.toast.action?.l10nId ?? null,
    );

    return (
        <Snackbar
            open={true}
            anchorOrigin={{ vertical: "bottom", horizontal: "right" }}
            autoHideDuration={
                props.toast.autoDismiss
                    ? (props.toast.durationMs ?? 6000)
                    : null
            }
            onClose={(_event, reason) => {
                if (reason === "clickaway") {
                    return;
                }
                props.onClose(props.toast.toastId);
            }}
            sx={{
                bottom: `${16 + props.index * 76}px !important`,
            }}
        >
            <Alert
                severity={getMuiSeverity(props.toast.severity)}
                variant="filled"
                css={css`
                    min-width: 360px;
                    cursor: ${props.toast.action ? "pointer" : "default"};
                `}
                onClick={() => {
                    if (props.toast.action) {
                        props.onAction(props.toast);
                    }
                }}
                action={
                    props.toast.action && localizedActionLabel ? (
                        <Button
                            color="inherit"
                            size="small"
                            onClick={(event) => {
                                event.stopPropagation();
                                props.onAction(props.toast);
                            }}
                        >
                            {localizedActionLabel}
                        </Button>
                    ) : undefined
                }
            >
                {localizedMessage}
            </Alert>
        </Snackbar>
    );
};

export const ToastHost: React.FunctionComponent = () => {
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

            if (toast.action.kind === "navigate" && toast.action.url) {
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

    // Subscribe once to backend websocket toast events so the host can show and dismiss toasts.
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
                return;
            }

            if (event.id === "dismiss") {
                const dismissEvent = event as IToastDismissEvent;
                removeToast(dismissEvent.toastId);
            }
        };

        WebSocketManager.addListener("toast", listener);
        return () => WebSocketManager.removeListener("toast", listener);
    }, [removeToast]);

    return (
        <>
            {toasts.map((toast, index) => (
                <ToastItem
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
