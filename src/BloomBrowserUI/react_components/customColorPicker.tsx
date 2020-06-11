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

export const CustomColorPicker: React.FunctionComponent<
    ICustomPicker
> = props => {
    const [colorChoice, setColorChoice] = useState(props.currentColor);
    const [nextCustomSwatchNumber, setNextCustomSwatchNumber] = useState(1);

    // Handler for when the user picks a color by manipulating the ChromePicker.
    // This handler may be 'hit' many times as sliders are manipulated, etc.
    // So we don't want to create a new custom name for this color until the picker closes.
    const handleColorChange: ColorChangeHandler = (color, event) => {
        const newColor = getSwatchDefnFromColorResult(color, "");
        setColorChoice(newColor);
        props.onChange(newColor);
    };

    // Handler for when the user clicks on a swatch at the bottom of the picker.
    const handleSwatchClick = (index: number) => (e: any) => {
        const swatch = props.swatchColors[index];
        setColorChoice(swatch);
        props.onChange(swatch);
    };

    const getNewCustomSwatchName = (): string => {
        const nextNumber = nextCustomSwatchNumber;
        setNextCustomSwatchNumber(nextNumber + 1);
        return `Custom${nextNumber}`;
    };

    // Caller should keep track of whether the color picker is open or closed.
    // When it is closing, caller should use this method to ask for the new final color,
    // as this guarantees a "CustomN" name for the new Swatch.
    // The new swatch should then find its way back into the props.swatchColors array for the next time.
    const getFinalColorChosen = (): ISwatchDefn => {
        const swatch = colorChoice;
        if (swatch.name === "") {
            swatch.name = getNewCustomSwatchName();
        }
        return swatch;
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

    const getToolTip = (name: string): string => {
        return name.toLocaleUpperCase();
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
                        tooltip={getToolTip(swatchDefn.name)}
                        onClick={handleSwatchClick(i)}
                        opacity={swatchDefn.opacity}
                    />
                ))}
        </>
    );

    const getRgbOfCurrentSwatch = (): RGBColor => {
        const currentSwatch = props.currentColor;
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

export const getSwatchFromHex = (
    hexColor: string,
    customName: string,
    opacity?: number
): ISwatchDefn => {
    return {
        name: customName,
        colors: [hexColor],
        opacity: opacity ? opacity : 1
    };
};

export default CustomColorPicker;
