/** @jsx jsx **/
import { jsx, css } from "@emotion/core";
import * as React from "react";
import { useState } from "react";
import {
    SketchPicker,
    ColorChangeHandler,
    ColorResult,
    RGBColor
} from "react-color";
import ColorSwatch, { IColorInfo } from "./colorSwatch";
import * as tinycolor from "tinycolor2";
import "./customColorPicker.less";

// We are combining the 'react-color' ChromePicker with our own list of swatches. The reason for using our
// own swatches is so we can support swatches with gradients and alpha.
interface ICustomPicker {
    // set to 'true' to eliminate alpha slider (e.g. text color)
    noAlphaSlider?: boolean;
    noGradientSwatches?: boolean;
    onChange: (color: IColorInfo) => void;
    currentColor: IColorInfo;
    swatchColors: IColorInfo[];
}

export const CustomColorPicker: React.FunctionComponent<ICustomPicker> = props => {
    const [colorChoice, setColorChoice] = useState(props.currentColor);

    // Handler for when the user picks a color by manipulating the ChromePicker.
    // This handler may be 'hit' many times as sliders are manipulated, etc.
    const handleColorChange: ColorChangeHandler = (color, event) => {
        const newColor = getColorInfoFromColorResult(color, "");
        changeColor(newColor);
    };

    // Handler for when the user clicks on a swatch at the bottom of the picker.
    const handleSwatchClick = (swatchColor: IColorInfo) => (e: any) => {
        changeColor(swatchColor);
    };

    const changeColor = (swatchColor: IColorInfo) => {
        setColorChoice(swatchColor);
        props.onChange(swatchColor);
    };

    const getColorInfoFromColorResult = (
        color: ColorResult,
        customName: string
    ): IColorInfo => {
        // A color that comes from the ChromePicker (not from clicking on a swatch), cannot be a gradient.
        let opacity = color.rgb.a;
        // ColorResult (from react-color) CAN have undefined alpha in its RGBColor, so we just
        // check here and assume the color is opaque if the alpha channel is undefined.
        if (opacity === undefined) {
            opacity = 1.0;
        }
        let colorString = color.hex;
        if (opacity === 0.0) {
            colorString = "transparent";
        }
        return {
            name: customName,
            colors: [colorString],
            opacity: opacity
        };
    };

    const getColorSwatches = () => (
        <React.Fragment>
            {props.swatchColors
                .filter(colorInfo => {
                    if (props.noGradientSwatches) {
                        return colorInfo.colors.length === 1;
                    } else {
                        return true;
                    }
                })
                .filter(colorInfo => {
                    return props.noAlphaSlider ? colorInfo.opacity === 1 : true;
                })
                .map((colorInfo: IColorInfo, i: number) => (
                    <ColorSwatch
                        colors={colorInfo.colors}
                        name={colorInfo.name}
                        key={i}
                        onClick={handleSwatchClick(colorInfo)}
                        opacity={colorInfo.opacity}
                    />
                ))}
        </React.Fragment>
    );

    const getRgbOfCurrentColor = (): RGBColor => {
        const currentColor = colorChoice;
        const rgbColor = tinycolor(currentColor.colors[0]).toRgb();
        rgbColor.a = currentColor.opacity;
        return rgbColor;
    };

    return (
        <div className="custom-color-picker">
            <SketchPicker
                disableAlpha={props.noAlphaSlider}
                // if the current color choice happens to be a gradient, this will be 'white'.
                color={getRgbOfCurrentColor()}
                onChange={handleColorChange}
                // We do want to show a set of color presets, but as far as I could discover
                // the SketchPicker can't display gradients, so we'll leave out its own ones
                // and use our own.
                presetColors={[]}
                styles={{
                    default: {
                        picker: {
                            boxShadow: "unset"
                        }
                    }
                }}
            />
            <div
                css={css`
                    margin-top: 10px;
                `}
                className="swatch-row"
            >
                {getColorSwatches()}
            </div>
        </div>
    );
};

export default CustomColorPicker;
