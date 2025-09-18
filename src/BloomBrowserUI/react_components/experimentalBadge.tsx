import { css } from "@emotion/react";
import * as React from "react";
import { kBloomDarkTextOverWarning, kBloomWarning } from "../utils/colorUtils";

export const ExperimentalBadge: React.FunctionComponent = (props) => {
    return (
        <div
            className={"avatar " + props["className"]}
            css={css`
                border-radius: 8px;
                background-color: ${kBloomWarning};
                color: ${kBloomDarkTextOverWarning};
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
