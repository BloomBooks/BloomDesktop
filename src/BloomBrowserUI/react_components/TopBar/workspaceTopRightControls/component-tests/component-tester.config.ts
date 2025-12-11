import type { IBloomComponentConfig } from "../../../component-tester/componentTypes";

const config: IBloomComponentConfig<Record<string, never>> = {
    defaultProps: {
        skipApi: true,
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
        initialLanguages: [
            {
                langTag: "en",
                menuText: "English",
                tooltip: "100% translated",
                isCurrent: true,
            },
            {
                langTag: "fr",
                menuText: "Français",
                tooltip: "80% translated",
                isCurrent: false,
            },
        ],
        initialHelpItems: [
            {
                id: "documentation",
                text: "Documentation",
                isSeparator: false,
                enabled: true,
            },
            {
                id: "dividerA",
                text: "",
                isSeparator: true,
                enabled: false,
            },
            {
                id: "aboutBloom",
                text: "About Bloom",
                isSeparator: false,
                enabled: true,
            },
        ],
    },
    modulePath: "../TopBar/workspaceTopRightControls/WorkspaceTopRightControls",
    exportName: "WorkspaceTopRightControls",
};

export default config;
