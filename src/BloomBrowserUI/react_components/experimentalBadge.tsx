/** @jsx jsx **/
import { jsx, css } from "@emotion/core";
import React = require("react");

export const ExperimentalBadge: React.FunctionComponent<{}> = props => {
    return (
        <div
            className={"avatar " + props["className"]}
            css={css`
                border-radius: 8px;
                background-color: orange;
                color: white;
                padding: 5px;
                transform: rotate(15deg);
                margin-left: auto;
                font-weight: normal;
            `}
        >
            {/* Intentionally not localizing this. First, we rarely localize while something is experimental,
            and second, a longer translation is going to look really bad. */}
            Experimental
        </div>
    );
};
