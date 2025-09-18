import { css } from "@emotion/react";
import * as React from "react";
import { useState } from "react";
import {
    BloomDialog,
    DialogBottomButtons,
    DialogMiddle,
    DialogTitle,
} from "./BloomDialog/BloomDialog";
import BloomButton from "./bloomButton";
import { DialogCancelButton } from "./BloomDialog/commonDialogComponents";
import SmallNumberPicker from "./smallNumberPicker";
import {
    IBloomDialogEnvironmentParams,
    useSetupBloomDialog,
} from "./BloomDialog/BloomDialogPlumbing";

export interface INumberChooserDialogProps {
    min: number;
    max: number;
    title: string;
    prompt: string;
    onClick: (chosen: number) => void;
    dialogEnvironment?: IBloomDialogEnvironmentParams;
}

// This component is intended to be wrapped by another function that passes a BloomApi
// onclick handler. For example, see duplicateManyDialog.tsx.
export const NumberChooserDialog: React.FunctionComponent<
    INumberChooserDialogProps
> = (props) => {
    const { showDialog, closeDialog, propsForBloomDialog } =
        useSetupBloomDialog(props.dialogEnvironment);

    const [numberChosen, setNumberChosen] = useState(props.min);

    return (
        <BloomDialog {...propsForBloomDialog}>
            <DialogTitle title={props.title} />
            <DialogMiddle>
                <div
                    css={css`
                        display: flex;
                        flex-direction: row;
                    `}
                >
                    <p>{props.prompt}</p>
                    <div
                        css={css`
                            margin-left: 8px;
                            margin-top: -5px;
                            max-width: 40px;
                        `}
                    >
                        <SmallNumberPicker
                            minLimit={props.min}
                            maxLimit={props.max}
                            handleChange={setNumberChosen}
                        />
                    </div>
                </div>
            </DialogMiddle>
            <DialogBottomButtons>
                <BloomButton
                    l10nKey="Common.OK"
                    hasText={true}
                    enabled={true}
                    variant={"contained"}
                    onClick={() => {
                        props.onClick(numberChosen);
                        closeDialog();
                    }}
                >
                    OK
                </BloomButton>
                <DialogCancelButton onClick_DEPRECATED={closeDialog} />
            </DialogBottomButtons>
        </BloomDialog>
    );
};
