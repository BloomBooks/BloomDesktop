import React = require("react");
import * as ReactDOM from "react-dom";
import { useState } from "react";
import Dialog from "@material-ui/core/Dialog";
import { DialogTitle, DialogActions, DialogContent } from "@material-ui/core";
import CloseOnEscape from "react-close-on-escape";
import { useL10n } from "./l10nHooks";
import BloomButton from "./bloomButton";
import { getEditViewFrameExports } from "../bookEdit/js/bloomFrames";
import { BloomApi } from "../utils/bloomApi";

// All strings are assumed localized by the caller
export interface IConfirmDialogProps {
    title: string;
    titleL10nKey: string;
    message: string;
    messageL10nKey: string;
    confirmButtonLabel: string;
    confirmButtonLabelL10nKey: string;
    onDialogClose: (result: DialogResult) => void;
}

let externalSetOpen;

const ConfirmDialog: React.FC<IConfirmDialogProps> = props => {
    const [open, setOpen] = useState(true);
    externalSetOpen = setOpen;

    React.useEffect(() => {
        BloomApi.postBoolean("editView/setModalState", open);
    }, [open]);

    const onClose = (result: DialogResult) => {
        setOpen(false);
        props.onDialogClose(result);
    };

    return (
        <CloseOnEscape onEscape={() => onClose(DialogResult.Cancel)}>
            <Dialog className="bloomModalDialog confirmDialog" open={open}>
                <DialogTitle>
                    {useL10n(props.title, props.titleL10nKey)}
                </DialogTitle>
                <DialogContent>
                    {useL10n(props.message, props.messageL10nKey)}
                </DialogContent>
                <DialogActions>
                    <BloomButton
                        key="Confirm"
                        l10nKey={props.confirmButtonLabelL10nKey}
                        enabled={true}
                        onClick={() => onClose(DialogResult.Confirm)}
                        hasText={true}
                    >
                        {props.confirmButtonLabel}
                    </BloomButton>
                    <BloomButton
                        key="Cancel"
                        l10nKey="Common.Cancel"
                        enabled={true}
                        onClick={() => onClose(DialogResult.Cancel)}
                        hasText={true}
                        variant="outlined"
                    >
                        Cancel
                    </BloomButton>
                </DialogActions>
            </Dialog>
        </CloseOnEscape>
    );
};

export enum DialogResult {
    Confirm,
    Cancel
}

export const showConfirmDialog = (
    props: IConfirmDialogProps,
    container?: Element | null
) => {
    doRender(props, container);
    externalSetOpen(true);
};

const doRender = (props: IConfirmDialogProps, container?: Element | null) => {
    let modalContainer;
    if (container) modalContainer = container;
    else modalContainer = getEditViewFrameExports().getModalDialogContainer();
    ReactDOM.render(<ConfirmDialog {...props} />, modalContainer);
};
