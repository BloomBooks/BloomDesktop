import { css } from "@emotion/react";
import * as React from "react";
import { useState, useEffect } from "react";
import { RadioGroup } from "@mui/material";
import { MuiRadio } from "../react_components/muiRadio";
import {
    BloomDialog,
    DialogTitle,
    DialogMiddle,
    DialogBottomButtons,
} from "../react_components/BloomDialog/BloomDialog";
import {
    DialogOkButton,
    DialogCancelButton,
} from "../react_components/BloomDialog/commonDialogComponents";

export interface IRadioChoice {
    value: string;
    label: string; // already localized
    description?: string; // optional already-localized secondary line shown under the label
    disabled?: boolean; // when true the choice can't be selected (e.g. not currently allowed)
}

// A small, reusable dialog that offers a set of mutually-exclusive radio choices with OK and
// Cancel. OK is disabled until the user selects one; Cancel is always available. It is rendered
// inline as part of its parent screen; the parent controls `open` and receives the chosen value
// (or undefined if the user cancelled) via `onClose`. All strings are passed in already localized.
export const RadioChoiceDialog: React.FunctionComponent<{
    open: boolean;
    title: string;
    message?: string;
    options: IRadioChoice[];
    onClose: (value?: string) => void;
}> = (props) => {
    const [choice, setChoice] = useState<string>();

    // Start with nothing selected each time the dialog opens, so OK is disabled until the user picks.
    useEffect(() => {
        if (props.open) setChoice(undefined);
    }, [props.open]);

    const cancel = () => props.onClose(undefined);

    return (
        <BloomDialog
            open={props.open}
            onClose={cancel}
            onCancel={cancel}
            dialogFrameProvidedExternally={false}
        >
            <DialogTitle title={props.title} />
            <DialogMiddle
                css={css`
                    width: 400px;
                `}
            >
                {props.message && (
                    <p
                        css={css`
                            margin-top: 0;
                        `}
                    >
                        {props.message}
                    </p>
                )}
                <RadioGroup
                    value={choice ?? ""}
                    onChange={(e) => setChoice(e.target.value)}
                >
                    {props.options.map((o) => (
                        <MuiRadio
                            key={o.value}
                            value={o.value}
                            label={o.label}
                            description={o.description}
                            disabled={o.disabled}
                            // The labels arrive already localized from the caller.
                            alreadyLocalized={true}
                            l10nKey=""
                        />
                    ))}
                </RadioGroup>
            </DialogMiddle>
            <DialogBottomButtons>
                <DialogOkButton
                    default={true}
                    enabled={choice !== undefined}
                    onClick={() => props.onClose(choice)}
                />
                <DialogCancelButton />
            </DialogBottomButtons>
        </BloomDialog>
    );
};
