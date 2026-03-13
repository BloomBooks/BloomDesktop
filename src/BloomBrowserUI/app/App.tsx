import * as React from "react";
import { css } from "@emotion/react";
import "App.less";
import { CollectionsTabPane } from "../collectionsTab/CollectionsTabPane";
import { WireUpForWinforms } from "../utils/WireUpWinform";
import {
    TopBar,
    TabStates,
    WorkspaceTabId,
} from "../react_components/TopBar/TopBar";
import { PublishTabPane } from "../publish/PublishTab/PublishTabPane";
import { kPanelBackground } from "../bloomMaterialUITheme";
import { useWatchApiObject } from "../utils/bloomApi";

// invoke this with http://localhost:8089". Doesn't do much yet... someday will be the root of our UI.

export const App: React.FunctionComponent = () => {
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
            Object.entries(tabStates).find((entry) => entry[1] === "active")?.[0] ??
            "collection"
        ) as WorkspaceTabId;
    }, [tabStates]);

    const renderActiveTab = () => {
        if (activeTab === "collection") {
            return <CollectionsTabPane />;
        }

        if (activeTab === "publish") {
            return <PublishTabPane />;
        }

        return <>This is just a placeholder for the edit tab for now.</>;
    };

    return (
        <div
            css={css`
                display: flex;
                flex-direction: column;
                height: 100%;
                background: ${kPanelBackground};
            `}
        >
            <TopBar />
            {renderActiveTab()}
        </div>
    );
};

WireUpForWinforms(App);
