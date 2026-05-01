import { css } from "@emotion/react";
import * as React from "react";
import { useState } from "react";
import tinycolor from "tinycolor2";
import { IColorInfo } from "./colorSwatch";

interface IHexColorInputProps {
    initial: IColorInfo;
    // Called when the user completes a valid hex color entry.
    // fromBlankOpacity is true if the entry was a 7-character hex code (implying full opacity).
    onChangeComplete: (newValue: IColorInfo, fromBlankOpacity: boolean) => void;
    includeOpacityChannel?: boolean;
    // If true, omit the FF opacity suffix when displaying a fully opaque color.
    // The client controls this based on whether the last color change came from
    // a 7-character hex entry (user typing) or from an external source (swatch, eyedropper, etc.).
    displayOpaqueAsBlank?: boolean;
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
    displayOpaqueAsBlank?: boolean,
): string => {
    // First, our hex value will be empty, if we're dealing with a gradient.
    // The massage method below will add a hash character...
    if (colorInfo.colors.length > 1) return "";
    const firstColor = colorInfo.colors[0];
    const hexColor = tinycolor(firstColor).toHexString();

    if (!includeOpacityChannel) {
        return hexColor;
    }

    if (displayOpaqueAsBlank && colorInfo.opacity === 1) {
        return hexColor;
    }

    const alphaHex = Math.round(colorInfo.opacity * 255)
        .toString(16)
        .padStart(2, "0")
        .toUpperCase();
    return `${hexColor}${alphaHex}`;
};

export const isCompleteHexColorInput = (
    color: string,
    includeOpacityChannel?: boolean,
): boolean => {
    if (!includeOpacityChannel) {
        return color.length === 7;
    }

    return color.length === 7 || color.length === 9;
};

export const getColorInfoFromHexCodeChange = (
    hexColor: string,
    includeOpacityChannel?: boolean,
): IColorInfo => {
    if (includeOpacityChannel && /^#[0-9A-Fa-f]{8}$/.test(hexColor)) {
        return {
            colors: [hexColor.substring(0, 7)],
            opacity: parseInt(hexColor.substring(7, 9), 16) / 255,
        };
    }

    return {
        colors: [hexColor],
        opacity: 1,
    };
};

interface IHexColorInputEditorProps {
    initialHexValue: string;
    includeOpacityChannel?: boolean;
    onChangeComplete: (newValue: IColorInfo, fromBlankOpacity: boolean) => void;
}

const HexColorInputEditor: React.FunctionComponent<
    IHexColorInputEditorProps
> = (props) => {
    const [currentColor, setCurrentColor] = useState(
        () => props.initialHexValue,
    );

    const handleInputChange: React.ChangeEventHandler<HTMLInputElement> = (
        e,
    ) => {
        const result = massageColorInput(
            e.target.value,
            props.includeOpacityChannel,
        );
        setCurrentColor(result);
        if (isCompleteHexColorInput(result, props.includeOpacityChannel)) {
            // fromBlankOpacity is true if a 7-character hex was entered with opacity enabled
            const fromBlankOpacity =
                !!props.includeOpacityChannel && result.length === 7;
            props.onChangeComplete(
                getColorInfoFromHexCodeChange(
                    result,
                    props.includeOpacityChannel,
                ),
                fromBlankOpacity,
            );
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

// This component wraps the original, renamed HexColorInput so that it can be
// re-mounted with a new initialHexValue (by key change) whenever the initial
// color changes from an external source. This lets us avoid a useEffect.
export const HexColorInput: React.FunctionComponent<IHexColorInputProps> = (
    props,
) => {
    const initialHexValue = massageColorInput(
        getHexColorValueFromColorInfo(
            props.initial,
            props.includeOpacityChannel,
            props.displayOpaqueAsBlank,
        ),
        props.includeOpacityChannel,
    );

    return (
        <HexColorInputEditor
            key={initialHexValue}
            initialHexValue={initialHexValue}
            includeOpacityChannel={props.includeOpacityChannel}
            onChangeComplete={props.onChangeComplete}
        />
    );
};
