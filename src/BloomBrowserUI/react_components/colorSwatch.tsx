import * as React from "react";
import { RGBColor } from "react-color";
import { CSSProperties } from "@material-ui/styles";

interface IColorSwatch {
    color: RGBColor;
    index: number;
    tooltip?: string;
    onClick: (index: number) => void;
}

export const ColorSwatch: React.FunctionComponent<IColorSwatch> = props => {
    const styleObject: CSSProperties = {
        backgroundColor: `rgba(${props.color.r},${props.color.g},${
            props.color.b
        },${props.color.a})`,
        width: 16,
        height: 16,
        borderRadius: 3,
        margin: "0, 10px, 10px, 0",
        boxShadow: "rgba(0, 0, 0, 0.15) 0px 0px 0px 1px inset"
    };
    return (
        <div
            className="color-swatch"
            style={styleObject}
            title={props.tooltip}
            onClick={() => props.onClick(props.index)}
        />
    );
};

export default ColorSwatch;
