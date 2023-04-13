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
    deprecatedVersionWhichDoesntEnsureMultilineLabelsWork?: boolean;
    temporarilyDisableI18nWarning?: boolean;
    onCheckChanged: (v: boolean | undefined) => void;
    l10nParam0?: string;
    l10nParam1?: string;
    tooltipContents?: React.ReactNode;
    hideBox?: boolean; // used for when a control is *never* user-operable, but we're just showin a check mark or not
}> = props => {
    const theme = useTheme();
    const [previousTriState, setPreviousTriState] = useState<
        boolean | undefined
    >(props.checked);

    let labelStr: string;
    let labelL10nKey: string;
    if (typeof props.label === "string") {
        labelStr = props.label;
        if (props.l10nKey === undefined)
            throw new Error("l10nKey must be provided if label is a string");
        labelL10nKey = props.l10nKey;
    } else {
        labelStr = "";
        labelL10nKey = "";
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

    const mainLabel =
        typeof props.label === "string" ? localizedLabel : props.label;

    // // Work has been done below to ensure that a wrapped label will align with the control correctly.
    // If messing with the layout, be sure you didn't break this by checking the storybook story.
    const checkboxControl = (
        <div
            css={css`
                display: flex;
                flex-direction: row;
                min-height: 39px; // ensures that the cases without an actual checkbox are the same minimum height as the ones with a checkbox
                align-items: start;
            `}
        >
            {props.hideBox || (
                <Checkbox
                    css={css`
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
                        width: 27px;
                        padding-top: 7px;
                        color: ${theme.palette.primary.main};
                        svg {
                            transform: scale(0.8);
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
                    padding-top: 10px;
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
                    css={css`
                        ${props.disabled && `opacity: ${kBloomDisabledOpacity}`}
                    `}
                >
                    {mainLabel}
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
                margin-left: 0; // I don't understand why this is needed, but he default has 11px
            `}
            control={c}
            label=""
        />
    );
};

// wrap the icons so that they can be center-aligned with each other
export const UniformInlineIcon: React.FunctionComponent<{
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
                // else it stick up. We have align-items: baseline on the parent, but
                // there isn't, like, an align-items:top.
                height: 18px;
                //overflow-y: clip;
                /* border: solid 0.1px red; */
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
