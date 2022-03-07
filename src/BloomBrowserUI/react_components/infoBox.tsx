/** @jsx jsx **/
import { jsx, css } from "@emotion/core";
import * as React from "react";
import { kBloomBlue } from "../bloomMaterialUITheme";
import InfoIcon from "@material-ui/icons/Info";

export const InfoBox: React.FunctionComponent<{
    text: string;
    className?: string;
}> = props => {
    return (
        <div
            className={props.className}
            css={css`
                display: flex;
                color: ${kBloomBlue};
                border: solid 1px ${kBloomBlue + "80"};
                background-color: ${kBloomBlue + "10"};
                padding: 5px 10px;
                border-radius: 5px;
            `}
        >
            <InfoIcon
                css={css`
                    margin-right: 10px;
                `}
            ></InfoIcon>
            <div>{props.text}</div>
        </div>
    );
};
