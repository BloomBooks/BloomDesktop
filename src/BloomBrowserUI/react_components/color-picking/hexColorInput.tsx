import { css } from "@emotion/react";
import * as React from "react";
import { useEffect, useState } from "react";
import tinycolor from "tinycolor2";
import { IColorInfo } from "./colorSwatch";

interface IHexColorInputProps {
    initial: IColorInfo;
    onChangeComplete: (newValue: string) => void;
    includeOpacityChannel?: boolean;
}

const hashChar = "#";

const massageColorInput = (
    color: string,
    includeOpacityChannel?: boolean,
): string => {
    let result = color.toUpperCase();
    result = result.replace(/[^0-9A-F]/g, ""); // eliminate any non-hex characters
    result = hashChar + result; // insert hash as the first character
    const maxLength = includeOpacityChannel ? 9 : 7;
    if (result.length > maxLength) {
        result = result.slice(0, maxLength);
    }
    return result;
};

// In general, we want our Hex Color input to reflect the first value in the 'colors' array.
// For our predefined gradients, however, we want the hex input to be empty.
// And for named colors, we need to show the hex equivalent.
const getHexColorValueFromColorInfo = (
    colorInfo: IColorInfo,
    includeOpacityChannel?: boolean,
): string => {
    // First, our hex value will be empty, if we're dealing with a gradient.
    // The massage method below will add a hash character...
    if (colorInfo.colors.length > 1) return "";
    const firstColor = colorInfo.colors[0];
    const hexColor = tinycolor(firstColor).toHexString();

    if (!includeOpacityChannel) {
        return hexColor;
    }

    const alphaHex = Math.round(colorInfo.opacity * 255)
        .toString(16)
        .padStart(2, "0")
        .toUpperCase();
    return `${hexColor}${alphaHex}`;
};

export const HexColorInput: React.FunctionComponent<IHexColorInputProps> = (
    props,
) => {
    const getHexValue = React.useCallback(
        (colorInfo: IColorInfo): string =>
            massageColorInput(
                getHexColorValueFromColorInfo(
                    colorInfo,
                    props.includeOpacityChannel,
                ),
                props.includeOpacityChannel,
            ),
        [props.includeOpacityChannel],
    );

    const [currentColor, setCurrentColor] = useState(() =>
        getHexValue(props.initial),
    );

    const initialHexValue = getHexValue(props.initial);

    // Keep the displayed hex string in sync when the parent changes the color programmatically
    // (e.g. swatch click, eyedropper, or external currentColor updates).
    useEffect(() => {
        setCurrentColor(initialHexValue);
    }, [initialHexValue]);

    const handleInputChange: React.ChangeEventHandler<HTMLInputElement> = (
        e,
    ) => {
        const result = massageColorInput(
            e.target.value,
            props.includeOpacityChannel,
        );
        setCurrentColor(result);
        const completeLength = props.includeOpacityChannel ? 9 : 7;
        if (result.length === completeLength) {
            props.onChangeComplete(result);
        }
    };

    const borderThickness = 2;
    const controlWidth = props.includeOpacityChannel ? 80 : 60;
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
