import * as React from "react";
import { CSSProperties } from "@material-ui/styles";

interface IPointerProps {
    filled?: boolean;
    translateX?: string; // different usages require different adjustments here
    translateY?: string;
}

// CirclePointer is used in CustomColorPicker's Saturation block as the white circle pointer.
// See below for SolidCircleSlider, an specific type of CirclePointer used elsewhere.
export const CirclePointer: React.FunctionComponent<IPointerProps> = (
    props: IPointerProps
) => {
    const pointerColor = "white";
    const fillColorString = props.filled ? pointerColor : "";
    const translateXAdjustment = props.translateX ? props.translateX : "-8px";
    const translateYAdjustment = props.translateY ? props.translateY : "-8px";
    const translation = `translate(${translateXAdjustment}, ${translateYAdjustment})`;

    const styleObject: CSSProperties = {
        cursor: "pointer",
        height: 14,
        width: 14,
        borderRadius: 7,
        borderStyle: "solid",
        borderWidth: 1,
        borderColor: pointerColor,
        backgroundColor: fillColorString,
        transform: translation,
        boxShadow: "0 1px 4px 0 rgba(0, 0, 0, 0.37)"
    };

    return <div style={styleObject} />;
};

// SolidCircleSlider is used in CustomColorPicker's Hue and Alpha sliders as the solid white slider control.
export const SolidCircleSlider = () => (
    <CirclePointer filled={true} translateX="-6px" translateY="0" />
);

export default CirclePointer;
