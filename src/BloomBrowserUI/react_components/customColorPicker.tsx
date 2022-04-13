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
import ColorSwatch, { ISwatchDefn } from "./colorSwatch";
import * as tinycolor from "tinycolor2";
import "./customColorPicker.less";

// We are combining the 'react-color' ChromePicker with our own list of swatches. The reason for using our
// own swatches is so we can support swatches with gradients and alpha.
interface ICustomPicker {
    // set to 'true' to eliminate alpha slider (e.g. text color)
    noAlphaSlider?: boolean;
    noGradientSwatches?: boolean;
    onChange: (color: ISwatchDefn) => void;
    currentColor: ISwatchDefn;
    swatchColors: ISwatchDefn[];
}

export const CustomColorPicker: React.FunctionComponent<ICustomPicker> = props => {
    const [colorChoice, setColorChoice] = useState(props.currentColor);

    // Handler for when the user picks a color by manipulating the ChromePicker.
    // This handler may be 'hit' many times as sliders are manipulated, etc.
    const handleColorChange: ColorChangeHandler = (color, event) => {
        const newColor = getSwatchDefnFromColorResult(color, "");
        changeColor(newColor);
    };

    // Handler for when the user clicks on a swatch at the bottom of the picker.
    const handleSwatchClick = (swatch: ISwatchDefn) => (e: any) => {
        changeColor(swatch);
    };

    const changeColor = (swatch: ISwatchDefn) => {
        setColorChoice(swatch);
        props.onChange(swatch);
    };

    const getSwatchDefnFromColorResult = (
        color: ColorResult,
        customName: string
    ): ISwatchDefn => {
        // A Swatch that comes from the ChromePicker (not from clicking on a swatch), cannot be a gradient.
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

    const getSwatchColors = () => (
        <React.Fragment>
            {props.swatchColors
                .filter(swatch => {
                    if (props.noGradientSwatches) {
                        return swatch.colors.length === 1;
                    } else {
                        return true;
                    }
                })
                .filter(swatch => {
                    return props.noAlphaSlider ? swatch.opacity === 1 : true;
                })
                .map((swatchDefn: ISwatchDefn, i: number) => (
                    <ColorSwatch
                        colors={swatchDefn.colors}
                        name={swatchDefn.name}
                        key={i}
                        onClick={handleSwatchClick(swatchDefn)}
                        opacity={swatchDefn.opacity}
                    />
                ))}
        </React.Fragment>
    );

    const getRgbOfCurrentSwatch = (): RGBColor => {
        const currentSwatch = colorChoice;
        const rgbColor = tinycolor(currentSwatch.colors[0]).toRgb();
        rgbColor.a = currentSwatch.opacity;
        return rgbColor;
    };

    return (
        <div className="custom-color-picker">
            <SketchPicker
                disableAlpha={props.noAlphaSlider}
                // if the current color choice happens to be a gradient, this will be 'white'.
                color={getRgbOfCurrentSwatch()}
                onChange={handleColorChange}
                // We do want to show a set of color presets, but as far as I could discover
                // the SketchPicker can't display gradients, so we'll leave out its own ones
                // and use our own.
                presetColors={[]}
            />
            <div
                css={css`
                    margin-top: 10px;
                `}
                className="swatch-row"
            >
                {getSwatchColors()}
            </div>
        </div>
    );
};

export default CustomColorPicker;
