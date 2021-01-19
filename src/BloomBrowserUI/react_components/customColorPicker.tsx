import * as React from "react";
import { useState } from "react";
import {
    ChromePicker,
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
        return {
            name: customName,
            colors: [color.hex],
            opacity: color.rgb.a
        };
    };

    const getSwatchColors = () => (
        <>
            {props.swatchColors
                .filter(swatch => {
                    if (props.noGradientSwatches) {
                        return swatch.colors.length === 1;
                    } else {
                        return true;
                    }
                })
                .filter(swatch => {
                    const opacity = swatch.opacity ? swatch.opacity : 1;
                    return props.noAlphaSlider ? opacity === 1 : true;
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
        </>
    );

    const getRgbOfCurrentSwatch = (): RGBColor => {
        const currentSwatch = colorChoice;
        const rgbColor = tinycolor(currentSwatch.colors[0]).toRgb();
        rgbColor.a = currentSwatch.opacity ? currentSwatch.opacity : 1;
        return rgbColor;
    };

    return (
        <div className="custom-color-picker">
            <ChromePicker
                disableAlpha={props.noAlphaSlider}
                // if the current color choice happens to be a gradient, this will be 'white'.
                color={getRgbOfCurrentSwatch()}
                onChange={handleColorChange}
            />
            <div className="swatch-row">{getSwatchColors()}</div>
        </div>
    );
};

export default CustomColorPicker;
