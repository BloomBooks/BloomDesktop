import { css } from "@emotion/react";
import * as React from "react";
import { CollectionTopBarControls } from "./CollectionTopBarControls/CollectionTopBarControls";
import { WorkspaceTopRightControls } from "./workspaceTopRightControls/WorkspaceTopRightControls";
import { EditTopBarControls } from "../../bookEdit/topbar/editTopBarControls";
import { WorkspaceTabId } from "./TopBar";

export const TopBarControls: React.FunctionComponent<{
    activeTab: WorkspaceTabId;
}> = (props) => {
    return (
        <div
            css={css`
                flex: 1;
                display: flex;
                justify-content: space-between;
                padding-top: 2px;
            `}
        >
            <div
                css={css`
                    display: flex;
                    align-items: flex-start;
                    flex: 1;
                `}
            >
                {props.activeTab === "collection" ? (
                    <CollectionTopBarControls />
                ) : props.activeTab === "edit" ? (
                    <EditTopBarControls />
                ) : null}
            </div>
            <div
                css={css`
                    margin-left: 10px;
                    display: flex;
                    align-items: flex-start;
                `}
            >
                <WorkspaceTopRightControls />
            </div>
        </div>
    );
};
