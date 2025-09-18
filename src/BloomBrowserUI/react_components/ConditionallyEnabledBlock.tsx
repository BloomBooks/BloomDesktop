import { css } from "@emotion/react";
import * as React from "react";

// when disabled, this prevents interacting with children and partially hides them
export const ConditionallyEnabledBlock: React.FunctionComponent<{
    enable: boolean;
}> = (props) => {
    return (
        <div
            css={css`
                position: relative; // set bounds of the overlay
            `}
        >
            {!props.enable && (
                <div
                    css={css`
                        background-color: rgb(255 255 255 / 75%);
                        position: absolute;
                        left: 0;
                        right: 0;
                        top: 0;
                        bottom: 0;
                        z-index: 1000;
                    `}
                />
            )}

            {props.children}
        </div>
    );
};
