/** @jsx jsx **/
import { jsx, css } from "@emotion/react";
import * as React from "react";
import { useApiBoolean } from "../utils/bloomApi";
import { Div } from "./l10nComponents";
import { kBloomBlue } from "../bloomMaterialUITheme";
import { LightTooltip } from "./lightTooltip";
import { kBloomDisabledOpacity } from "../utils/colorUtils";
import { UniformInlineIcon } from "./BloomCheckBox";

// A localized label that may show a tick mark next to it
export const TickableBox: React.FunctionComponent<{
    english: string;
    l10nKey: string;
    l10nComment?: string;
    ticked: boolean;
    icon?: React.ReactNode;
    // There is no behavior to actually disable, but this gives it an appearance
    // similar to MUI disabled controls.
    disabled?: boolean;
    title?: React.ReactNode;
}> = props => {
    const checkMarkString: string = "\u2713"; // elsewhere we used \u10004 but not sure how to do 5 digits in Javascript;

    const mainContent = (
        <div
            css={css`
                display: flex;
                align-items: center;
                // Less than this causes the size to change when the check mark appears.
                // I wish I had a more principled way to prevent that. I think the
                // problem is that the 'larger' font size of the check mark makes that
                // box higher than the simple label. I tried displaying a space when
                // not displaying the check mark, but that didn't help.
                min-height: 26px;
                ${props.disabled ? `opacity: ${kBloomDisabledOpacity};` : ""}
            `}
        >
            <div
                css={css`
                    width: 28px; // Seems to line things up in a column with Mui check boxes
                    color: ${kBloomBlue};
                    font-size: larger;
                `}
            >
                {props.ticked && checkMarkString}
            </div>

            <UniformInlineIcon icon={props.icon} />

            <Div
                css={css`
                    margin-left: 5px;
                `}
                l10nKey={props.l10nKey}
                l10nComment={props.l10nComment}
            >
                {props.english}
            </Div>
        </div>
    );

    return props.title ? (
        <LightTooltip title={props.title}>{mainContent}</LightTooltip>
    ) : (
        mainContent
    );
};
