/** @jsx jsx **/
import { jsx, css } from "@emotion/core";
import * as React from "react";
import { useEffect, useState } from "react";
import tinycolor = require("tinycolor2");
import { IColorInfo } from "./colorSwatch";

interface IHexColorInputProps {
    initial: IColorInfo;
    onChangeComplete: (newValue: string) => void;
}

const hashChar = "#";

export const HexColorInput: React.FunctionComponent<IHexColorInputProps> = props => {
    const [currentColor, setCurrentColor] = useState("");

    // In general, we want our Hex Color input to reflect the first value in the 'colors' array.
    // For our predefined gradients, however, we want the hex input to be empty.
    // And for named colors, we need to show the hex equivalent.
    const getHexColorValueFromColorInfo = (): string => {
        if (props.initial.colors.length > 1) return ""; // massage method below will add a hash character
        const firstColor = props.initial.colors[0];
        if (firstColor[0] === hashChar) return firstColor;
        return tinycolor(firstColor).toHexString();
    };

    const massageColorInput = (color: string): string => {
        let result = color.replace(/#/g, ""); // remove any hashes
        result = hashChar + result; // insert hash as the first character
        result = result.toUpperCase();
        result = result.replace(/[^#0-9A-F]/g, ""); // eliminate any non-hex characters
        if (result.length > 7) {
            result = result.slice(0, 7);
        }
        return result;
    };

    useEffect(() => {
        setCurrentColor(massageColorInput(getHexColorValueFromColorInfo()));
    }, [props.initial]);

    const handleInputChange: React.ChangeEventHandler<HTMLInputElement> = e => {
        const result = massageColorInput(e.target.value);
        setCurrentColor(result);
        if (result.length === 7) {
            props.onChangeComplete(result);
        }
    };

    const borderThickness = 3;
    const controlWidth = 84; // This width handles "#DDDDDD" as the maximum width input.
    const inputWidth = controlWidth - 2 * borderThickness;

    return (
        <div
            css={css`
                border: ${borderThickness}px solid lightgray;
                border-radius: 4px;
                width: ${controlWidth}px;
                height: 22px;
                background-color: white;
                padding: 2px;
            `}
        >
            <input
                css={css`
                    border: none;
                    width: ${inputWidth}px;
                    font-size: medium;
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
