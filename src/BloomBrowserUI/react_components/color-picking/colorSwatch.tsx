import { css } from "@emotion/react";
import * as React from "react";
import { Checkboard } from "react-color/lib/components/common";
import tinycolor from "tinycolor2";

// External definition of a color swatch
export interface IColorInfo {
    // Usually Hex colors
    // We use an array here, so we can support gradients (top to bottom).
    colors: string[];
    name?: string;
    opacity: number;
}

// More complete definition we need to pass in for handling swatch display.
export interface IClickableColorSwatch extends IColorInfo {
    onClick?: React.MouseEventHandler<IClickableColorSwatch>;
    width?: number;
    height?: number;
}

export const ColorSwatch: React.FunctionComponent<IClickableColorSwatch> = (
    props: IClickableColorSwatch,
) => {
    const swatchWidth = props.width ? props.width : 20;
    const swatchHeight = props.height ? props.height : 20;

    const handleSwatchClick = (e: React.MouseEvent<HTMLDivElement>): void => {
        // This cast handles the change in types, but we don't use the event in any case.
        const castEvent =
            e as unknown as React.MouseEvent<IClickableColorSwatch>;
        if (props.onClick) props.onClick(castEvent);
    };

    return (
        <div
            css={css`
                width: ${swatchWidth}px;
                height: ${swatchHeight}px;
                border-radius: 3px;
                margin: 0 0 8px 8px;
                position: relative;
            `}
            className="color-swatch"
        >
            <Checkboard grey="#aaa" />
            <div
                css={css`
                    background: ${getBackgroundColorCssFromColorInfo(props)};
                    box-shadow: rgba(0, 0, 0, 0.15) 0px 0px 0px 1px inset;
                    position: absolute;
                    width: ${swatchWidth}px;
                    height: ${swatchHeight}px;
                `}
                onClick={handleSwatchClick}
            />
        </div>
    );
};

export const getBackgroundColorCssFromColorInfo = (
    colorInfo: IColorInfo,
): string => {
    const baseColor = colorInfo.colors; // An array of strings representing colors

    // 'initialColorString' will be 'gradient' if props.color represents a gradient (2 colors).
    // Otherwise, it could be a name of a color (OldLace) or a hex value starting with '#'
    const initialColorString =
        baseColor.length === 1 ? baseColor[0] : "gradient";
    if (colorInfo.opacity === 0.0) {
        return "transparent";
    }
    // 'backgroundString' will end up being a named color, a linear-gradient string,
    // or an rgba string (with possible opacity values).
    let backgroundString: string = initialColorString;
    if (initialColorString.startsWith("#")) {
        const rgb = tinycolor(initialColorString).toRgb();
        backgroundString = `rgba(${rgb.r}, ${rgb.g}, ${rgb.b}, ${colorInfo.opacity})`;
    }
    if (initialColorString === "gradient") {
        backgroundString =
            "linear-gradient(" + baseColor[0] + ", " + baseColor[1] + ")";
    }
    return backgroundString; // set this to the elements "background" CSS prop, NOT "background-color"
};

// Handles all types of color strings: special-named, hex, rgb(), or rgba().
// If colorSpec entails opacity, this string should be of the form "rgba(r, g, b, a)".
export const getColorInfoFromString = (colorSpec: string): IColorInfo => {
    // If colorSpec has transparency, the color will be an rgba() string.
    // We need to pull out the "opacity" and add it to the swatch here.
    const colorStruct = tinycolor(colorSpec);
    const opacity = colorStruct.getAlpha();
    // Complete transparency, though, is a special case.
    // We want to continue using 'transparent' as the color in that case.
    if (opacity === 0.0) {
        return {
            colors: ["transparent"],
            opacity: opacity,
        };
    }
    return {
        colors: [`#${colorStruct.toHex()}`],
        opacity: opacity,
    };
};

// The purpose of this is to get the colors array ready for persistence.
// It assumes the "a" of any rgba values is already captured in the opacity field.
// Thus all hex values are 6 digits.
export function normalizeColorInfoColorsAsHex(colorInfo: IColorInfo): void {
    colorInfo.colors[0] = `#${tinycolor(colorInfo.colors[0]).toHex()}`;
    if (colorInfo.colors[1])
        colorInfo.colors[1] = `#${tinycolor(colorInfo.colors[1]).toHex()}`;
}

export default ColorSwatch;
