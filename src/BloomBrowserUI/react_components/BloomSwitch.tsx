import { css } from "@emotion/react";
import * as React from "react";
import { FormControlLabel, Switch, SwitchProps } from "@mui/material";
import { useL10n } from "./l10nHooks";
import { kBloomGold } from "../bloomMaterialUITheme";

interface IProps extends SwitchProps {
    className?: string; // carry in the css props from the caller
    english?: string;
    l10nKey: string;
    highlightWhenTrue?: boolean;
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
            css={
                props.highlightWhenTrue &&
                props.checked &&
                kHighlightSwitchWhenTrueCSS
            }
            className={props.className} // carry in the css props from the caller
        />
    );
};

const kHighlightSwitchWhenTrueCSS = css`
    .MuiSwitch-thumb {
        background-color: ${kBloomGold};
    }
    // we want this, but it doesn't work because the track isn't
    // actually under the .Mui-Checked element. Sigh.
    .MuiSwitch-track {
        background-color: ${kBloomGold} !important;
    }
`;
