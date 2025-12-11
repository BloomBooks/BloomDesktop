import { css } from "@emotion/react";
import * as React from "react";
import BloomButton from "../../bloomButton";

interface ZoomControlProps {
    zoom: number;
    minZoom: number;
    maxZoom: number;
    onZoomChange: (newZoom: number) => void;
}

export const ZoomControl: React.FunctionComponent<ZoomControlProps> = (
    props,
) => {
    const clampZoom = (value: number) => {
        return Math.min(Math.max(value, props.minZoom), props.maxZoom);
    };

    const applyDelta = (delta: number) => {
        const clamped = clampZoom(props.zoom + delta);
        props.onZoomChange(clamped);
    };

    return (
        <div
            css={css`
                display: flex;
                align-items: center;
                gap: 4px;
                background-color: transparent;
                padding: 4px 6px;
                border-radius: 4px;
            `}
        >
            <BloomButton
                l10nKey=""
                alreadyLocalized={true}
                enabled={true}
                transparent={true}
                hasText={true}
                onClick={() => applyDelta(-10)}
                css={css`
                    min-width: 28px;
                    padding: 4px;
                    border: hidden;
                    background-color: transparent;
                `}
            >
                -
            </BloomButton>

            <div
                css={css`
                    width: 56px;
                    text-align: center;
                `}
            >{`${props.zoom}%`}</div>

            <BloomButton
                l10nKey=""
                alreadyLocalized={true}
                enabled={true}
                transparent={true}
                hasText={true}
                onClick={() => applyDelta(10)}
                css={css`
                    min-width: 28px;
                    padding: 4px;
                    border: hidden;
                    background-color: transparent;
                `}
            >
                +
            </BloomButton>
        </div>
    );
};
