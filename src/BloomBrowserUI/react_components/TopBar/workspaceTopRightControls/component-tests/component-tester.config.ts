import type { IBloomComponentConfig } from "../../../component-tester/componentTypes";

const config: IBloomComponentConfig<Record<string, never>> = {
    defaultProps: {
        initialState: {
            uiLanguageLabel: "English",
            showUnapprovedText:
                "Show translations which have not been approved yet",
            showUnapprovedChecked: false,
            zoom: 100,
            zoomEnabled: true,
            minZoom: 50,
            maxZoom: 300,
        },
    },
    modulePath: "../TopBar/workspaceTopRightControls/WorkspaceTopRightControls",
    exportName: "WorkspaceTopRightControls",
};

export default config;
