import { css } from "@emotion/react";

import * as React from "react";
import { kBloomBlue } from "../../bloomMaterialUITheme";

export const ProgressBar: React.FunctionComponent<{
    percentage: number;
    animateWhileInProgress?: boolean;
}> = (props) => {
    const widthRule = `width: ${props.percentage}%;`;
    const showAnimatedSheen =
        props.animateWhileInProgress &&
        props.percentage > 0 &&
        props.percentage < 100;
    return (
        <span
            role="progressbar"
            aria-valuemin={0}
            aria-valuemax={100}
            aria-valuenow={props.percentage}
            css={css`
                position: relative;
                background-color: transparent;
                height: 2px;
                display: block;
            `}
        >
            <span
                css={css`
                    position: absolute;
                    height: 1px;
                    width: 100%;
                    background-color: ${kBloomBlue};
                `}
            />
            <span
                css={css`
                    background-color: ${kBloomBlue};
                    height: 3px;
                    ${widthRule}
                    position: absolute;
                    top: -1px;
                    left: 0;
                    bottom: 0;
                    overflow: hidden;
                `}
            >
                {showAnimatedSheen && (
                    <span
                        css={css`
                            position: absolute;
                            inset: 0;
                            background: linear-gradient(
                                90deg,
                                rgba(255, 255, 255, 0) 0%,
                                rgba(255, 255, 255, 0.18) 35%,
                                rgba(255, 255, 255, 0.7) 50%,
                                rgba(255, 255, 255, 0.18) 65%,
                                rgba(255, 255, 255, 0) 100%
                            );
                            transform: translateX(-100%);
                            animation: progress-bar-sheen 1.4s ease-in-out
                                infinite;

                            @keyframes progress-bar-sheen {
                                from {
                                    transform: translateX(-100%);
                                }

                                to {
                                    transform: translateX(100%);
                                }
                            }
                        `}
                    />
                )}
            </span>
        </span>
    );
};
