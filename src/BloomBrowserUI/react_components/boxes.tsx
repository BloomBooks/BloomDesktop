import { css, jsx } from "@emotion/react";
import { useL10n } from "./l10nHooks";
import * as React from "react";
import {
    kDialogPadding,
    kBloomBlue,
    kBorderRadiusForSpecialBlocks,
    kBloomBlue50Transparent,
} from "../bloomMaterialUITheme";

import InfoIcon from "@mui/icons-material/Info";
import WarningIcon from "@mui/icons-material/Warning";
import ErrorIcon from "@mui/icons-material/Error";
import WaitIcon from "@mui/icons-material/HourglassEmpty";
import { kBloomDarkTextOverWarning, kBloomWarning } from "../utils/colorUtils";
import { Box, SvgIconPropsSizeOverrides } from "@mui/material";
import { OverridableStringUnion } from "@mui/types";

export const kErrorBoxColor = "#eb3941";
const kLightBlueBackground = "#faffff";

export const BoxWithIconAndText: React.FunctionComponent<{
    hasBorder?: boolean;
    color?: string;
    borderColor?: string;
    backgroundColor?: string;
    icon?: JSX.Element;
    bottomRightButton?: JSX.Element;
}> = (props) => {
    let border = css``;
    if (props.hasBorder) {
        border = css`
            border: solid 1px ${props.borderColor || kBloomBlue50Transparent};
        `;
    }
    const {
        hasBorder,
        color,
        borderColor,
        backgroundColor,
        icon,
        bottomRightButton,
        ...propsToPass
    } = props;
    const cssForIcon = css`
        margin-right: ${kDialogPadding};
    `;
    // React's cloneElement doesn't work with Emotion's css prop, so we have to do this.
    // See https://github.com/emotion-js/emotion/issues/1102.
    const cloneElement = (element, props) =>
        jsx(element.type, {
            key: element.key,
            ref: element.ref,
            ...element.props,
            ...props,
        });
    return (
        <Box
            sx={{ boxShadow: 1 }}
            css={css`
                display: flex;
                flex-direction: column;
                background-color: ${props.backgroundColor ||
                kLightBlueBackground};
                border-radius: ${kBorderRadiusForSpecialBlocks};
                padding: ${kDialogPadding};
                color: ${props.color || kBloomBlue};
                // The original version of this used p instead of div to get this spacing below.
                // But we want div so we have more flexibility with adding children.
                margin-block-end: 1em;
                ${border};
                a {
                    color: ${props.color || kBloomBlue};
                }
                position: relative; // For positioning of the button
            `}
            {...propsToPass} // allows defining more css rules from container
        >
            <div
                css={css`
                    display: flex;
                    flex-direction: row;
                `}
            >
                {props.icon ? (
                    cloneElement(props.icon, { css: cssForIcon })
                ) : (
                    <InfoIcon color="primary" css={cssForIcon} />
                )}
                <div
                    css={css`
                        flex-grow: 1;
                    `}
                >
                    {props.children}
                </div>
            </div>

            {props.bottomRightButton && (
                <div
                    css={css`
                        margin-top: 10px;
                        align-self: flex-end;
                        display: flex;
                        align-items: center;
                    `}
                >
                    {props.bottomRightButton}
                </div>
            )}
        </Box>
    );
};

export const NoteBoxSansBorder: React.FunctionComponent<IBoxProps> = (
    props,
) => {
    return <BoxWithIconAndText {...props}>{props.children}</BoxWithIconAndText>;
};

interface IBoxProps {
    l10Msg?: string;
    l10nKey?: string;
    // The bizarre type below is chosen to match the fontSize property of the SvgIconProps interface.
    iconSize?: OverridableStringUnion<
        "inherit" | "large" | "medium" | "small",
        SvgIconPropsSizeOverrides
    >;
    bottomRightButton?: JSX.Element;
}
export const NoteBox: React.FunctionComponent<IBoxProps> = (props) => {
    const localizedMessage = useL10n(props.l10Msg || "", props.l10nKey || null);
    return (
        <BoxWithIconAndText
            hasBorder={true}
            icon={<InfoIcon fontSize={props.iconSize} />}
            bottomRightButton={props.bottomRightButton}
            {...props}
        >
            {localizedMessage || props.children}
        </BoxWithIconAndText>
    );
};

export const WaitBox: React.FunctionComponent<IBoxProps> = (props) => {
    const localizedMessage = useL10n(props.l10Msg || "", props.l10nKey || null);
    return (
        <BoxWithIconAndText
            color="white"
            backgroundColor="#96668F"
            icon={<WaitIcon fontSize={props.iconSize} />}
            bottomRightButton={props.bottomRightButton}
            {...props}
        >
            {localizedMessage || props.children}
        </BoxWithIconAndText>
    );
};

export const WarningBox: React.FunctionComponent<IBoxProps> = (props) => {
    const localizedMessage = useL10n(props.l10Msg || "", props.l10nKey || null);
    return (
        <BoxWithIconAndText
            color={kBloomDarkTextOverWarning}
            backgroundColor={kBloomWarning}
            icon={<WarningIcon fontSize={props.iconSize} />}
            bottomRightButton={props.bottomRightButton}
            {...props}
        >
            {localizedMessage || props.children}
        </BoxWithIconAndText>
    );
};

export const ErrorBox: React.FunctionComponent<IBoxProps> = (props) => {
    const localizedMessage = useL10n(props.l10Msg || "", props.l10nKey || null);
    return (
        <BoxWithIconAndText
            color="white"
            backgroundColor={kErrorBoxColor}
            icon={<ErrorIcon fontSize={props.iconSize} />}
            bottomRightButton={props.bottomRightButton}
            {...props}
        >
            {localizedMessage || props.children}
        </BoxWithIconAndText>
    );
};
