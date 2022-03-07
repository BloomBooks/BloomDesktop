/** @jsx jsx **/
import { jsx, css } from "@emotion/core";
import * as React from "react";
import { useState, useEffect } from "react";
import { useL10n } from "../../react_components/l10nHooks";
import { useDrawAttention } from "../../react_components/UseDrawAttention";
import { useTheme, Theme, Typography } from "@material-ui/core";
import IconButton from "@material-ui/core/IconButton";
import RefreshIcon from "@material-ui/icons/Refresh";

export const SimplePreview: React.FunctionComponent<{
    landscape: boolean;
    url: string;
    landscapeWidth: number;
}> = props => {
    const theme: Theme = useTheme();

    // Desktop pixels are much larger, so things come out bloated.
    // For now what we do is make the player & readium think we have twice the pixels,
    // then shrink it all.
    const pixelDensityMultiplier = 2;
    const scale = 25;
    const screenWidth = 9 * scale;
    const screenHeight = 16 * scale;

    var iframeClasses = "";
    if (props.landscape) {
        iframeClasses = `
            // height: ${(pixelDensityMultiplier * 900) / 16}%;
            // width: ${pixelDensityMultiplier * 100}%;
            transform: rotate(90deg)  translate(0, -${screenWidth}px)
                scale( ${1 / pixelDensityMultiplier});
                `;
    } else {
        iframeClasses = `
            // width: ${(pixelDensityMultiplier * 900) / 16}% !important;
            // height: ${pixelDensityMultiplier * 100}% !important;
            transform: scale(${1 / pixelDensityMultiplier});
            `;
    }

    var rootClasses = "";
    if (props.landscape) {
        rootClasses = `transform-origin: top left;
    transform: translate(0, ${screenWidth}px )
        rotate(-90deg);`;
    }

    var width = (props.landscapeWidth * 9) / 16;
    var height = props.landscapeWidth;
    if (props.landscape) {
        const temp = width;
        width = height;
        height = temp;
    }

    return (
        <div>
            <div
                css={css`
                    height: ${height}px; // Enhance: could be conditional on landscape
                    width: ${width}px;
                    ${rootClasses} //background-color:
                `}
            >
                <iframe
                    id="simple-preview"
                    css={css`
                        background-color: black;
                        border: none;
                        flex-shrink: 0; // without this, the height doesn't grow
                        transform-origin: top left;
                        height: ${pixelDensityMultiplier * 100 + "%"};
                        width: ${pixelDensityMultiplier * 100 + "%"};
                        ${iframeClasses}
                    `}
                    title="book preview"
                    src={props.url}
                />
            </div>
        </div>
    );
};
