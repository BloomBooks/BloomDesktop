import * as React from "react";
import { Checkboard } from "react-color/lib/components/common";
import { CSSProperties } from "@material-ui/styles";
import * as tinycolor from "tinycolor2";

// External definition of a color swatch
export interface ISwatchDefn {
    // Usually Hex colors
    // We use an array here, so we can support gradients (top to bottom).
    colors: string[];
    name?: string;
    opacity?: number;
}

// More complete definition we need to pass in for handling swatch display.
export interface IColorSwatch extends ISwatchDefn {
    onClick: React.MouseEventHandler<IColorSwatch>;
}

export const ColorSwatch: React.FunctionComponent<IColorSwatch> = (
    props: IColorSwatch
) => {
    const wrapperStyle: CSSProperties = {
        width: 20,
        height: 20,
        borderRadius: 3,
        margin: "0, 10px, 10px, 0",
        position: "relative"
    };

    const swatchStyle: CSSProperties = {
        background: getBackgroundFromSwatch(props),
        boxShadow: "rgba(0, 0, 0, 0.15) 0px 0px 0px 1px inset",
        position: "absolute",
        width: 20,
        height: 20
    };

    const handleSwatchClick = (e: React.MouseEvent<HTMLDivElement>): void => {
        // This cast handles the change in types, but we don't use the event in any case.
        const castEvent = (e as unknown) as React.MouseEvent<IColorSwatch>;
        props.onClick(castEvent);
    };

    return (
        <div style={wrapperStyle} className="color-swatch">
            <Checkboard grey="#aaa" />
            <div style={swatchStyle} onClick={handleSwatchClick} />
        </div>
    );
};

export const getBackgroundFromSwatch = (swatch: ISwatchDefn): string => {
    const baseColor = swatch.colors; // An array of strings representing colors

    // 'initialColorString' will be 'gradient' if props.color represents a gradient (2 colors).
    // Otherwise, it could be a name of a color (OldLace) or a hex value starting with '#'
    const initialColorString =
        baseColor.length === 1 ? baseColor[0] : "gradient";
    const opacity = swatch.opacity ? swatch.opacity : 1.0;
    // 'backgroundString' will end up being a named color, a linear-gradient string,
    // or an rgba string (with possible opacity values).
    let backgroundString: string = initialColorString;
    if (initialColorString.startsWith("#")) {
        const rgb = tinycolor(initialColorString).toRgb();
        backgroundString = `rgba(${rgb.r}, ${rgb.g}, ${rgb.b}, ${opacity})`;
    }
    if (initialColorString === "gradient") {
        backgroundString =
            "linear-gradient(" + baseColor[0] + ", " + baseColor[1] + ")";
    }
    return backgroundString; // set this to the elements "background" CSS prop, NOT "background-color"
};

export default ColorSwatch;
