import { css } from "@emotion/react";
import * as React from "react";
import { FormControlLabel, Switch, SwitchProps } from "@mui/material";
import { useL10n } from "./l10nHooks";
import { kBloomBlue, kBloomGold } from "../bloomMaterialUITheme";

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
                props.checked &&
                (props.highlightWhenTrue
                    ? kHighlightSwitchWhenTrueCSS
                    : kNormalStylingWhenTrueCSS)
            }
            className={props.className} // carry in the css props from the caller
        />
    );
};

const kHighlightSwitchWhenTrueCSS = css`
    color: ${kBloomGold};
    .MuiSwitch-thumb {
        background-color: ${kBloomGold};
    }
    .MuiSwitch-track {
        background-color: ${kBloomGold} !important;
    }
`;
const kNormalStylingWhenTrueCSS = css`
    .MuiSwitch-thumb {
        background-color: ${kBloomBlue};
    }
    .MuiSwitch-track {
        background-color: ${kBloomBlue} !important;
    }
`;
