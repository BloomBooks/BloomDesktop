import { css } from "@emotion/react";
import * as React from "react";
import { useEffect, useState } from "react";
import BloomButton from "../../bloomButton";
import { ArrowDropDown } from "@mui/icons-material";
import { get, postJson } from "../../../utils/bloomApi";
import WebSocketManager from "../../../utils/WebSocketManager";

export const UiLanguageMenu: React.FunctionComponent = () => {
    const [label, setLabel] = useState<string | undefined>(undefined);

    // Fetch the current label from the backend.
    useEffect(() => {
        get("workspace/topRight/uiLanguageState", (result) => {
            const state = result.data as string;
            setLabel(state);
        });
    }, []);

    // Listen for backend pushes so the label stays in sync with WinForms state.
    useEffect(() => {
        const listener = (e) => {
            if (e.id === "uiLanguage") {
                const state = e as string;
                setLabel(state);
            } else if (typeof e === "string") {
                setLabel(e);
            }
        };
        WebSocketManager.addListener("workspaceTopRightControls", listener);
        return () =>
            WebSocketManager.removeListener(
                "workspaceTopRightControls",
                listener,
            );
    }, []);

    const onOpen = () => {
        postJson("workspace/topRight/openLanguageMenu", {});
    };

    return (
        <BloomButton
            l10nKey=""
            alreadyLocalized={true}
            enabled={true}
            hasText={true}
            variant="text"
            onClick={onOpen}
            endIcon={<ArrowDropDown />}
            css={css`
                border: hidden;
                font-size: 11px;
                padding-inline: 5px;
                padding-top: 1px;
                padding-bottom: 2px;
                text-transform: none;
                width: fit-content;
            `}
        >
            {label ?? ""}
        </BloomButton>
    );
};
