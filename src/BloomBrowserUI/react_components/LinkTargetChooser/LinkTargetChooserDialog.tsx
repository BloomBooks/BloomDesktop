/** @jsxImportSource @emotion/react */
import * as React from "react";
import { useState, useCallback } from "react";
import { css } from "@emotion/react";
import {
    BloomDialog,
    DialogTitle,
    DialogMiddle,
    DialogBottomButtons,
    DialogBottomLeftButtons,
} from "../BloomDialog/BloomDialog";
import { LinkTargetChooser, LinkTargetInfo } from "./LinkTargetChooser";
import { DialogCloseButton } from "../BloomDialog/commonDialogComponents";
import BloomButton from "../bloomButton";
import { useL10n } from "../l10nHooks";

export const LinkTargetChooserDialog: React.FunctionComponent<{
    open: boolean;
    currentURL: string;
    onClose: () => void;
    onSelect?: (info: LinkTargetInfo) => void;
}> = (props) => {
    const { onClose, onSelect } = props;
    const [currentLinkInfo, setCurrentLinkInfo] =
        useState<LinkTargetInfo | null>(null);

    const handleURLChanged = useCallback((info: LinkTargetInfo) => {
        setCurrentLinkInfo(info);
    }, []);

    const handleOK = useCallback(() => {
        if (currentLinkInfo && onSelect) {
            onSelect(currentLinkInfo);
        }
    }, [currentLinkInfo, onSelect]);

    const handleClose = useCallback(() => {
        if (onClose) {
            onClose();
        }
    }, [onClose]);

    const dialogTitle = useL10n(
        "Choose Link Target",
        "LinkTargetChooser.Dialog.Title",
        "Title of the dialog used to pick the destination for a link.",
    );

    const hasValidLink =
        currentLinkInfo !== null &&
        currentLinkInfo.url.trim() !== "" &&
        !currentLinkInfo.hasError;

    return (
        <BloomDialog
            open={props.open}
            onClose={props.onClose}
            css={css`
                // injecting previewMode.css in page thumbnails set cursor to not-allowed
                cursor: default !important;
                .MuiDialog-paper {
                    max-width: 1200px;
                    width: 90vw;
                }
            `}
        >
            <DialogTitle title={dialogTitle} />
            <DialogMiddle
                css={css`
                    display: flex;
                    flex-direction: column;
                    height: 600px;
                `}
            >
                <LinkTargetChooser
                    currentURL={props.currentURL}
                    onURLChanged={handleURLChanged}
                />
            </DialogMiddle>
            <DialogBottomButtons>
                <DialogBottomLeftButtons>
                    {/* Future: Add "Remove Link" button here */}
                </DialogBottomLeftButtons>
                <DialogCloseButton onClick={handleClose} />
                <BloomButton
                    onClick={handleOK}
                    enabled={hasValidLink}
                    l10nKey="Common.OK"
                    hasText={true}
                    className="initialFocus"
                >
                    OK
                </BloomButton>
            </DialogBottomButtons>
        </BloomDialog>
    );
};
