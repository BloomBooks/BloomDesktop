/** @jsxImportSource @emotion/react */
import * as React from "react";
import { css } from "@emotion/react";
import { kBloomBlue } from "../../../bloomMaterialUITheme";
import { WorkspaceTopRightControls } from "./WorkspaceTopRightControls";

export const WorkspaceTopRightControlsHarness: React.FunctionComponent = () => {
    return (
        <div
            css={css`
                min-height: 100vh;
                display: flex;
                align-items: center;
                justify-content: center;
                background: ${kBloomBlue};
            `}
        >
            <WorkspaceTopRightControls
                skipApi={true}
                initialState={{
                    uiLanguageLabel: "English",
                    showUnapprovedText:
                        "Show translations which have not been approved yet",
                    showUnapprovedChecked: false,
                    zoom: 100,
                    zoomEnabled: true,
                    minZoom: 50,
                    maxZoom: 300,
                }}
                initialLanguages={[
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
                ]}
                initialHelpItems={[
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
                ]}
            />
        </div>
    );
};
