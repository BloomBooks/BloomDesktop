import { css } from "@emotion/react";
import * as React from "react";
import { useEffect, useState } from "react";
import BloomButton from "../../bloomButton";
import { get, postJson } from "../../../utils/bloomApi";
import WebSocketManager from "../../../utils/WebSocketManager";

interface IZoomInfo {
    zoom: number;
    minZoom: number;
    maxZoom: number;
    zoomEnabled: boolean;
}

export const ZoomControl: React.FunctionComponent = () => {
    const [zoomInfo, setZoomInfo] = useState<IZoomInfo | undefined>(undefined);

    // Fetch zoom info from the backend.
    useEffect(() => {
        if (zoomInfo) {
            return;
        }
        get("workspace/topRight/zoomInfo", (result) => {
            const state = result.data as IZoomInfo;
            setZoomInfo(state);
        });
    }, [zoomInfo]);

    // Listen for backend pushes so the zoom control stays in sync with WinForms state.
    useEffect(() => {
        const listener = (e) => {
            if (e.id === "zoom") {
                const state = e as IZoomInfo;
                setZoomInfo(state);
            }
        };
        WebSocketManager.addListener("workspaceTopRightControls", listener);
        return () =>
            WebSocketManager.removeListener(
                "workspaceTopRightControls",
                listener,
            );
    }, []);

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
                border: hidden;
                background-color: transparent;
                cursor: pointer;
            `}
        >
            {props.label}
        </BloomButton>
    );
};
