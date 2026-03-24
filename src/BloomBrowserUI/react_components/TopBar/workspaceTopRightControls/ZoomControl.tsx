import { css } from "@emotion/react";
import * as React from "react";
import { useEffect, useState } from "react";
import BloomButton from "../../bloomButton";
import { get, postJson } from "../../../utils/bloomApi";
import { useSubscribeToWebSocketForObject } from "../../../utils/WebSocketManager";

interface IZoomInfo {
    zoom: number;
    minZoom: number;
    maxZoom: number;
    zoomEnabled: boolean;
}

export const ZoomControl: React.FunctionComponent = () => {
    const [zoomInfo, setZoomInfo] = useState<IZoomInfo | undefined>(undefined);

    // Fetch zoom info once on initial load.
    useEffect(() => {
        get("workspace/topRight/zoom", (result) => {
            const zoomInfo = result.data as IZoomInfo;
            setZoomInfo(zoomInfo);
        });
    }, []);
    // Get subsequent updates via WebSocket.
    useSubscribeToWebSocketForObject(
        "workspaceTopRightControls",
        "zoom",
        setZoomInfo,
    );

    const clampZoom = (value: number, current: IZoomInfo) => {
        return Math.min(Math.max(value, current.minZoom), current.maxZoom);
    };

    const applyDelta = (delta: number) => {
        if (!zoomInfo) {
            return;
        }
        const clamped = clampZoom(zoomInfo.zoom + delta, zoomInfo);
        setZoomInfo({ ...zoomInfo, zoom: clamped });
        postJson("workspace/topRight/zoom", { zoom: clamped });
    };

    if (!zoomInfo || !zoomInfo.zoomEnabled) {
        return null;
    }

    return (
        <div
            css={css`
                display: flex;
                align-items: center;
                gap: 8px;
            `}
        >
            <PlusMinusButton label="–" onClick={() => applyDelta(-10)} />

            <span
                css={css`
                    cursor: default; // Don't want the user to think he can type
                `}
            >{`${zoomInfo.zoom}%`}</span>

            <PlusMinusButton label="+" onClick={() => applyDelta(10)} />
        </div>
    );
};

interface PlusMinusButtonProps {
    label: string;
    onClick: () => void;
}

const PlusMinusButton: React.FunctionComponent<PlusMinusButtonProps> = (
    props,
) => {
    return (
        <BloomButton
            l10nKey=""
            alreadyLocalized={true}
            enabled={true}
            transparent={true}
            hasText={true}
            onClick={props.onClick}
            css={css`
                font-size: 18px;
                border: none;
                background-color: transparent;
                cursor: pointer;
            `}
        >
            {props.label}
        </BloomButton>
    );
};
