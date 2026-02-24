import CloseIcon from "@mui/icons-material/Close";
import ErrorOutlineIcon from "@mui/icons-material/ErrorOutline";
import WarningAmberIcon from "@mui/icons-material/WarningAmber";
import { css } from "@emotion/react";
import { Snackbar, useTheme } from "@mui/material";
import { Theme } from "@mui/material/styles";
import * as React from "react";
import { useL10n2 } from "../react_components/l10nHooks";

// Keep values in sync with ToastSeverity in ToastService.cs.
export type ToastSeverity = "error" | "warning" | "notice";

type RequireAtLeastOne<T, Keys extends keyof T = keyof T> = Omit<T, Keys> &
    {
        [K in Keys]-?: Required<Pick<T, K>> & Partial<Omit<T, K>>;
    }[Keys];

type ToastActionInfoBase = {
    // Keep property names and semantics in sync with ToastAction in ToastService.cs.
    label?: string;
    l10nId?: string;
    url?: string;
    callbackId?: string;
};

export type ToastInfoAction = RequireAtLeastOne<
    ToastActionInfoBase,
    "label" | "l10nId"
>;

type ToastInfoBase = {
    toastId: string;
    severity: ToastSeverity;
    text?: string;
    l10nId?: string;
    durationSeconds?: number;
    action?: ToastInfoAction;
};

export type ToastInfo = RequireAtLeastOne<ToastInfoBase, "text" | "l10nId">;

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
                width: 18px;
                height: 18px;
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
    toast: ToastInfo;
    index: number;
    onClose: (toastId: string) => void;
    onAction: (toast: ToastInfo) => void;
}> = (props) => {
    const theme = useTheme();
    const localizedMessageFromL10n = useL10n2({
        key: props.toast.l10nId || "",
    });
    const localizedActionLabelFromL10n = useL10n2({
        key: props.toast.action?.l10nId || "",
    });
    const localizedMessage = props.toast.l10nId
        ? localizedMessageFromL10n
        : props.toast.text;
    const localizedActionLabel = props.toast.action?.l10nId
        ? localizedActionLabelFromL10n
        : props.toast.action?.label;

    const TOAST_HEIGHT_WITH_SPACING = 72;
    const TOAST_BOTTOM_MARGIN = 24;

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
                    bottom: ${TOAST_BOTTOM_MARGIN +
                    props.index * TOAST_HEIGHT_WITH_SPACING}px;
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
                    display: flex;
                    min-width: 360px;
                    max-width: 432px;
                    align-items: flex-start;
                    gap: 12px;
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
                        display: flex;
                        flex-direction: column;
                        align-items: flex-start;
                        gap: 6px;
                        line-height: 1.35;
                    `}
                >
                    <div>{localizedMessage}</div>
                    {props.toast.action && localizedActionLabel ? (
                        <button
                            type="button"
                            css={css`
                                margin: 0;
                                padding: 0;
                                border: none;
                                background: transparent;
                                color: ${theme.palette.primary.main};
                                font: inherit;
                                line-height: inherit;
                                text-decoration: underline;
                                text-underline-offset: 2px;
                                cursor: pointer;

                                &:hover {
                                    color: ${theme.palette.primary.dark};
                                }
                            `}
                            onClick={(event) => {
                                event.stopPropagation();
                                props.onAction(props.toast);
                            }}
                        >
                            {localizedActionLabel}
                        </button>
                    ) : null}
                </div>
                <button
                    type="button"
                    aria-label="Close toast"
                    css={css`
                        margin: 0;
                        padding: 0;
                        border: none;
                        background: transparent;
                        color: ${theme.palette.text.secondary};
                        display: inline-flex;
                        align-items: center;
                        justify-content: center;
                        cursor: pointer;

                        &:hover {
                            color: ${theme.palette.text.primary};
                        }
                    `}
                    onClick={(event) => {
                        event.stopPropagation();
                        props.onClose(props.toast.toastId);
                    }}
                >
                    <CloseIcon fontSize="small" />
                </button>
            </div>
        </Snackbar>
    );
};
