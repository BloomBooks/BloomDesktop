import { css, ThemeProvider } from "@emotion/react";
import * as React from "react";
import { useEffect, useMemo, useState } from "react";
import { TopBarButton } from "../../TopBarButton";
import { get, getBloomApiPrefix, post } from "../../../utils/bloomApi";
import { WireUpForWinforms } from "../../../utils/WireUpWinform";
import {
    useSubscribeToWebSocketForObject,
    useSubscribeToWebSocketForStringMessage,
} from "../../../utils/WebSocketManager";
import {
    kBloomBlue,
    kBloomYellow,
    kWarningColor,
    lightTheme,
} from "../../../bloomMaterialUITheme";
import { kBloomGray } from "../../../utils/colorUtils";
const bloomApiPrefix = getBloomApiPrefix(false);

const teamCollectionIcon = `${bloomApiPrefix}images/Team32x32.png`;
const attentionIcon = `${bloomApiPrefix}images/Attention.svg`;
const disconnectedIcon = `${bloomApiPrefix}images/Disconnected.svg`;
const openCreateCollectionIcon = `${bloomApiPrefix}images/OpenCreateCollection24x24.png`;
const settingsIcon = `${bloomApiPrefix}images/settings24x24.png`;

export type TeamCollectionStatus =
    | "Nominal"
    | "NewStuff"
    | "Error"
    | "ClobberPending"
    | "Disconnected"
    | "None";

export interface ITeamCollectionTopBarStatus {
    status: TeamCollectionStatus;
    showReloadButton: boolean;
}

const defaultTeamCollectionStatus: ITeamCollectionTopBarStatus = {
    status: "None",
    showReloadButton: false,
};

const statusBadgeColors: Partial<Record<TeamCollectionStatus, string>> = {
    NewStuff: kBloomBlue,
    Disconnected: kBloomYellow,
    Error: kWarningColor,
    ClobberPending: kWarningColor,
};

const statusLabels: Partial<Record<TeamCollectionStatus, string>> = {
    NewStuff: "Updates Available",
    Disconnected: "Disconnected",
    Error: "Problems Encountered",
    ClobberPending: "Needs Attention",
};

const statusIcons: Record<TeamCollectionStatus, string> = {
    Nominal: teamCollectionIcon,
    NewStuff: attentionIcon,
    Error: attentionIcon,
    ClobberPending: attentionIcon,
    Disconnected: disconnectedIcon,
    None: teamCollectionIcon,
};

const mainButtonBackground = kBloomBlue;
const mainButtonTextColor = "rgb(60, 60, 60)";

export const CollectionTopBarControls: React.FunctionComponent = () => {
    const [teamCollectionStatus, setTeamCollectionStatus] = useState(
        defaultTeamCollectionStatus,
    );
    const [_l10nVersion, setL10nVersion] = useState(0);

    useEffect(() => {
        get("teamCollection/topBarStatus", (result) => {
            setTeamCollectionStatus(result.data as ITeamCollectionTopBarStatus);
        });
    }, []);

    useSubscribeToWebSocketForStringMessage("app", "uiLanguageChanged", () => {
        setL10nVersion((current) => current + 1);
    });

    useSubscribeToWebSocketForObject<ITeamCollectionTopBarStatus>(
        "collectionTopBar",
        "teamCollectionStatus",
        (results) => {
            setTeamCollectionStatus({
                status: results.status,
                showReloadButton: results.showReloadButton,
            });
        },
    );

    const handleTeamCollectionClick = React.useCallback(() => {
        post("teamCollection/showStatusDialog");
    }, []);

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
                    iconPath={settingsIcon}
                    labelL10nKey="CollectionTab.SettingsButton"
                    labelEnglish="Settings"
                    onClick={handleLegacySettingsClick}
                    backgroundColor={mainButtonBackground}
                    textColor={mainButtonTextColor}
                />
                <TopBarButton
                    iconPath={openCreateCollectionIcon}
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
                    /* padding: 6px 10px; */
                    /* height: 66px; */
                    /* box-sizing: border-box; */
                    /* background-color: green; */
                `}
            >
                <TeamCollectionButton
                    status={teamCollectionStatus}
                    onClick={handleTeamCollectionClick}
                />
                {rightSideButtons}
            </div>
        </ThemeProvider>
    );
};

const TeamCollectionButton: React.FunctionComponent<{
    status: ITeamCollectionTopBarStatus;
    onClick: () => void;
}> = (props) => {
    if (props.status.status === "None") {
        return <div />;
    }

    const badgeColor = statusBadgeColors[props.status.status];
    const statusLabel = statusLabels[props.status.status];
    const iconPath = statusIcons[props.status.status];

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
                {badgeColor && (
                    <div
                        css={css`
                            position: absolute;
                            top: 6px;
                            right: 6px;
                            width: 10px;
                            height: 10px;
                            border-radius: 50%;
                            background-color: ${badgeColor};
                            box-shadow: 0 0 0 1px rgba(0, 0, 0, 0.15);
                        `}
                    />
                )}
                <TopBarButton
                    iconPath={iconPath}
                    labelL10nKey="TeamCollection.TeamCollection"
                    labelEnglish="Team Collection"
                    onClick={props.onClick}
                    backgroundColor={kBloomGray}
                    textColor={"white"}
                    cssOverrides={css`
                        border-radius: 8px;
                        grid-template-rows: 34px auto;
                        padding: 10px 8px 6px;
                    `}
                />
            </div>
            {statusLabel && (
                <div
                    css={css`
                        font-size: 11px;
                        line-height: 14px;
                        color: "green";
                        background-color: ${kBloomGray};
                        padding: 2px 8px;
                        border-radius: 12px;
                        box-shadow: 0 1px 2px rgba(0, 0, 0, 0.18);
                    `}
                >
                    {statusLabel}
                </div>
            )}
        </div>
    );
};

WireUpForWinforms(CollectionTopBarControls);
