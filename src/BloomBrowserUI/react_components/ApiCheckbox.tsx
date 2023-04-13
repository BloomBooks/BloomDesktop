import * as React from "react";
import { BloomCheckbox } from "./BloomCheckBox";
import { useApiBoolean } from "../utils/bloomApi";

// A localized checkbox that is backed by a boolean API get/set
// This is a "uncontrolled component".
export const ApiCheckbox: React.FunctionComponent<{
    english: string;
    l10nKey: string;
    l10nComment?: string;
    l10nParam0?: string;
    apiEndpoint: string;
    disabled?: boolean;
    icon?: React.ReactNode;
    iconScale?: number;
    // If defined, the checkbox should have this value when disabled,
    // whatever value we get from the API.
    forceDisabledValue?: boolean;
    onChange?: () => void;
    title?: React.ReactNode;
}> = props => {
    const [checked, setChecked] = useApiBoolean(props.apiEndpoint, false);

    let showChecked = checked;
    if (props.disabled && props.forceDisabledValue !== undefined) {
        showChecked = props.forceDisabledValue;
    }

    return (
        <BloomCheckbox
            checked={showChecked}
            disabled={props.disabled}
            label={props.english}
            l10nKey={props.l10nKey}
            l10nComment={props.l10nComment}
            l10nParam0={props.l10nParam0}
            icon={props.icon}
            iconScale={props.iconScale}
            tooltipContents={props.title}
            onCheckChanged={(newState: boolean | undefined) => {
                setChecked(!!newState);
                if (props.onChange) {
                    props.onChange();
                }
            }}
        />
    );
};
