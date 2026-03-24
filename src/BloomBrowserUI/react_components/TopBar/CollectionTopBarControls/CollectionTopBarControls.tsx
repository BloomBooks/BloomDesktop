import { css } from "@emotion/react";
import * as React from "react";
import { useState } from "react";
import { TopBarButton } from "../../TopBarButton";
import { TeamCollectionButton } from "./TeamCollectionButton";
import { TeamCollectionStatus } from "../../../teamCollection/TeamCollectionStatus";
import {
    getBloomApiPrefix,
    post,
    useWatchApiData,
} from "../../../utils/bloomApi";
import { useSubscribeToWebSocketForStringMessage } from "../../../utils/WebSocketManager";
import { kBloomBlue } from "../../../bloomMaterialUITheme";
const bloomApiPrefix = getBloomApiPrefix(false);

const kOpenCreateCollectionIcon = `${bloomApiPrefix}images/OpenCreateCollection24x24.png`;
const kSettingsIcon = `${bloomApiPrefix}images/settings24x24.png`;

const mainButtonBackground = kBloomBlue;
// Review: this doesn't seem to match anything else in our UI.
// But it is what the original WinForms button had, so I've kept it for now.
// Likely we'll need JH to make a full pass through the TopBar controls once
// they are all in React to decide what we want everything to be.
const mainButtonTextColor = "rgb(60, 60, 60)";

export const CollectionTopBarControls: React.FunctionComponent = () => {
    // Forces a refresh. Currently used for localization changes.
    const [generation, setGeneration] = useState(0);
    useSubscribeToWebSocketForStringMessage("app", "uiLanguageChanged", () => {
        setGeneration((current) => current + 1);
    });

    const teamCollectionStatus = useWatchApiData<TeamCollectionStatus>(
        "teamCollection/tcStatus",
        "None",
        "collection",
        "tcStatus",
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
        /* The result of the two sets of flex divs is we get the TC button
           on the left and the other buttons on the right */
        <div
            key={`collection-topbar-${generation}`}
            css={css`
                display: flex;
                align-items: flex-start;
                justify-content: space-between;
                padding-top: 2px;
                width: 100%;
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
    );
};
