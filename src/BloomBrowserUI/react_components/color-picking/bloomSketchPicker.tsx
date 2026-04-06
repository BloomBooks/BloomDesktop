import { css } from "@emotion/react";
import * as React from "react";
import { ColorResult, CustomPicker } from "react-color";
import { Saturation, Hue, Alpha } from "react-color/lib/components/common";
import CustomSliderCursor from "./customCursors";

interface IBloomSketchPickerProps {
    // Set to 'true' to eliminate alpha slider (e.g. text color)
    noAlphaSlider?: boolean;

    onChange: (color: ColorResult) => void;
    onChangeComplete?: (color: ColorResult) => void;

    // Needed for the Alpha slider percentage display.
    currentOpacity: number;
}

// This combines 3 'react-color' components to make our version of react-color's SketchPicker.
const BloomSketchPicker: React.FunctionComponent<IBloomSketchPickerProps> = (
    props,
) => {
    const opacityString = (props.currentOpacity * 100).toFixed(0) + "%";

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
                        margin-top: 6px;
                        display: flex;
                        align-items: center;
                        gap: 8px;
                    `}
                >
                    <div
                        className="alpha-slider-track"
                        css={css`
                            ${commonComponentCss}
                            height: 14px;
                            flex: 1 1 auto;
                        `}
                    >
                        <Alpha {...props} pointer={CustomSliderCursor} />
                    </div>
                    <span
                        className="alpha-slider-percentage"
                        css={css`
                            width: 34px;
                            flex: 0 0 34px;
                            text-align: right;
                            font-size: 12px;
                            line-height: 1;
                            color: #000;
                        `}
                    >
                        {opacityString}
                    </span>
                </div>
            )}
        </div>
    );
};

export default CustomPicker(BloomSketchPicker);
