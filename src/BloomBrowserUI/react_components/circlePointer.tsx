import * as React from "react";
import { CSSProperties } from "@material-ui/styles";

interface IPointerProps {
    filled?: boolean;
    color?: string; // let's just use a hex color string; defaults to white
    translateX?: string; // different usages require different adjustments here
}

export const CirclePointer: React.FunctionComponent<IPointerProps> = props => {
    const pointerColor = props.color ? props.color : "#fff";
    const fillColorString = props.filled ? pointerColor : "";
    const translateXAdjustment = props.translateX ? props.translateX : "-6px";
    const translation = `translate(${translateXAdjustment})`;

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

export const SolidCircleSlider = () => (
    <CirclePointer filled={true} translateX="-2px" />
);

export default CirclePointer;
