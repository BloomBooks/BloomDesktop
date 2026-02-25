import ErrorOutlineIcon from "@mui/icons-material/ErrorOutline";
import WarningAmberIcon from "@mui/icons-material/WarningAmber";
import { css } from "@emotion/react";
import { Button, Snackbar, useTheme } from "@mui/material";
import { Theme } from "@mui/material/styles";
import * as React from "react";
import { useL10n } from "../react_components/l10nHooks";

// Keep values in sync with ToastSeverity in src/BloomExe/web/ToastService.cs.
export type ToastSeverity = "error" | "warning" | "notice";

export interface IToastAction {
    // Keep property names and semantics in sync with ToastAction in src/BloomExe/web/ToastService.cs.
    label?: string;
    l10nId?: string;
    url?: string;
    callbackId?: string;
}

export interface IToast {
    toastId: string;
    severity: ToastSeverity;
    text?: string;
    l10nId?: string;
    l10nDefaultText?: string;
    durationSeconds?: number;
    dedupeKey?: string;
    action?: IToastAction;
}

const getSeverityIcon = (severity: ToastSeverity): React.ReactNode => {
    if (severity === "error") {
        return <ErrorOutlineIcon fontSize="small" />;
    }

    if (severity === "warning") {
        return <WarningAmberIcon fontSize="small" />;
    }

    return (
        <img
            src="/bloom/images/favicon.ico"
            alt="Bloom"
            css={css`
                width: 16px;
                height: 16px;
            `}
        />
    );
};

const getSeverityColor = (severity: ToastSeverity, theme: Theme): string => {
    if (severity === "error") {
        return theme.palette.error.main;
    }

    if (severity === "warning") {
        return theme.palette.warning.main;
    }

    return theme.palette.info.main;
};

export const Toast: React.FunctionComponent<{
    toast: IToast;
    index: number;
    onClose: (toastId: string) => void;
    onAction: (toast: IToast) => void;
}> = (props) => {
    const theme = useTheme();
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
                props.toast.durationSeconds
                    ? props.toast.durationSeconds * 1000
                    : null
            }
            onClose={(_event, reason) => {
                if (reason === "clickaway") {
                    return;
                }
                props.onClose(props.toast.toastId);
            }}
            css={css`
                && {
                    bottom: ${16 + props.index * 76}px;
                }
            `}
        >
            <div
                role={props.toast.severity === "notice" ? "status" : "alert"}
                aria-live={
                    props.toast.severity === "notice" ? "polite" : "assertive"
                }
                aria-atomic={true}
                css={css`
                    min-width: 360px;
                    display: flex;
                    align-items: center;
                    gap: 10px;
                    padding: 10px 14px;
                    border-radius: 4px;
                    background-color: ${theme.palette.background.paper};
                    color: ${theme.palette.text.primary};
                    box-shadow: ${theme.shadows[6]};
                    cursor: ${props.toast.action ? "pointer" : "default"};
                `}
                onClick={() => {
                    if (props.toast.action) {
                        props.onAction(props.toast);
                    }
                }}
            >
                <span
                    css={css`
                        color: ${getSeverityColor(props.toast.severity, theme)};
                        display: inline-flex;
                        align-items: center;
                    `}
                >
                    {getSeverityIcon(props.toast.severity)}
                </span>
                <div
                    css={css`
                        flex: 1;
                    `}
                >
                    {localizedMessage}
                </div>
                {props.toast.action && localizedActionLabel ? (
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
                ) : undefined}
            </div>
        </Snackbar>
    );
};
