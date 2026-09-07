import * as React from "react";
import { css } from "@emotion/react";
import "App.less";
import "../bookEdit/workspaceRoot";
import { CollectionsTabPane } from "../collectionsTab/CollectionsTabPane";
import { WireUpForWinforms } from "../utils/WireUpWinform";
import {
    defaultWorkspaceTabState,
    getActiveWorkspaceTab,
    TopBar,
    useWorkspaceTabInfo,
    WorkspaceTabId,
} from "../react_components/TopBar/TopBar";
import { PublishTabPane } from "../publish/PublishTab/PublishTabPane";
import { kPanelBackground } from "../bloomMaterialUITheme";
import { EditTabPane } from "./EditTabPane";
import { ToastHost } from "../toast/ToastHost";
import { E2eStepCaption } from "./e2eCaption/E2eStepCaption";

export const App: React.FunctionComponent = () => {
    // Eventually the source of truth of what tab is active will be on the
    // typescript side. But for now, App.tsx is just a development-only tool
    // which moves us a little closer to a single top-level React component.
    // For now, we just use the same mechanisms TopBar is using to keep tab state in sync.
    // AI found a few reasons this switch is still not trivial:
    // C# has non-UI tab switches that happen from backend workflows:
    // Team collection toast click returns to collection tab in WorkspaceView.cs:606.
    // Publish flow can force jump to edit tab in LibraryPublishApi.cs:517.
    // Edit-book command switches to edit tab in WorkspaceView.cs:1164.
    const state = useWorkspaceTabInfo();

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

        return <EditTabPane active={true} />;
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
            <div
                css={css`
                    flex: 1;
                    min-height: 0;
                    position: relative;
                `}
            >
                {renderActiveTab()}
            </div>
            <div id="modal-dialog-container" />
            <ToastHost />
            {/* Says what an end-to-end test is doing. Renders nothing unless Bloom was
                launched with --e2e. */}
            <E2eStepCaption />
        </div>
    );
};

WireUpForWinforms(App);
