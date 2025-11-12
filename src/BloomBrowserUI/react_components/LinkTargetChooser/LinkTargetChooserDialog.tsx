/** @jsxImportSource @emotion/react */
import * as React from "react";
import { useState, useCallback, useEffect } from "react";
import { css } from "@emotion/react";
import {
    BloomDialog,
    DialogTitle,
    DialogMiddle,
    DialogBottomButtons,
    DialogBottomLeftButtons,
} from "../BloomDialog/BloomDialog";
import { LinkTargetChooser, LinkTargetInfo } from "./LinkTargetChooser";
import { parseURL } from "./urlParser";
import { DialogCancelButton } from "../BloomDialog/commonDialogComponents";
import BloomButton from "../bloomButton";
import { useL10n } from "../l10nHooks";

export const LinkTargetChooserDialog: React.FunctionComponent<{
    open: boolean;
    currentURL: string;
    onCancel?: () => void;
    onSelect?: (info: LinkTargetInfo) => void;
}> = (props) => {
    const { onCancel, onSelect } = props;
    const [currentLinkInfo, setCurrentLinkInfo] =
        useState<LinkTargetInfo | null>(null);

    // Sync currentLinkInfo when dialog opens or currentURL changes
    useEffect(() => {
        if (props.open && props.currentURL) {
            const parsed = parseURL(props.currentURL);
            // Only initialize for complete URLs:
            // - External URLs are complete on their own
            // - Book-path URLs with a bookId are complete
            // Do NOT initialize for hash-only URLs (need book context) or empty URLs
            if (
                parsed.urlType === "external" ||
                parsed.urlType === "book-path"
            ) {
                setCurrentLinkInfo({
                    url: parsed.parsedUrl,
                    bookThumbnail: null,
                    bookTitle: null,
                    hasError: false,
                });
            } else {
                setCurrentLinkInfo(null);
            }
        } else if (props.open) {
            // Dialog opened with no URL - reset state
            setCurrentLinkInfo(null);
        }
    }, [props.open, props.currentURL]);
    const handleURLChanged = useCallback((info: LinkTargetInfo) => {
        setCurrentLinkInfo(info);
    }, []);

    const handleOK = useCallback(() => {
        if (currentLinkInfo && onSelect) {
            onSelect(currentLinkInfo);
        }
    }, [currentLinkInfo, onSelect]);

    const handleCancel = useCallback(() => {
        if (onCancel) {
            onCancel();
        }
    }, [onCancel]);

    const handleDialogClose = useCallback(
        (_event?: object, _reason?: "escapeKeyDown" | "backdropClick") => {
            handleCancel();
        },
        [handleCancel],
    );

    const handleDialogCancel = useCallback(
        (
            _reason?:
                | "escapeKeyDown"
                | "backdropClick"
                | "titleCloseClick"
                | "cancelClicked",
        ) => {
            handleCancel();
        },
        [handleCancel],
    );

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
            onClose={handleDialogClose}
            onCancel={handleDialogCancel}
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
                <DialogCancelButton />
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
