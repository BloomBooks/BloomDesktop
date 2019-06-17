import * as React from "react";
import { useState } from "react";
import { FormControlLabel, Checkbox } from "@material-ui/core";
import { useL10n } from "./l10nHooks";

// wrap up the complex material-ui checkbox in something simple and make it handle tristate
export const MuiCheckbox: React.FunctionComponent<{
    label: string;
    l10nKey: string;
    l10nComment?: string;
    checked: boolean | undefined;
    tristate?: boolean;
    disabled?: boolean;
    alreadyLocalized?: boolean;
    onCheckChanged: (v: boolean | undefined) => void;
    l10nParam0?: string;
    l10nParam1?: string;
}> = props => {
    const [previousTriState, setPreviousTriState] = useState<
        boolean | undefined
    >(props.checked);

    const localizedLabel = useL10n(
        props.label,
        props.alreadyLocalized ? null : props.l10nKey,
        props.l10nComment,
        props.l10nParam0,
        props.l10nParam1
    );

    return (
        <FormControlLabel
            control={
                <Checkbox
                    disabled={props.disabled}
                    checked={!!props.checked}
                    indeterminate={props.checked == null}
                    //enhance; I would like  it to show a square with a question mark inside: indeterminateIcon={"?"}
                    onChange={(e, newState) => {
                        if (!props.tristate) {
                            props.onCheckChanged(newState);
                        } else {
                            let next: boolean | undefined = false;
                            switch (previousTriState) {
                                case null:
                                    next = false;
                                    break;
                                case true:
                                    next = undefined;
                                    break;
                                case false:
                                    next = true;
                                    break;
                            }
                            setPreviousTriState(next);
                            props.onCheckChanged(next);
                        }
                    }}
                    color="primary"
                />
            }
            label={localizedLabel}
        />
    );
};
