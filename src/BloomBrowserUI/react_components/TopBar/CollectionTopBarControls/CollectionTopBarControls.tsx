import { css, ThemeProvider } from "@emotion/react";
import * as React from "react";
import { useEffect, useMemo, useState } from "react";
import { TopBarButton } from "../../TopBarButton";
import { get, getBloomApiPrefix, post } from "../../../utils/bloomApi";
import { WireUpForWinforms } from "../../../utils/WireUpWinform";
import { useSubscribeToWebSocketForStringMessage } from "../../../utils/WebSocketManager";
import {
    kBloomBlue,
    kBloomYellow,
    lightTheme,
} from "../../../bloomMaterialUITheme";
import { kBloomGray } from "../../../utils/colorUtils";
const bloomApiPrefix = getBloomApiPrefix(false);

const kOpenCreateCollectionIcon = `${bloomApiPrefix}images/OpenCreateCollection24x24.png`;
const kSettingsIcon = `${bloomApiPrefix}images/settings24x24.png`;

type TeamCollectionStatus =
    | "None"
    | "Nominal"
    | "NewStuff"
    | "Error"
    | "ClobberPending"
    | "Disconnected";

const mainButtonBackground = kBloomBlue;
const mainButtonTextColor = "rgb(60, 60, 60)";

export const CollectionTopBarControls: React.FunctionComponent = () => {
    const [teamCollectionStatus, setTeamCollectionStatus] =
        useState<TeamCollectionStatus>("None");
    const [_l10nVersion, setL10nVersion] = useState(0);

    useEffect(() => {
        get("teamCollection/tcStatus", (result) => {
            setTeamCollectionStatus(result.data as TeamCollectionStatus);
        });
    }, []);

    useSubscribeToWebSocketForStringMessage("app", "uiLanguageChanged", () => {
        setL10nVersion((current) => current + 1);
    });

    useSubscribeToWebSocketForStringMessage(
        "collectionTopBar",
        "teamCollectionStatus",
        (status) => {
            setTeamCollectionStatus(status as TeamCollectionStatus);
        },
    );

    const handleLegacySettingsClick = React.useCallback(() => {
        post("workspace/legacySettings");
    }, []);

    const handleOpenOrCreateClick = React.useCallback(() => {
        post("workspace/openOrCreateCollection/");
    }, []);

    const rightSideButtons = useMemo(
        () => (
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
        ),
        [handleLegacySettingsClick, handleOpenOrCreateClick],
    );

    return (
        <ThemeProvider theme={lightTheme}>
            <div
                css={css`
                    display: flex;
                    align-items: center;
                    justify-content: space-between;
                `}
            >
                <TeamCollectionButton status={teamCollectionStatus} />
                {rightSideButtons}
            </div>
        </ThemeProvider>
    );
};

const TeamCollectionButton: React.FunctionComponent<{
    status: TeamCollectionStatus;
}> = (props) => {
    const handleTeamCollectionClick = React.useCallback(() => {
        post("teamCollection/showStatusDialog");
    }, []);

    if (props.status === "None") {
        return <div />;
    }

    const kTeamCollectionIcon = `${bloomApiPrefix}images/Team32x32.png`;
    const kNewStuffIcon = `${bloomApiPrefix}images/TC Button Updates Available.png`;
    const kDisconnectedIcon = `${bloomApiPrefix}images/Disconnected.svg`;
    const kWarningIcon = `${bloomApiPrefix}images/TC Button Warning.png`;
    const kSmallTcIcon = `${bloomApiPrefix}images/TC Button Grey Small Team.png`;

    const statusBadgeColors: Partial<Record<TeamCollectionStatus, string>> = {
        Nominal: "white",
        NewStuff: "rgb(88, 210, 85)",
        Disconnected: kBloomYellow,
        Error: kBloomYellow,
        ClobberPending: kBloomYellow,
    };

    const statusLabels: Partial<Record<TeamCollectionStatus, string>> = {
        Nominal: "Team Collection",
        NewStuff: "Updates Available",
        Disconnected: "Disconnected",
        Error: "Problems Encountered",
        ClobberPending: "Problems Encountered",
    };

    const statusIcons: Partial<Record<TeamCollectionStatus, string>> = {
        Nominal: kTeamCollectionIcon,
        NewStuff: kNewStuffIcon,
        Disconnected: kDisconnectedIcon,
        Error: kWarningIcon,
        ClobberPending: kWarningIcon,
    };

    const badgeColor = statusBadgeColors[props.status] || "white";
    const statusLabel = statusLabels[props.status] || "Team Collection";
    const iconPath = statusIcons[props.status] || kTeamCollectionIcon;
    return (
        <div
            css={css`
                display: flex;
                flex-direction: column;
                align-items: flex-start;
                gap: 4px;
            `}
        >
            <div
                css={css`
                    position: relative;
                    display: inline-block;
                `}
            >
                {props.status !== "Nominal" && (
                    <img
                        src={kSmallTcIcon}
                        css={css`
                            position: absolute;
                            top: 8px;
                            left: 8px;
                            width: 16px;
                        `}
                    />
                )}
                <TopBarButton
                    iconPath={iconPath}
                    labelL10nKey=""
                    labelEnglish={statusLabel}
                    onClick={handleTeamCollectionClick}
                    backgroundColor={kBloomGray}
                    textColor={badgeColor}
                    cssOverrides={css`
                        border-radius: 8px;
                        grid-template-rows: 34px auto;
                        padding-left: 12px;
                        padding-right: 12px;
                    `}
                />
            </div>
        </div>
    );
};

WireUpForWinforms(CollectionTopBarControls);
