/** @jsx jsx **/
import { jsx, css } from "@emotion/core";
import * as React from "react";
import { ColorChangeHandler, ColorResult, CustomPicker } from "react-color";
import { Saturation, Hue, Alpha } from "react-color/lib/components/common";
import CustomSliderCursor from "./customCursors";

// This component combines 3 'react-color' components in the style that we want.
interface IBloomSketchPicker {
    // set to 'true' to eliminate alpha slider (e.g. text color)
    noAlphaSlider?: boolean;
    onChange: (color: ColorResult) => void;
}

const BloomSketchPicker: React.FunctionComponent<IBloomSketchPicker> = props => {
    // Handler for when the user picks a color by manipulating the react-color components.
    // This handler may be 'hit' many times as sliders are manipulated, etc.
    const handleColorChange: ColorChangeHandler = (color: ColorResult) => {
        props.onChange(color);
    };
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
                css={css`
                    ${commonComponentCss}
                    height: 180px;
                    :hover {
                        cursor: crosshair;
                    }
                `}
            >
                <Saturation {...props} onChange={handleColorChange} />
            </div>
            <div
                css={css`
                    ${commonComponentCss}
                    height: 14px;
                    margin-top: 14px;
                `}
            >
                <Hue
                    {...props}
                    onChange={handleColorChange}
                    pointer={CustomSliderCursor}
                />
            </div>
            {!props.noAlphaSlider && (
                <div
                    css={css`
                        ${commonComponentCss}
                        height: 14px;
                        margin-top: 6px;
                    `}
                >
                    <Alpha
                        {...props}
                        onChange={handleColorChange}
                        pointer={CustomSliderCursor}
                    />
                </div>
            )}
        </div>
    );
};

export default CustomPicker(BloomSketchPicker);
