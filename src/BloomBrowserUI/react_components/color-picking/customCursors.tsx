// For use by BloomSketchPicker
/** @jsx jsx **/
import { jsx, css } from "@emotion/core";
import * as React from "react";

const CustomSliderCursor: React.FunctionComponent<{
    tip?: string;
}> = props => {
    const calculateLeftMargin = () => {
        if (!props.tip) return ""; // not gonna be used anyway
        // 0% - 9%
        if (props.tip.length === 2) return "-24";
        // 90% - 100%
        if (props.tip.length === 4 || props.tip[0] === "9") return "0";
        return props.tip[0] === "1" ? "-14" : "-12";
    };

    const tooltipString = props.tip
        ? `
        :after {
            position: absolute;
            margin-top: -24px;
            margin-left: ${calculateLeftMargin()}px;
            padding: 0 2px;
            background-color: white;
            border: 1px solid gray;
            border-radius: 4px;
            content: "${props.tip}";
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
