// For use by BloomSketchPicker
/** @jsx jsx **/
import { jsx, css } from "@emotion/core";
import * as React from "react";

const CustomSliderCursor: React.FunctionComponent<{
    tooltip?: string;
}> = props => {
    // Because I couldn't get the tooltip to show up outside the bounds of the containing element,
    // in this case the BloomSketchPicker, the tooltip location needs to be adjusted as we approach
    // the right and left margins of the container.
    // This may not be exact, but it works well even in scaled situations.
    const calculateLeftMargin = () => {
        if (!props.tooltip) return ""; // not gonna be used anyway
        // 0% - 9%
        if (props.tooltip.length === 2) return "-24";
        // 90% - 100%
        if (props.tooltip.length === 4 || props.tooltip[0] === "9") return "0";
        return props.tooltip[0] === "1" ? "-14" : "-12";
    };

    const tooltipString = props.tooltip
        ? `
        :after {
            position: absolute;
            margin-top: -24px;
            margin-left: ${calculateLeftMargin()}px;
            padding: 0 2px;
            background-color: white;
            border: 1px solid gray;
            border-radius: 4px;
            content: "${props.tooltip}";
        }
    `
        : "";

    return (
        <div
            css={css`
                width: 5px;
                height: 11px;
                background-color: white;
                border: 1px solid lightgray;
                border-radius: 2px;
                :hover {
                    cursor: ew-resize;
                    ${tooltipString}
                }
            `}
        />
    );
};

export default CustomSliderCursor;
