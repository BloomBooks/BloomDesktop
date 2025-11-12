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
import { LinkTargetChooser } from "./LinkTargetChooser";
import { DialogCancelButton } from "../BloomDialog/commonDialogComponents";
import BloomButton from "../bloomButton";
import { useL10n } from "../l10nHooks";

export const LinkTargetChooserDialog: React.FunctionComponent<{
    open: boolean;
    currentURL: string;
    onCancel?: () => void;
    onSetUrl?: (url: string) => void;
}> = (props) => {
    const [currentUrl, setCurrentUrl] = useState<string>("");
    const [hasError, setHasError] = useState<boolean>(false);
    const [hasUserInteracted, setHasUserInteracted] = useState<boolean>(false);

    // Reset state when dialog opens/closes or currentURL changes
    useEffect(() => {
        if (props.open) {
            setCurrentUrl("");
            setHasError(false);
            setHasUserInteracted(false);
        }
    }, [props.open, props.currentURL]);

    const handleURLChanged = useCallback((url: string, hasError: boolean) => {
        setCurrentUrl(url);
        setHasError(hasError);
        setHasUserInteracted(true);
    }, []);

    const handleOK = useCallback(() => {
        if (props.onSetUrl) {
            props.onSetUrl(currentUrl.trim());
        }
    }, [currentUrl, props]);

    const handleCancel = useCallback(() => {
        if (props.onCancel) {
            props.onCancel();
        }
    }, [props]);

    const dialogTitle = useL10n(
        "Choose Link Target",
        "LinkTargetChooser.Dialog.Title",
        "Title of the dialog used to pick the destination for a link.",
    );

    return (
        <BloomDialog
            open={props.open}
            onClose={handleCancel}
            onCancel={handleCancel}
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
                <DialogCancelButton />
                <BloomButton
                    onClick={handleOK}
                    enabled={!hasError && hasUserInteracted}
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
