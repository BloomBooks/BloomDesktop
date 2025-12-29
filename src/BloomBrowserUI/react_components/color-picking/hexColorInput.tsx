import { css } from "@emotion/react";
import * as React from "react";
import { useEffect, useState } from "react";
import tinycolor from "tinycolor2";
import { IColorInfo } from "./colorSwatch";

interface IHexColorInputProps {
    initial: IColorInfo;
    onChangeComplete: (newValue: string) => void;
}

const hashChar = "#";

const massageColorInput = (color: string): string => {
    let result = color.toUpperCase();
    result = result.replace(/[^0-9A-F]/g, ""); // eliminate any non-hex characters
    result = hashChar + result; // insert hash as the first character
    if (result.length > 7) {
        result = result.slice(0, 7);
    }
    return result;
};

// In general, we want our Hex Color input to reflect the first value in the 'colors' array.
// For our predefined gradients, however, we want the hex input to be empty.
// And for named colors, we need to show the hex equivalent.
const getHexColorValueFromColorInfo = (colorInfo: IColorInfo): string => {
    // First, our hex value will be empty, if we're dealing with a gradient.
    // The massage method below will add a hash character...
    if (colorInfo.colors.length > 1) return "";
    const firstColor = colorInfo.colors[0];
    if (firstColor[0] === hashChar) return firstColor;
    // In some cases we might be dealing with a color word like "black" or "white" or "transparent".
    return tinycolor(firstColor).toHexString();
};

const getInitialHexValue = (colorInfo: IColorInfo): string => {
    return massageColorInput(getHexColorValueFromColorInfo(colorInfo));
};

export const HexColorInput: React.FunctionComponent<IHexColorInputProps> = (
    props,
) => {
    const [currentColor, setCurrentColor] = useState(() =>
        getInitialHexValue(props.initial),
    );

    const initialHexValue = getInitialHexValue(props.initial);

    // Keep the displayed hex string in sync when the parent changes the color programmatically
    // (e.g. swatch click, eyedropper, or external currentColor updates).
    useEffect(() => {
        setCurrentColor(initialHexValue);
    }, [initialHexValue]);

    const handleInputChange: React.ChangeEventHandler<HTMLInputElement> = (
        e,
    ) => {
        const result = massageColorInput(e.target.value);
        setCurrentColor(result);
        if (result.length === 7) {
            props.onChangeComplete(result);
        }
    };

    const borderThickness = 2;
    const controlWidth = 60; // This width handles "#DDDDDD" as the maximum width input.
    const inputWidth = controlWidth - 2 * borderThickness;

    return (
        <div
            css={css`
                border: ${borderThickness}px solid lightgray;
                border-radius: 4px;
                width: ${controlWidth}px;
                height: 18px;
                background-color: white;
                padding: 2px;
            `}
        >
            <input
                css={css`
                    border: none;
                    width: ${inputWidth}px;
                    font-size: 12px;
                    :focus {
                        outline: none; // get rid of blue outline inside of box when selected.
                    }
                `}
                type="text"
                value={currentColor}
                onChange={handleInputChange}
            />
        </div>
    );
};
