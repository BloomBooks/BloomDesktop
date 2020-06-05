import * as React from "react";
import { RGBColor, ColorChangeHandler, ColorResult } from "react-color";
import { CSSProperties } from "@material-ui/styles";
import * as tinycolor from "tinycolor2";

interface IColorSwatch {
    color: RGBColor;
    index: number;
    tooltip: string;
    onClick: ColorChangeHandler;
}

export const ColorSwatch: React.FunctionComponent<IColorSwatch> = props => {
    const styleObject: CSSProperties = {
        backgroundColor: `rgba(${props.color.r},${props.color.g},${
            props.color.b
        },${props.color.a})`,
        width: 20,
        height: 20,
        borderRadius: 3,
        margin: "0, 10px, 10px, 0",
        boxShadow: "rgba(0, 0, 0, 0.15) 0px 0px 0px 1px inset",
        zIndex: 1 // Necessary to get the tooltip to show on hover.
    };

    const swatchClick: React.MouseEventHandler = event => {
        const color = tinycolor(props.color);
        const colorResult: ColorResult = {
            rgb: color.toRgb(),
            hex: color.toHex(),
            hsl: color.toHsl()
        };

        // We don't actually use the 'event' param, but this is necessary to match the ColorChangeHandler signature.
        const castEvent = (event as unknown) as React.ChangeEvent<
            HTMLInputElement
        >;

        props.onClick(colorResult, castEvent);
    };

    return (
        <div
            className="color-swatch"
            style={styleObject}
            title={props.tooltip}
            onClick={swatchClick}
        />
    );
};

export default ColorSwatch;
