import * as React from "react";
import {
    CustomPicker,
    ColorChangeHandler,
    HSLColor,
    RGBColor
} from "react-color";
import {
    Alpha,
    Checkboard,
    EditableInput,
    Hue,
    Saturation
} from "react-color/lib/components/common";
import ColorSwatch from "./colorSwatch";
import CirclePointer, { SolidCircleSlider } from "./circlePointer";
import "./customColorPicker.less";

// This is a bit tricky. React-color's 'CustomPicker' wraps our 'CustomColorPicker'
// react component. This means that from the outside we feed in a 'color' prop that
// can be either a hex string, an rgb object, or an hsl object. Inside this wrapped
// component, we have all three types of color definitions available as props.
interface ICustomPicker {
    onChange: () => ColorChangeHandler;
    hex?: string;
    rgb?: RGBColor;
    hsl?: HSLColor;
    swatchColors: RGBColor[];
}

export const CustomColorPicker: React.FunctionComponent<
    ICustomPicker
> = props => {
    const onSaturationChange: ColorChangeHandler = (color, event) => {
        //props.onChange;
    };
    const onHueChange: ColorChangeHandler = (color, event) => {
        //props.onChange;
    };
    const onAlphaChange: ColorChangeHandler = (color, event) => {
        //props.onChange;
    };
    const onInputChange: ColorChangeHandler = (color, event) => {
        //props.onChange;
    };

    const onSwatchClick = (index: number) => {
        //props.onChange(props.swatchColors[index]);
    };

    const getSwatchColors = () => (
        <>
            {props.swatchColors.map((rgb: RGBColor, i: number) => (
                <ColorSwatch
                    color={rgb}
                    key={i}
                    index={i}
                    onClick={onSwatchClick}
                />
            ))}
        </>
    );

    return (
        <div className="custom-color-picker">
            <div className="saturation-block">
                <Saturation
                    {...props}
                    onChange={onSaturationChange}
                    pointer={CirclePointer}
                />
            </div>
            <div className="check-hue-alpha-row">
                <div className="check-section">
                    <div
                        className="checkboard-block"
                        style={{
                            color: props.hex,
                            backgroundColor: `rgba(${props.rgb!.r},${
                                props.rgb!.g
                            },${props.rgb!.b},${props.rgb!.a})`
                        }}
                    >
                        <Checkboard />
                    </div>
                </div>
                <div className="hue-alpha-section">
                    <div className="hue-row">
                        <Hue
                            {...props}
                            pointer={SolidCircleSlider}
                            direction="horizontal"
                            onChange={onHueChange}
                        />
                    </div>
                    <div className="alpha-row">
                        <Alpha
                            {...props}
                            pointer={SolidCircleSlider}
                            onChange={onAlphaChange}
                        />
                    </div>
                </div>
            </div>
            <div className="edit-input">
                <EditableInput
                    label="hex"
                    value={props.hex}
                    onChange={onInputChange}
                />
            </div>
            <div className="swatch-row">{getSwatchColors()}</div>
        </div>
    );
};

export default CustomPicker(CustomColorPicker);
