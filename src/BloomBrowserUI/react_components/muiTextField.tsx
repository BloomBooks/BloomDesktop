import * as React from "react";
import { TextField, TextFieldProps } from "@mui/material";

import { useL10n } from "./l10nHooks";
import { ILocalizationProps } from "./l10nComponents";

// wrap up the material-ui text field in something localizable
export const MuiTextField: React.FunctionComponent<
    ILocalizationProps &
        TextFieldProps & {
            label: string;
        }
> = (props) => {
    const localizedLabel = useL10n(
        props.label,
        props.alreadyLocalized || props.temporarilyDisableI18nWarning
            ? null
            : props.l10nKey,
        props.l10nComment,
        props.l10nParam0,
        props.l10nParam1,
    );

    const { label: _label, ...propsToPass } = props;

    return (
        <TextField
            label={localizedLabel}
            variant="outlined"
            InputLabelProps={{
                shrink: true,
            }}
            {...propsToPass}
        />
    );
};
