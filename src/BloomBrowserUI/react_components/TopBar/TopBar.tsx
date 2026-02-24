import { css } from "@emotion/react";
import * as React from "react";
import { CollectionsTabIcon } from "./CollectionsTabIcon";
import { EditTabIcon } from "./EditTabIcon";
import { PublishTabIcon } from "./PublishTabIcon";
import { WireUpForWinforms } from "../../utils/WireUpWinform";
import { postJson, useWatchApiObject } from "../../utils/bloomApi";
import { TopBarControls } from "./TopBarControls";
import { Span } from "../l10nComponents";
import {
    kBloomBlue,
    kBloomPurple,
    kBloomRed,
    kGreyOnDarkColor,
} from "../../bloomMaterialUITheme";
import { ScopedCssBaseline } from "@mui/material";
import { ToastHost } from "../Toast/ToastHost";

export type WorkspaceTabId = "collection" | "edit" | "publish";

type WorkspaceTabState = "active" | "enabled" | "disabled" | "hidden";

type TabStates = Record<WorkspaceTabId, WorkspaceTabState>;

interface ITabDefinition {
    id: WorkspaceTabId;
    l10nId: string;
    svg: React.ReactNode;
    color: string;
}

const tabDefinitions: Array<ITabDefinition> = [
    {
        id: "collection",
        l10nId: "CollectionTab.Collections",
        svg: <CollectionsTabIcon />,
        color: kBloomBlue,
    },
    {
        id: "edit",
        l10nId: "EditTab.Edit",
        svg: <EditTabIcon />,
        color: kBloomPurple,
    },
    {
        id: "publish",
        l10nId: "PublishTab.Publish",
        svg: <PublishTabIcon />,
        color: kBloomRed,
    },
];

export const TopBar: React.FunctionComponent = () => {
    const defaultState = React.useMemo(
        () => ({
            tabStates: {
                collection: "active",
                edit: "hidden",
                publish: "hidden",
            } as TabStates,
        }),
        [],
    );

    const state = useWatchApiObject<{ tabStates: TabStates }>(
        "workspace/tabs",
        defaultState,
        "workspace",
        "tabs",
    );

    const tabStates = state.tabStates ?? defaultState.tabStates;
    const activeTab = React.useMemo((): WorkspaceTabId => {
        return (
            tabDefinitions.find((t) => tabStates[t.id] === "active")?.id ??
            "collection"
        );
    }, [tabStates]);

    const handleSelectTab = React.useCallback(
        (tab: WorkspaceTabId) => {
            const tabState = tabStates[tab];

            if (
                tabState === "active" ||
                tabState === "hidden" ||
                tabState === "disabled"
            ) {
                return;
            }
            postJson("workspace/selectTab", { tab });
        },
        [tabStates],
    );

    const getColorForTab = React.useCallback(
        (tabId: WorkspaceTabId): string => {
            const tab = tabDefinitions.find((t) => t.id === tabId);
            return tab ? tab.color : kBloomRed;
        },
        [],
    );

    // Notify c# when user clicks in the top bar so WinForms menus opened from top bar controls
    // can close when the click is outside those menus.
    // Temporary: this is only needed while those menus are still WinForms menus.
    // Remove this bridge when top bar and menus are all running in one browser UI.
    React.useEffect(() => {
        const notifyBrowserClicked = () => {
            const webViewWindow = window as unknown as {
                chrome?: {
                    webview?: { postMessage: (message: string) => void };
                };
            };
            webViewWindow.chrome?.webview?.postMessage("browser-clicked");
        };

        window.addEventListener("click", notifyBrowserClicked);
        return () => {
            window.removeEventListener("click", notifyBrowserClicked);
        };
    }, []);

    return (
        /* ScopedCssBaseline injects MUI's base styles (it sets html/body to the theme typography,
           normalizes margins, etc.).
           Without it, I was having trouble with the browser keeping default fonts and spacing
           in WorkspaceTopRightControls, such that our theme's font family/size and resets were not working.
           I'm using ScopedCssBaseline instead of CssBaseline because when we move to single browser,
           CssBaseline would apply everywhere. */
        <ScopedCssBaseline>
            <>
                <div
                    css={css`
                        background-color: ${getColorForTab(activeTab)};
                        padding-top: 2px;
                        display: flex;
                        align-items: flex-start;
                        gap: 100px;
                    `}
                >
                    <BloomTabs
                        tabStates={tabStates}
                        selectTab={handleSelectTab}
                    />
                    <TopBarControls activeTab={activeTab} />
                </div>
                <ToastHost />
            </>
        </ScopedCssBaseline>
    );
};

const kDisabledColor = kGreyOnDarkColor;

const Tab: React.FunctionComponent<{
    tab: ITabDefinition;
    selected: boolean;
    disabled: boolean;
    select: () => void;
}> = (props) => {
    return (
        <li role="presentation">
            <a
                role="tab"
                aria-selected={props.selected ? "true" : "false"}
                aria-disabled={props.disabled ? "true" : "false"}
                css={css`
                    // style as big rectangular tab
                    color: white;
                    text-align: center;
                    padding: 14px 16px;
                    text-decoration: none;
                    //height: 55px;

                    cursor: pointer;

                    background-color: #575757;
                    border: solid thin black;
                    border-top-left-radius: 4px;
                    border-top-right-radius: 4px;
                    font-weight: normal;
                    &[aria-selected="true"] {
                        background-color: #2e2e2e;
                        font-weight: bold;
                    }
                    &:hover {
                        color: black;
                        background-color: white;
                    }
                    display: flex;
                    flex-direction: column;
                    gap: 5px;
                    justify-content: center;
                    align-items: center;
                    font-size: 12px;
                    font-family: "segoe ui";
                    padding: 5px 21px;

                    &[aria-disabled="true"] {
                        pointer-events: none;
                        color: ${kDisabledColor};
                        & svg,
                        & svg * {
                            fill: ${kDisabledColor};
                            stroke: ${kDisabledColor};
                        }
                    }
                `}
                // when clicked, add "selected" class to this tab
                onClick={props.select}
            >
                {props.tab.svg}
                <Span l10nKey={props.tab.l10nId} />
            </a>
        </li>
    );
};
export const BloomTabs: React.FunctionComponent<{
    tabStates: TabStates;
    selectTab: (tab: WorkspaceTabId) => void;
}> = (props) => {
    return (
        <ul
            role="tablist"
            css={
                // style as tabs
                css`
                    display: flex;
                    flex-direction: row;
                    list-style-type: none;
                    margin: 0;
                    padding: 0;
                    gap: 1px;
                    min-width: 300px;
                `
            }
        >
            {tabDefinitions.map((tab) => {
                const tabState = props.tabStates[tab.id];
                if (tabState === "hidden") {
                    return null;
                }

                const disabled = tabState === "disabled";
                const selected = tabState === "active";

                return (
                    <Tab
                        key={tab.id}
                        tab={tab}
                        selected={selected}
                        disabled={disabled}
                        select={() => props.selectTab(tab.id)}
                    />
                );
            })}
        </ul>
    );
};

WireUpForWinforms(TopBar);
