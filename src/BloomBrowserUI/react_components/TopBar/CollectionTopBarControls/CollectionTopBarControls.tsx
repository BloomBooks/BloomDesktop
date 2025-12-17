import { css, ThemeProvider } from "@emotion/react";
import * as React from "react";
import { useEffect, useState } from "react";
import { TopBarButton } from "../../TopBarButton";
import {
    TeamCollectionButton,
    TeamCollectionStatus,
} from "./TeamCollectionButton";
import { get, getBloomApiPrefix, post } from "../../../utils/bloomApi";
import { WireUpForWinforms } from "../../../utils/WireUpWinform";
import { useSubscribeToWebSocketForStringMessage } from "../../../utils/WebSocketManager";
import { kBloomBlue, lightTheme } from "../../../bloomMaterialUITheme";
const bloomApiPrefix = getBloomApiPrefix(false);

const kOpenCreateCollectionIcon = `${bloomApiPrefix}images/OpenCreateCollection24x24.png`;
const kSettingsIcon = `${bloomApiPrefix}images/settings24x24.png`;

const mainButtonBackground = kBloomBlue;
const mainButtonTextColor = "rgb(60, 60, 60)";

export const CollectionTopBarControls: React.FunctionComponent = () => {
    const [l10nVersion, setL10nVersion] = useState(0);
    useSubscribeToWebSocketForStringMessage("app", "uiLanguageChanged", () => {
        setL10nVersion((current) => current + 1);
    });

    const [teamCollectionStatus, setTeamCollectionStatus] =
        useState<TeamCollectionStatus>("None");
    useEffect(() => {
        get("teamCollection/tcStatus", (result) => {
            setTeamCollectionStatus(result.data as TeamCollectionStatus);
        });
    }, []);
    useSubscribeToWebSocketForStringMessage(
        "collectionTopBar",
        "teamCollectionStatus",
        (status) => {
            setTeamCollectionStatus(status as TeamCollectionStatus);
        },
    );

    // "legacy" means the winforms one.
    // We have a new CollectionSettingsDialog react component which exists but isn't finished.
    const handleLegacySettingsClick = React.useCallback(() => {
        post("workspace/showLegacySettingsDialog");
    }, []);

    const handleOpenOrCreateClick = React.useCallback(() => {
        post("workspace/openOrCreateCollection/");
    }, []);

    return (
        <ThemeProvider theme={lightTheme}>
            <div
                key={`collection-topbar-${l10nVersion}`}
                css={css`
                    display: flex;
                    align-items: flex-start;
                    justify-content: space-between;
                    padding-top: 2px;
                `}
            >
                <TeamCollectionButton status={teamCollectionStatus} />
                <div
                    css={css`
                        display: flex;
                        gap: 10px;
                        align-items: center;
                    `}
                >
                    <TopBarButton
                        iconPath={kSettingsIcon}
                        labelL10nKey="CollectionTab.SettingsButton"
                        labelEnglish="Settings"
                        onClick={handleLegacySettingsClick}
                        backgroundColor={mainButtonBackground}
                        textColor={mainButtonTextColor}
                    />
                    <TopBarButton
                        iconPath={kOpenCreateCollectionIcon}
                        labelL10nKey="CollectionTab.Open/CreateCollectionButton"
                        labelEnglish="Other Collection"
                        onClick={handleOpenOrCreateClick}
                        backgroundColor={mainButtonBackground}
                        textColor={mainButtonTextColor}
                    />
                </div>
            </div>
        </ThemeProvider>
    );
};
WireUpForWinforms(CollectionTopBarControls);
