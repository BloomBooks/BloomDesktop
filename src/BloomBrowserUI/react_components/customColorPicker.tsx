import * as React from "react";
import {
    CustomPicker,
    ColorChangeHandler,
    HSLColor,
    RGBColor,
    Color,
    ColorResult
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
import * as tinycolor from "tinycolor2";
import "./customColorPicker.less";

// This is a bit tricky. React-color's 'CustomPicker' wraps our 'CustomColorPicker'
// react component. This means that from the outside we feed in a 'color' prop that
// can be either a hex string, an rgb object, or an hsl object. Inside this wrapped
// component, we have all three types of color definitions available as props.
interface ICustomPicker {
    // set to 'true' to eliminate alpha slider
    noAlphaSlider?: boolean;
    onChange: (color: Color | ColorResult) => void;
    hex?: string;
    rgb?: RGBColor;
    hsl?: HSLColor;
    swatchColors: ISwatchDefn[];
}

export interface ISwatchDefn {
    name: string;
    // hex color; only the first element of the array is used here, but it's easier to maintain
    // the array than make a different single string property that can't be shared
    // between the two interfaces.
    colors?: string[];
    opacity?: number;
}

export const CustomColorPicker: React.FunctionComponent<
    ICustomPicker
> = props => {
    const swatchRgbColors: RGBColor[] = props.swatchColors.map(swatchDef => {
        const hexString = (swatchDef.colors as string[])[0];
        const opacity = swatchDef.opacity ? swatchDef.opacity : 1;
        const rgb = tinycolor(hexString).toRgb();
        rgb.a = opacity;
        return rgb;
    });

    const handleColorChange: ColorChangeHandler = (color, event) =>
        props.onChange(color);

    let nextCustomCounter = 1;
    const getToolTip = (rgb: RGBColor): string => {
        const color = tinycolor(rgb);
        const keyword = color.toName();
        return keyword
            ? keyword.toLocaleUpperCase()
            : `Custom${nextCustomCounter++}`;
    };
    const getSwatchColors = () => (
        <>
            {swatchRgbColors.map((rgb: RGBColor, i: number) => (
                <ColorSwatch
                    color={rgb}
                    key={i}
                    index={i}
                    tooltip={getToolTip(rgb)}
                    onClick={handleColorChange}
                />
            ))}
        </>
    );

    const currentRgbaColorString = `rgba(${props.rgb!.r},${props.rgb!.g},${
        props.rgb!.b
    },${props.rgb!.a})`;

    return (
        <div className="custom-color-picker">
            <div className="saturation-block">
                <Saturation
                    {...props}
                    onChange={handleColorChange}
                    pointer={CirclePointer}
                />
            </div>
            <div className="check-hue-alpha-row">
                <div className="check-section">
                    <div
                        className="checkboard-block"
                        style={{
                            color: currentRgbaColorString
                        }}
                    >
                        <Checkboard />
                        <div
                            className="current-color-spot"
                            style={{
                                background: currentRgbaColorString
                            }}
                        />
                    </div>
                </div>
                <div className="hue-alpha-section">
                    <div className="hue-row">
                        <Hue
                            {...props}
                            pointer={SolidCircleSlider}
                            direction="horizontal"
                            onChange={handleColorChange}
                        />
                    </div>
                    {!props.noAlphaSlider && (
                        <div className="alpha-row">
                            <Alpha
                                {...props}
                                pointer={SolidCircleSlider}
                                onChange={handleColorChange}
                            />
                        </div>
                    )}
                </div>
            </div>
            <div className="edit-input">
                <EditableInput
                    label="hex"
                    value={props.hex}
                    onChange={handleColorChange}
                />
            </div>
            <div className="swatch-row">{getSwatchColors()}</div>
        </div>
    );
};

export default CustomPicker(CustomColorPicker);
