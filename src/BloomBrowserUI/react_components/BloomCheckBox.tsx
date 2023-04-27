/** @jsx jsx **/
import { jsx, css } from "@emotion/react";

import * as React from "react";
import { useState } from "react";
import { useTheme, Checkbox, FormControlLabel } from "@mui/material";
import { useL10n } from "./l10nHooks";
import { LightTooltip } from "./lightTooltip";
import { Check } from "@mui/icons-material";
import { kBloomDisabledOpacity } from "../utils/colorUtils";

// wrap up the complex material-ui checkbox in something simple and make it handle tristate
export const BloomCheckbox: React.FunctionComponent<{
    className?: string;
    label: string | React.ReactNode;
    l10nKey?: string;
    l10nComment?: string;
    checked: boolean | undefined;
    tristate?: boolean;
    disabled?: boolean;
    alreadyLocalized?: boolean;
    icon?: React.ReactNode;
    iconScale?: number;
    temporarilyDisableI18nWarning?: boolean;
    onCheckChanged: (v: boolean | undefined) => void;
    l10nParam0?: string;
    l10nParam1?: string;
    tooltipContents?: React.ReactNode;
    hideBox?: boolean; // used for when a control is *never* user-operable, but we're just showing a check mark or not
}> = props => {
    const theme = useTheme();
    const [previousTriState, setPreviousTriState] = useState<
        boolean | undefined
    >(props.checked);

    let labelStr: string;
    let labelL10nKey: string | null;
    if (typeof props.label === "string") {
        labelStr = props.label;
        if (props.l10nKey === undefined)
            throw new Error("l10nKey must be provided if label is a string");
        labelL10nKey = props.l10nKey;
    } else {
        labelStr = "";
        labelL10nKey = null; // null is a special value which causes useL10n not to ask the server for a translation
    }
    const localizedLabel = useL10n(
        labelStr,
        props.alreadyLocalized || props.temporarilyDisableI18nWarning
            ? null
            : labelL10nKey,
        props.l10nComment,
        props.l10nParam0,
        props.l10nParam1
    );

    // if the label is a string, we need to localize it. If it's a react node, we assume it's already localized.
    const labelComponent =
        typeof props.label === "string" ? localizedLabel : props.label;

    // If messing with the layout, be sure you didn't break this by checking the storybook story.
    const checkboxControl = (
        <div
            className="bloom-checkbox"
            css={css`
                display: flex;
                flex-direction: row;
                align-items: start;
            `}
        >
            {props.hideBox || (
                <Checkbox
                    css={css`
                        padding-top: 0; //  default is 9px, from somewhere (mui?)
                        padding-bottom: 0;
                        padding-left: 0;
                        // this is a bit of a mystery, but it is needed to get rid of that extra 2 pixels on the left
                        margin-left: -2px;
                    `}
                    className={props.className}
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
            )}
            {props.hideBox && (
                <div
                    css={css`
                        height: 0; // I don't understand this... in the devtools I can't tell why it has a larger height than its child, and I don't understand why this fixes it
                        width: 28px; // align with the rows that have an actual checkbox
                        color: ${theme.palette.primary.main};
                        svg {
                            transform: scale(0.8);
                            margin-left: -2px; // move so it's centered in the box
                        }
                        visibility: ${props.checked ? "visible" : "hidden"};
                    `}
                >
                    <Check />
                </div>
            )}

            <div
                css={css`
                    display: flex;
                    //align-items: baseline;
                    //padding-top: 10px;
                    ${props.icon && "margin-left: -8px;"}
                `}
            >
                {props.icon && (
                    <UniformInlineIcon
                        icon={props.icon}
                        iconScale={props.iconScale}
                        disabled={props.disabled}
                    />
                )}
                <div
                    className="bloom-checkbox-label" // this classname is to help overlay toolbox hack a fix
                    css={css`
                        ${props.disabled &&
                            `opacity: ${kBloomDisabledOpacity}`};
                        // this rule is about helping this to keep working even when font is small, as in the Overlay Tool
                        min-height: 15px;
                        //border: solid red 0.1px;
                    `}
                >
                    {labelComponent}
                </div>
            </div>
        </div>
    );

    const c = props.tooltipContents ? (
        <LightTooltip title={props.tooltipContents}>
            {checkboxControl}
        </LightTooltip>
    ) : (
        checkboxControl
    );

    return (
        <FormControlLabel
            css={css`
                padding-top: 10px; // maintain the default behavior for spacing
                margin-left: 0; // I don't understand why this is needed, but the default has 11px
            `}
            control={c}
            label=""
        />
    );
};

// wrap the icons so that they can be center-aligned with each other
const UniformInlineIcon: React.FunctionComponent<{
    icon?: React.ReactNode;
    iconScale?: number;
    disabled?: boolean;
}> = props => {
    const theme = useTheme();
    return (
        <div
            css={css`
                min-width: 20px;
                max-width: 20px;
                // the height here has to be about the same as the text or
                // else it sticks up. I could not figure out how to use something
                // that would grow to the right height when the label size changes
                // (or better, just not be tied in any way to the label size).
                height: 18px;
                //border: solid 0.1px red;
                display: flex;
                justify-content: center;
                margin-right: 4px;
                svg {
                    fill: ${
                        props.disabled ? "black" : theme.palette.primary.main
                    };
                    height: 100% !important;
                    ${props.iconScale !== undefined &&
                        `transform: scale(${props.iconScale});`} /* border: solid 0.1px purple; */
                    ${props.disabled && `opacity: ${kBloomDisabledOpacity}`}
                }
            `}
        >
            {props.icon}
        </div>
    );
};
