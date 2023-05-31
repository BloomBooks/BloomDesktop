import * as React from "react";
import { FormControlLabel, Switch, SwitchProps } from "@mui/material";
import { useL10n } from "./l10nHooks";

interface IProps extends SwitchProps {
    english?: string;
    l10nKey: string;
}
// Displays a Switch control
export const BloomSwitch: React.FunctionComponent<IProps> = props => {
    const label = useL10n(props.english ?? "", props.l10nKey);
    return (
        <FormControlLabel
            value="end"
            control={<Switch {...props} />}
            label={label}
            labelPlacement="end"
        />
    );
};
