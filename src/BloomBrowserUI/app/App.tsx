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
import {
    EmbeddedSimpleProgressDialog,
    kBloomBridgeProgressContext,
    kBloomBridgeProgressDialogId,
    kUpdateBookProgressDialogId,
} from "../react_components/Progress/SimpleProgressDialog";

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
            {/* Bringing a book up to date ("Update Book", and the automatic pass before the AI
                image editor or after a page-size change) shows its progress here, at the top
                level: it is started from more than one tab, and the Edit tab empties its own
                page while the work runs, so the dialog cannot live inside a tab. Being here
                also means its backdrop covers the whole of Bloom while it is up. */}
            <EmbeddedSimpleProgressDialog id={kUpdateBookProgressDialogId} />
            {/* The same dialog for BloomBridge's process-book runs, on a websocket context of its
                own (see kBloomBridgeProgressContext). */}
            <EmbeddedSimpleProgressDialog
                id={kBloomBridgeProgressDialogId}
                socketContext={kBloomBridgeProgressContext}
            />
            <ToastHost />
        </div>
    );
};

WireUpForWinforms(App);
