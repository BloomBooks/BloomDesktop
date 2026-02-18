import * as React from "react";
import "App.less";
import { CollectionsTabPane } from "../collectionsTab/CollectionsTabPane";
import { PublishTabPane } from "../publish/PublishTab/PublishTabPane";
import { WireUpForWinforms } from "../utils/WireUpWinform";
import { kPanelBackground } from "../bloomMaterialUITheme";
import { TopBar } from "../react_components/TopBar/TopBar";
import { useWatchApiObject } from "../utils/bloomApi";
import { css } from "@emotion/react";
import { EditTabHost } from "./EditTabHost";

type WorkspaceTabState = "active" | "enabled" | "disabled" | "hidden";

type ITabStates = {
    collection: WorkspaceTabState;
    edit: WorkspaceTabState;
    publish: WorkspaceTabState;
};

export const App: React.FunctionComponent = () => {
    const defaultState: { tabStates: ITabStates } = {
        tabStates: {
            collection: "active",
            edit: "hidden",
            publish: "hidden",
        },
    };

    const state = useWatchApiObject<{ tabStates: ITabStates }>(
        "workspace/tabs",
        defaultState,
        "workspace",
        "tabs",
    );

    const activeTab = React.useMemo(() => {
        if (state.tabStates.publish === "active") {
            return "publish";
        }
        if (state.tabStates.edit === "active") {
            return "edit";
        }
        return "collection";
    }, [state.tabStates]);

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
            {activeTab === "collection" && <CollectionsTabPane />}
            {activeTab === "edit" && <EditTabHost />}
            {activeTab === "publish" && <PublishTabPane />}
        </div>
    );
};

WireUpForWinforms(App);
