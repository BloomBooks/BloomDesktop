import * as React from "react";
import { useState } from "react";
import { ColorPicker } from "../colorPicker";
import { IColorInfo } from "../colorSwatch";

export const ColorPickerTestHarness: React.FunctionComponent = () => {
    const [currentColor, setCurrentColor] = useState<IColorInfo>({
        colors: ["#111111"],
        opacity: 1,
    });

    const swatches: IColorInfo[] = [
        { colors: ["#AA0000"], opacity: 1 },
        { colors: ["#00AA00"], opacity: 1 },
        { colors: ["#0000AA"], opacity: 1 },
    ];

    return (
        <div>
            <button
                data-testid="simulate-external-color"
                onClick={() =>
                    setCurrentColor({ colors: ["#123456"], opacity: 1 })
                }
            >
                Simulate external color change
            </button>

            <div data-testid="current-color-key">
                {currentColor.colors.join("|") + "|" + currentColor.opacity}
            </div>

            <ColorPicker
                currentColor={currentColor}
                swatchColors={swatches}
                transparency={true}
                onChange={(color) => setCurrentColor(color)}
            />
        </div>
    );
};
