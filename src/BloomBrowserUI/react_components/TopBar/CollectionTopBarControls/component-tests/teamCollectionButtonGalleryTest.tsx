import { css, ThemeProvider } from "@emotion/react";
import * as React from "react";
import { TeamCollectionButton } from "../TeamCollectionButton";
import { TeamCollectionStatus } from "../../../../teamCollection/TeamCollectionStatus";
import { kBloomBlue, lightTheme } from "../../../../bloomMaterialUITheme";

export const TeamCollectionButtonGalleryTest: React.FunctionComponent = () => {
    const statuses: TeamCollectionStatus[] = [
        "Nominal",
        "NewStuff",
        "Error",
        "ClobberPending",
        "Disconnected",
    ];

    return (
        <ThemeProvider theme={lightTheme}>
            <div
                css={css`
                    display: flex;
                    gap: 12px;
                    align-items: flex-start;
                    padding: 4px;
                    background-color: ${kBloomBlue};
                `}
            >
                {statuses.map((status) => (
                    <TeamCollectionButton key={status} status={status} />
                ))}
            </div>
        </ThemeProvider>
    );
};
