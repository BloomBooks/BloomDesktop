import * as React from "react";
import { MuiCheckbox } from "./muiCheckBox";
import { BloomApi } from "../utils/bloomApi";

// A localized checkbox that is backed by a boolean API get/set
// This is a "uncontrolled component".
export const ApiCheckbox: React.FunctionComponent<{
    english: string;
    l10nKey: string;
    l10nComment?: string;
    apiEndpoint: string;
    disabled?: boolean;
    onChange?: () => void;
}> = props => {
    const [checked, setChecked] = BloomApi.useApiBoolean(
        props.apiEndpoint,
        false
    );

    return (
        <MuiCheckbox
            checked={checked}
            disabled={props.disabled}
            english={props.english}
            l10nKey={props.l10nKey}
            l10nComment={props.l10nComment}
            onCheckChanged={(newState: boolean | null) => {
                setChecked(!!newState);
                if (props.onChange) {
                    props.onChange();
                }
            }}
        />
    );
};
