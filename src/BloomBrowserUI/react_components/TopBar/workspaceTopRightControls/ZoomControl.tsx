import { css } from "@emotion/react";
import * as React from "react";
import { useEffect, useState } from "react";
import BloomButton from "../../bloomButton";
import { get, postJson } from "../../../utils/bloomApi";
import WebSocketManager from "../../../utils/WebSocketManager";

interface ZoomState {
    zoom: number;
    minZoom: number;
    maxZoom: number;
    zoomEnabled: boolean;
}

export const ZoomControl: React.FunctionComponent = () => {
    const [zoomState, setZoomState] = useState<ZoomState | undefined>(
        undefined,
    );

    // Fetch zoom info from the backend.
    useEffect(() => {
        if (zoomState) {
            return;
        }
        get("workspace/topRight/zoomState", (result) => {
            const state = result.data as ZoomState;
            setZoomState(state);
        });
    }, [zoomState]);

    // Listen for backend pushes so the zoom control stays in sync with WinForms state.
    useEffect(() => {
        const listener = (e) => {
            if (e.id === "zoom") {
                const state = e as ZoomState;
                setZoomState(state);
            }
        };
        WebSocketManager.addListener("workspaceTopRightControls", listener);
        return () =>
            WebSocketManager.removeListener(
                "workspaceTopRightControls",
                listener,
            );
    }, []);

    const clampZoom = (value: number, current: ZoomState) => {
        return Math.min(Math.max(value, current.minZoom), current.maxZoom);
    };

    const applyDelta = (delta: number) => {
        if (!zoomState) {
            return;
        }
        const clamped = clampZoom(zoomState.zoom + delta, zoomState);
        setZoomState({ ...zoomState, zoom: clamped });
        postJson("workspace/topRight/zoom", { zoom: clamped });
    };

    if (!zoomState || !zoomState.zoomEnabled) {
        return null;
    }

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
            >{`${zoomState.zoom}%`}</div>

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
