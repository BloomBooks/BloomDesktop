import { css } from "@emotion/react";
import * as React from "react";
import { FormControlLabel, Switch, SwitchProps } from "@mui/material";
import { useL10n } from "./l10nHooks";
import { kBloomBlue, kBloomGold } from "../bloomMaterialUITheme";

interface IProps extends SwitchProps {
    className?: string; // carry in the css props from the caller
    english?: string;
    l10nKey: string;
    highlightWhenChecked?: boolean;
    englishWhenChecked?: string;
    l10nKeyWhenChecked?: string;
}

// Displays a Switch control
export const BloomSwitch: React.FunctionComponent<IProps> = (props) => {
    const label = useL10n(props.english ?? "", props.l10nKey);

    // Rules of hooks mean we have to call useL10n even if we won't use the result.
    const possibleLabelWhenChecked = useL10n(
        props.englishWhenChecked ?? "",
        props.l10nKeyWhenChecked ?? props.l10nKey,
    );
    const labelWhenChecked = props.l10nKeyWhenChecked
        ? possibleLabelWhenChecked
        : label;

    const [internalChecked, setInternalChecked] = React.useState<boolean>(
        props.checked ?? props.defaultChecked ?? false,
    );

    const isControlled = props.checked !== undefined;
    const checked = isControlled ? props.checked : internalChecked;

    const {
        english: _english,
        l10nKey: _l10nKey,
        highlightWhenChecked: _highlightWhenChecked,
        englishWhenChecked: _englishWhenChecked,
        l10nKeyWhenChecked: _l10nKeyWhenChecked,
        ...switchProps
    } = props;

    const switchCss =
        checked &&
        (props.highlightWhenChecked
            ? kHighlightSwitchWhenCheckedCSS
            : kNormalStylingWhenCheckedCSS);

    return (
        <FormControlLabel
            value="end"
            control={
                // Without this empty div, the vertical alignment between the button
                // and the label is wrong (from muiRadio). Probably the default 'div' display: block
                // interrupts the default flex layout.
                <div>
                    <Switch
                        css={css`
                            margin-top: -1px !important;
                        `}
                        {...switchProps}
                        checked={checked}
                        onChange={(event, newChecked) => {
                            if (!isControlled) {
                                setInternalChecked(newChecked);
                            }
                            props.onChange?.(event, newChecked);
                        }}
                    />
                </div>
            }
            label={checked ? labelWhenChecked : label}
            labelPlacement="end"
            css={css`
                align-items: baseline !important; // !important needed to override MUI default (from muiRadio)
                ${switchCss}
            `}
            className={props.className} // carry in the css props from the caller
        />
    );
};

const kHighlightSwitchWhenCheckedCSS = `
    color: ${kBloomGold};
    .MuiSwitch-thumb {
        background-color: ${kBloomGold};
    }
    .MuiSwitch-track {
        background-color: ${kBloomGold} !important;
}
`;
const kNormalStylingWhenCheckedCSS = `
    .MuiSwitch-thumb {
        background-color: ${kBloomBlue};
    }
    .MuiSwitch-track {
        background-color: ${kBloomBlue} !important;
    }
`;
