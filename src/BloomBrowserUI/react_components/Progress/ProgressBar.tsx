/** @jsx jsx **/
import { jsx, css } from "@emotion/react";

import * as React from "react";
import { kBloomBlue } from "../../bloomMaterialUITheme";

export const ProgressBar: React.FunctionComponent<{
    percentage: number;
}> = props => {
    const widthRule = `width: ${props.percentage}%;`;
    const backgroundImageRule = `background-image: radial-gradient(${kBloomBlue} 16%, transparent 42%);`;
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
                    height: 2px;
                    width: 100%;
                    ${backgroundImageRule}
                    background-size: 8px 10px;
                    background-position: 0 -23px;
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
                `}
            />
        </span>
    );
};
