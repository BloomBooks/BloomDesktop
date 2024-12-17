/** @jsx jsx **/
import { jsx, css } from "@emotion/react";
import {
    BloomDialog,
    BloomDialogContext,
    DialogMiddle,
    DialogTitle
} from "../react_components/BloomDialog/BloomDialog";
import {
    IBloomDialogEnvironmentParams,
    useSetupBloomDialog
} from "../react_components/BloomDialog/BloomDialogPlumbing";
import { WireUpForWinforms } from "../utils/WireUpWinform";
import { CollectionChooser } from "./CollectionChooser";
import { ICollectionInfo } from "./CollectionCard";

interface IProps {
    open: boolean;
    onClose: () => void;
    collections?: ICollectionInfo[];
    dialogEnvironment?: IBloomDialogEnvironmentParams;
}

export const CollectionChooserDialog: React.FunctionComponent<IProps> = props => {
    const { propsForBloomDialog } = useSetupBloomDialog(
        props.dialogEnvironment
    );
    return (
        <BloomDialog
            {...propsForBloomDialog}
            open={props.open}
            onClose={props.onClose}
            maxWidth={false}
        >
            <BloomDialogContext.Provider
                value={{
                    onCancel: props.onClose,
                    disableDragging: true
                }}
            >
                <DialogTitle
                    title={"Open / Create Collections"}
                    icon={"BloomIcon.svg"}
                />
            </BloomDialogContext.Provider>
            <DialogMiddle>
                <CollectionChooser collections={props.collections} />
            </DialogMiddle>
        </BloomDialog>
    );
};

WireUpForWinforms(CollectionChooserDialog);
