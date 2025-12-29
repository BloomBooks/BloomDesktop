import { css } from "@emotion/react";
import * as React from "react";
import { useState } from "react";
import { ColorPicker } from "../colorPicker";
import { IColorInfo } from "../colorSwatch";

export const ColorPickerManualHarness: React.FunctionComponent = () => {
    const [currentColor, setCurrentColor] = useState<IColorInfo>({
        colors: ["#E48C84"],
        opacity: 1,
    });

    const swatches: IColorInfo[] = [
        { colors: ["#E48C84"], opacity: 1 },
        { colors: ["#B58B4F"], opacity: 1 },
        { colors: ["#7E5A3C"], opacity: 1 },
        { colors: ["#F0E5D8"], opacity: 1 },
        { colors: ["#D9A6A0"], opacity: 1 },
        { colors: ["#8C6A5A"], opacity: 1 },
        { colors: ["#6D7A7B"], opacity: 1 },
        { colors: ["#F0D36E"], opacity: 1 },
        { colors: ["#85B2C2"], opacity: 1 },
    ];

    return (
        <div
            css={css`
                padding: 20px;
            `}
        >
            <ColorPicker
                currentColor={currentColor}
                swatchColors={swatches}
                transparency={false}
                onChange={(color) => {
                    setCurrentColor(color);
                }}
            />
        </div>
    );
};
