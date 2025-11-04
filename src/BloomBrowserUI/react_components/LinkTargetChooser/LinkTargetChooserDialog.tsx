/** @jsxImportSource @emotion/react */
import * as React from "react";
import { useState } from "react";
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

export const LinkTargetChooserDialog: React.FunctionComponent<{
    open: boolean;
    currentURL: string;
    onClose: () => void;
    onSelect?: (info: LinkTargetInfo) => void;
}> = (props) => {
    const [currentLinkInfo, setCurrentLinkInfo] =
        useState<LinkTargetInfo | null>(null);

    const handleURLChanged = (info: LinkTargetInfo) => {
        setCurrentLinkInfo(info);
    };

    const handleOK = () => {
        if (currentLinkInfo && props.onSelect) {
            props.onSelect(currentLinkInfo);
        }
    };

    const handleClose = () => {
        if (props.onClose) {
            props.onClose();
        }
    };

    const hasValidLink =
        currentLinkInfo !== null &&
        currentLinkInfo.url.trim() !== "" &&
        !currentLinkInfo.hasError;

    return (
        <BloomDialog
            open={props.open}
            onClose={props.onClose}
            css={css`
                .MuiDialog-paper {
                    max-width: 1200px;
                    width: 90vw;
                }
            `}
        >
            <DialogTitle title="Choose Link Target" />
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
