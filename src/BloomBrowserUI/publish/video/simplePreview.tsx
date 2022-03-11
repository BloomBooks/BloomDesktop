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
    // then shrink it all. This gives the controls a more reasonable share of the preview.
    const pixelDensityMultiplier = 2;
    const scale = 25;
    const screenWidth = 9 * scale;
    const screenHeight = 16 * scale;

    var width = (props.landscapeWidth * 9) / 16;
    var height = props.landscapeWidth;
    if (props.landscape) {
        const temp = width;
        width = height;
        height = temp;
    }

    var minBorder = 10; // required on sides and bottom, but not top, since nav bar provides visual border there.
    var rootWidth = props.landscapeWidth + 2 * minBorder;
    var rootHeight = props.landscapeWidth + minBorder;
    var sidePadding = (rootWidth - width) / 2;
    var topPadding = (props.landscapeWidth - height) / 2;
    var bottomPadding = topPadding + minBorder;

    return (
        <div>
            <div
                css={css`
                    box-sizing: border-box;
                    height: ${rootHeight}px;
                    width: ${rootWidth}px;
                    padding: ${topPadding}px ${sidePadding}px ${bottomPadding}px
                        ${sidePadding}px;
                    background-color: #2e2e2e;
                `}
            >
                <iframe
                    id="simple-preview"
                    css={css`
                        background-color: #2e2e2e;
                        border: none;
                        flex-shrink: 0; // without this, the height doesn't grow
                        transform-origin: top left;
                        height: ${height * pixelDensityMultiplier}px;
                        width: ${width * pixelDensityMultiplier}px;
                        transform: scale(${1 / pixelDensityMultiplier});
                    `}
                    title="book preview"
                    src={props.url}
                />
            </div>
        </div>
    );
};
