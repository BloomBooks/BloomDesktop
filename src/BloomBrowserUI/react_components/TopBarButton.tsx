import { css, SerializedStyles } from "@emotion/react";
import * as React from "react";
import BloomButton from "./bloomButton";
import { BloomTooltip } from "./BloomToolTip";
import { kUiFontStack } from "../bloomMaterialUITheme";

export interface ITopBarButtonProps {
    iconPath: string;
    disabledIconPath?: string;
    labelL10nKey: string;
    labelEnglish: string;
    tooltipL10nKey?: string;
    tooltipEnglish?: string;
    enabled?: boolean;
    clickApiEndpoint?: string;
    onClick?: () => void;
    cssOverrides?: SerializedStyles;
    backgroundColor: string;
    textColor: string;
    disabledTextColor?: string;
}

export const TopBarButton: React.FunctionComponent<ITopBarButtonProps> = (
    props,
) => {
    const isEnabled = props.enabled ?? true;
    const disabledTextColor = props.disabledTextColor ?? props.textColor;
    const tooltipDefinition = props.tooltipL10nKey
        ? {
              l10nKey: props.tooltipL10nKey,
              english: props.tooltipEnglish ?? props.labelEnglish,
          }
        : undefined;

    const button = (
        <BloomButton
            enabled={isEnabled}
            l10nKey={props.labelL10nKey}
            onClick={props.onClick}
            clickApiEndpoint={
                props.onClick ? undefined : props.clickApiEndpoint
            }
            enabledImageFile={props.iconPath}
            disabledImageFile={
                props.disabledIconPath ? props.disabledIconPath : props.iconPath
            }
            transparent={true}
            hasText={true}
            css={[
                topBarButtonCss(
                    isEnabled,
                    props.backgroundColor,
                    props.textColor,
                    disabledTextColor,
                ),
                props.cssOverrides,
            ]}
        >
            {props.labelEnglish}
        </BloomButton>
    );

    if (!tooltipDefinition) {
        return button;
    }

    return (
        <BloomTooltip
            tip={tooltipDefinition}
            tipWhenDisabled={tooltipDefinition}
            showDisabled={!isEnabled}
        >
            {button}
        </BloomTooltip>
    );
};

const topBarButtonCss = (
    isEnabled: boolean,
    backgroundColor: string,
    textColor: string,
    disabledTextColor: string,
) => css`
    background-color: ${backgroundColor};
    color: ${isEnabled ? textColor : disabledTextColor};
    border: none;
    display: grid;
    grid-template-rows: 26px auto; // 26 is an experimentally-determined magic number which makes it look like the original winforms button
    justify-items: center;
    font-family: ${kUiFontStack};
    font-size: 11px;
    padding: 8px;
    cursor: ${isEnabled ? "pointer" : "default"};
`;
