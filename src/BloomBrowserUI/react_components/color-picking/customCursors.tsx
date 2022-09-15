// For use by BloomSketchPicker
/** @jsx jsx **/
import { jsx, css } from "@emotion/core";
import * as React from "react";

const CustomSliderCursor: React.FunctionComponent = () => (
    <div
        css={css`
            width: 5px;
            height: 11px;
            background-color: white;
            border: 1px solid lightgray;
            border-radius: 2px;
            :hover {
                cursor: ew-resize;
            }
        `}
    />
);

export default CustomSliderCursor;
