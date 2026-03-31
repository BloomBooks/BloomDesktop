import { css } from "@emotion/react";
import * as React from "react";
import {
    ErrorBox,
    NoteBox,
    UpdateBox,
    WarningBox,
} from "../react_components/boxes";
import BloomButton from "../react_components/bloomButton";
import { useL10n2 } from "../react_components/l10nHooks";

// Keep values in sync with ToastType in ToastService.cs.
export type ToastType = "error" | "warning" | "notice" | "update";

type RequireAtLeastOne<T, Keys extends keyof T = keyof T> = Omit<T, Keys> &
    {
        [K in Keys]-?: Required<Pick<T, K>> & Partial<Omit<T, K>>;
    }[Keys];

// Keep in sync with ToastAction in ToastService.cs.
type ToastActionInfoBase = {
    label?: string;
    l10nId?: string;
    callbackId?: string;
};

type ToastActionInfo = RequireAtLeastOne<
    ToastActionInfoBase,
    "label" | "l10nId"
>;

// Keep in sync with ShowToast() in ToastService.cs.
type ToastInfoBase = {
    type?: ToastType;
    text?: string;
    l10nId?: string;
    durationSeconds?: number;
    actionInfo?: ToastActionInfo;
};

export type ToastInfo = RequireAtLeastOne<ToastInfoBase, "text" | "l10nId">;

export const Toast: React.FunctionComponent<{
    toast: ToastInfo;
    onClose: (toast: ToastInfo) => void;
    onAction: (toast: ToastInfo) => void;
}> = (props) => {
    const type = props.toast.type ?? "notice";
    const ToastBox =
        type === "error"
            ? ErrorBox
            : type === "warning"
              ? WarningBox
              : type === "update"
                ? UpdateBox
                : NoteBox;

    const localizedMessageFromL10n = useL10n2({
        key: props.toast.l10nId || "",
    });
    const localizedActionLabelFromL10n = useL10n2({
        key: props.toast.actionInfo?.l10nId || "",
    });
    const localizedMessage = props.toast.l10nId
        ? localizedMessageFromL10n
        : props.toast.text;
    const localizedActionLabel = props.toast.actionInfo?.l10nId
        ? localizedActionLabelFromL10n
        : props.toast.actionInfo?.label;
    const actionButton =
        props.toast.actionInfo && localizedActionLabel ? (
            <div
                onClick={(event) => {
                    event.stopPropagation();
                }}
            >
                <BloomButton
                    enabled={true}
                    l10nKey=""
                    alreadyLocalized={true}
                    hasText={true}
                    color="inherit"
                    variant="outlined"
                    size="small"
                    onClick={() => {
                        props.onAction(props.toast);
                    }}
                    css={css`
                        && {
                            color: inherit;
                            border-color: currentColor;
                        }

                        &&:hover {
                            border-color: currentColor;
                        }
                    `}
                >
                    {localizedActionLabel}
                </BloomButton>
            </div>
        ) : undefined;
    const contentCss = css`
        width: min(90vw, 300px);
        margin-block-end: 0;
    `;
    const wrapperCss = css`
        cursor: ${props.toast.actionInfo ? "pointer" : "default"};
    `;
    const handleToastClick = () => {
        if (props.toast.actionInfo) {
            props.onAction(props.toast);
        }
    };

    return (
        <div css={wrapperCss} onClick={handleToastClick}>
            <ToastBox
                closeButton={true}
                onCloseButtonClick={() => {
                    props.onClose(props.toast);
                }}
                bottomRightButton={actionButton}
                css={contentCss}
            >
                {localizedMessage}
            </ToastBox>
        </div>
    );
};
