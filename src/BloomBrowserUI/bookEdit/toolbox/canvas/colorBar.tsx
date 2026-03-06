import { css } from "@emotion/react";
import * as React from "react";
import Typography from "@mui/material/Typography";
import ColorSwatch, {
    IColorInfo,
    getBackgroundColorCssFromColorInfo,
} from "../../../react_components/color-picking/colorSwatch";
import tinycolor from "tinycolor2";
import { CSSProperties } from "react";
import { useL10n } from "../../../react_components/l10nHooks";

export interface IColorBarProps {
    id: string;
    // if defined, 'text' will display over the color bar in either white or black,
    // depending on the color bar's "perceived brightness".
    text?: string;
    onClick: () => void;
    colorInfo: IColorInfo;
    isDefault?: boolean;
}
// Displays a color bar menu item with optional localizable text.
export const ColorBar: React.FunctionComponent<IColorBarProps> = (
    props: IColorBarProps,
) => {
    const defaultStyleLabel = useL10n(
        "Default for style",
        "EditTab.DirectFormatting.labelForDefaultColor",
    );

    const baseColor = props.colorInfo.colors; // An array of strings representing colors

    const backgroundColorString = getBackgroundColorCssFromColorInfo(
        props.colorInfo,
    );

    const toolboxPanelBackground = "#2e2e2e";

    const isDark = (colorString: string): boolean => {
        const color = tinycolor(colorString); // handles named colors, rgba() and hex strings!!!
        return color.isDark();
    };

    let textColor = "black";
    if (baseColor[1]) {
        if (isDark(baseColor[0]) || isDark(baseColor[1])) {
            textColor = "white";
        }
    } else {
        if (isDark(backgroundColorString)) {
            textColor = "white";
        }
    }

    const bloomToolboxWhite = "#d2d2d2";

    const barStyles: CSSProperties = {
        minHeight: 30,
        background: props.isDefault
            ? toolboxPanelBackground
            : backgroundColorString,
        border: "1px solid",
        borderColor: bloomToolboxWhite,
        borderRadius: 4,
        display: "flex",
        justifyContent: props.isDefault ? "flex-start" : "center",
    };

    const textStyles: CSSProperties = {
        color: textColor,
        paddingTop: 4,
    };

    const defaultColor = [backgroundColorString];

    const defaultImplementation: JSX.Element = (
        <div
            css={css`
                display: flex;
                flex-direction: row;
                gap: 6px;
                margin: auto 0 auto 6px;
                height: 17px;
                align-items: center;
            `}
        >
            <div
                css={css`
                    border: 1px solid ${bloomToolboxWhite};
                    box-sizing: border-box;
                    /* .color-swatch {
                        margin: 0;
                    } background below is temporary */
                    background: linear-gradient(
                        to top left,
                        ${toolboxPanelBackground} 0%,
                        ${toolboxPanelBackground} calc(50% - 0.8px),
                        ${bloomToolboxWhite} 50%,
                        ${toolboxPanelBackground} calc(50% + 0.8px),
                        ${toolboxPanelBackground} 100%
                    );
                    width: 14px;
                    height: 14px;
                `}
            >
                {/* <ColorSwatch
                    colors={defaultColor}
                    opacity={1}
                    width={25}
                    height={15}
                /> */}
            </div>
            <Typography
                css={css`
                    color: ${bloomToolboxWhite};
                    font-size: 0.9rem !important;
                `}
            >
                {defaultStyleLabel}
            </Typography>
        </div>
    );

    // 'MuiInput-formControl' may seem like an odd class to give my homegrown div, but some of the toolbox's
    // spacing rules depend on it being one of these.
    return (
        <div
            className="MuiInput-formControl"
            style={barStyles}
            onClick={props.onClick}
        >
            {props.isDefault && defaultImplementation}
            {!props.isDefault && props.text && (
                <Typography style={textStyles}>{props.text}</Typography>
            )}
        </div>
    );
};
