import { css } from "@emotion/react";
import * as React from "react";
import { ColorResult, CustomPicker } from "react-color";
import { Saturation, Hue, Alpha } from "react-color/lib/components/common";
import CustomSliderCursor from "./customCursors";

interface IBloomSketchPickerProps {
    // Set to 'true' to eliminate alpha slider (e.g. text color)
    noAlphaSlider?: boolean;

    onChange: (color: ColorResult) => void;

    // Needed for tooltip on Alpha slider
    currentOpacity: number;
}

// This combines 3 'react-color' components to make our version of react-color's SketchPicker.
const BloomSketchPicker: React.FunctionComponent<IBloomSketchPickerProps> = (
    props,
) => {
    const transparencyString =
        ((1 - props.currentOpacity) * 100).toFixed(0) + "%";

    const commonComponentCss = "position: relative;";

    return (
        <div
            css={css`
                display: flex;
                flex-direction: column;
                width: 225px;
                padding: 0 2px;
            `}
        >
            <div
                // The change of cursor is here, instead of in a custom cursor, so it shows up over the
                // entire saturation block, not just where the "dot" is.
                css={css`
                    ${commonComponentCss}
                    height: 156px;
                    :hover {
                        cursor: crosshair;
                    }
                `}
            >
                <Saturation {...props} />
            </div>
            <div
                css={css`
                    ${commonComponentCss}
                    height: 14px;
                    margin-top: 10px;
                `}
            >
                <Hue {...props} pointer={CustomSliderCursor} />
            </div>
            {!props.noAlphaSlider && (
                <div
                    css={css`
                        ${commonComponentCss}
                        height: 14px;
                        margin-top: 6px;
                    `}
                    title={transparencyString}
                >
                    <Alpha {...props} pointer={CustomSliderCursor} />
                </div>
            )}
        </div>
    );
};

export default CustomPicker(BloomSketchPicker);
