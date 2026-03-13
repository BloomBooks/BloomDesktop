import * as React from "react";
import { css } from "@emotion/react";
import "App.less";
import { CollectionsTabPane } from "../collectionsTab/CollectionsTabPane";
import { WireUpForWinforms } from "../utils/WireUpWinform";
import {
    defaultWorkspaceTabState,
    getActiveWorkspaceTab,
    TopBar,
    TabStates,
    WorkspaceTabId,
} from "../react_components/TopBar/TopBar";
import { PublishTabPane } from "../publish/PublishTab/PublishTabPane";
import { kPanelBackground } from "../bloomMaterialUITheme";
import { useWatchApiObject } from "../utils/bloomApi";

export const App: React.FunctionComponent = () => {
    // Eventually the source of truth of what tab is active will be on the
    // typescript side. But for now, App.tsx is just a development-only tool
    // which moves us a little closer to a single top-level React component.
    // For now, we just use the same mechanisms TopBar is using to keep tab state in sync.
    const state = useWatchApiObject<{ tabStates: TabStates }>(
        "workspace/tabs",
        defaultWorkspaceTabState,
        "workspace",
        "tabs",
    );

    const tabStates = state.tabStates ?? defaultWorkspaceTabState.tabStates;
    const activeTab = React.useMemo((): WorkspaceTabId => {
        return getActiveWorkspaceTab(tabStates);
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
