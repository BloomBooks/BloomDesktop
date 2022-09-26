/** @jsx jsx **/
import { jsx, css } from "@emotion/core";
import * as React from "react";
import { useState } from "react";
import { ColorResult, RGBColor } from "react-color";
import BloomSketchPicker from "./bloomSketchPicker";
import ColorSwatch, { IColorInfo } from "./colorSwatch";
import * as tinycolor from "tinycolor2";
import { HexColorInput } from "./hexColorInput";

// We are combining parts of the 'react-color' component set with our own list of swatches.
// The reason for using our own swatches is so we can support swatches with gradients and alpha.
interface IColorPickerProps {
    // set to 'true' to eliminate alpha slider (e.g. text color)
    noAlphaSlider?: boolean;
    noGradientSwatches?: boolean;
    onChange: (color: IColorInfo) => void;
    currentColor: IColorInfo;
    swatchColors: IColorInfo[];
}

export const ColorPicker: React.FunctionComponent<IColorPickerProps> = props => {
    const [colorChoice, setColorChoice] = useState(props.currentColor);

    const changeColor = (swatchColor: IColorInfo) => {
        setColorChoice(swatchColor);
        props.onChange(swatchColor);
    };

    // Handler for when the user clicks on a swatch at the bottom of the picker.
    const handleSwatchClick = (swatchColor: IColorInfo) => () => {
        changeColor(swatchColor);
    };

    // Handler for when the user clicks/drags in the BloomSketchPicker (Saturation, Hue and Alpha).
    const handlePickerChange = (color: ColorResult) => {
        const newColor = getColorInfoFromColorResult(color, "");
        changeColor(newColor);
    };

    // Handler for when the user changes the hex code value (including pasting).
    const handleHexCodeChange = (hexColor: string) => {
        const newColor = {
            colors: [hexColor],
            opacity: colorChoice.opacity // Don't change opacity
        };
        changeColor(newColor);
    };

    const getColorInfoFromColorResult = (
        color: ColorResult,
        customName: string
    ): IColorInfo => {
        // A color that comes from a react-color component (not from clicking on a swatch),
        // cannot be a gradient.
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

    const getRgbaOfCurrentColor = (): RGBColor => {
        const rgbColor = tinycolor(colorChoice.colors[0]).toRgb();
        rgbColor.a = colorChoice.opacity;
        return rgbColor;
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

    return (
        <div
            className="custom-color-picker"
            css={css`
                display: flex;
                align-items: center;
                flex-direction: column;
                overflow-x: hidden;
            `}
        >
            <BloomSketchPicker
                noAlphaSlider={props.noAlphaSlider}
                // if the current color choice happens to be a gradient, this will be 'white'.
                color={getRgbaOfCurrentColor()}
                onChange={handlePickerChange}
                currentOpacity={colorChoice.opacity}
            />
            <div
                css={css`
                    height: 26px;
                    width: 100%;
                    margin-top: 16px;
                    display: flex;
                    flex-direction: row;
                    justify-content: space-between;
                `}
            >
                <HexColorInput
                    initial={colorChoice}
                    onChangeComplete={handleHexCodeChange}
                />
                <ColorSwatch
                    colors={colorChoice.colors}
                    opacity={colorChoice.opacity}
                    width={48}
                    height={26}
                />
            </div>
            <div
                css={css`
                    margin-top: 20px;
                    display: flex;
                    flex: 2;
                    flex-direction: row;
                    flex-wrap: wrap;
                    padding: 0 0 0 8px;
                    max-width: 209px; // 225px less margin and padding of 8px each
                `}
                className="swatch-row"
            >
                {getColorSwatches()}
            </div>
        </div>
    );
};

export default ColorPicker;
