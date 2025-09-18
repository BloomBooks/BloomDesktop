import * as React from "react";
import * as ReactDOM from "react-dom";
import { useL10n } from "./l10nHooks";
import { getEditTabBundleExports } from "../bookEdit/js/bloomFrames";
import { postBoolean } from "../utils/bloomApi";
import {
    BloomDialog,
    DialogBottomButtons,
    DialogMiddle,
    DialogTitle,
} from "./BloomDialog/BloomDialog";
import {
    IBloomDialogEnvironmentParams,
    Mode,
    useSetupBloomDialog,
} from "./BloomDialog/BloomDialogPlumbing";
import BloomButton from "./bloomButton";

export interface IConfirmDialogProps {
    title: string;
    titleL10nKey: string;
    message: string;
    messageL10nKey: string;
    confirmButtonLabel: string;
    confirmButtonLabelL10nKey: string;
    cancelButtonLabel?: string;
    cancelButtonLabelL10nKey?: string;
    onDialogClose: (result: DialogResult) => void;
    dialogEnvironment?: IBloomDialogEnvironmentParams;
}

export let showConfirmDialog: () => void = () => {
    console.error("showConfirmDialog is not set up yet.");
};

export const ConfirmDialog: React.FC<IConfirmDialogProps> = (props) => {
    const { showDialog, closeDialog, propsForBloomDialog } =
        useSetupBloomDialog(props.dialogEnvironment);

    showConfirmDialog = showDialog;

    React.useEffect(() => {
        if (props.dialogEnvironment?.mode === Mode.Edit)
            postBoolean("editView/setModalState", propsForBloomDialog.open);
    }, [props.dialogEnvironment?.mode, propsForBloomDialog.open]);

    const onClose = (result: DialogResult) => {
        closeDialog();
        props.onDialogClose(result);
    };

    return (
        <BloomDialog {...propsForBloomDialog}>
            <DialogTitle title={useL10n(props.title, props.titleL10nKey)} />
            <DialogMiddle>
                {useL10n(props.message, props.messageL10nKey)}
            </DialogMiddle>
            <DialogBottomButtons>
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
                    l10nKey={props.cancelButtonLabelL10nKey || "Common.Cancel"}
                    enabled={true}
                    onClick={() => onClose(DialogResult.Cancel)}
                    hasText={true}
                    variant="outlined"
                >
                    {props.cancelButtonLabel || "Cancel"}
                </BloomButton>
            </DialogBottomButtons>
        </BloomDialog>
    );
};

export enum DialogResult {
    Confirm,
    Cancel,
}

export const showConfirmDialogFromOutsideReact = (
    props: IConfirmDialogProps,
    container?: Element | null,
) => {
    doRender(props, container);
    showConfirmDialog();
};

const doRender = (props: IConfirmDialogProps, container?: Element | null) => {
    let modalContainer;
    if (container) modalContainer = container;
    else {
        modalContainer = getEditTabBundleExports().getModalDialogContainer();
        if (!props.dialogEnvironment) {
            props.dialogEnvironment = {
                dialogFrameProvidedExternally: false,
                initiallyOpen: false,
            };
        }
        props.dialogEnvironment.mode = Mode.Edit;
    }

    try {
        ReactDOM.render(<ConfirmDialog {...props} />, modalContainer);
    } catch (error) {
        console.error(error);
    }
};
