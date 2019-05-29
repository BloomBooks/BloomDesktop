import * as React from "react";
import { useState } from "react";
import { FormControlLabel, Checkbox } from "@material-ui/core";
import { useL10n } from "./l10nHooks";

// wrap up the complex material-ui checkbox in something simple and make it handle tristate
export const MuiCheckbox: React.FunctionComponent<{
    english: string;
    l10nKey: string;
    l10nComment?: string;
    checked: boolean | null;
    tristate?: boolean;
    disabled?: boolean;
    onCheckChanged: (v: boolean | null) => void;
}> = props => {
    const [previousTriState, setPreviousTriState] = useState<boolean | null>(
        props.checked
    );

    const localizedLabel = useL10n(
        props.english,
        props.l10nKey,
        props.l10nComment
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
                            let next: boolean | null = false;
                            switch (previousTriState) {
                                case null:
                                    next = false;
                                    break;
                                case true:
                                    next = null;
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
