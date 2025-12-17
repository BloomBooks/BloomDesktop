import { css } from "@emotion/react";
import * as React from "react";
import { TopBarButton } from "../../TopBarButton";
import { getBloomApiPrefix, post } from "../../../utils/bloomApi";
import { kBloomYellow } from "../../../bloomMaterialUITheme";
import { kBloomGray } from "../../../utils/colorUtils";

const bloomApiPrefix = getBloomApiPrefix(false);

export type TeamCollectionStatus =
    | "None"
    | "Nominal"
    | "NewStuff"
    | "Error"
    | "ClobberPending"
    | "Disconnected";

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

export const TeamCollectionButton: React.FunctionComponent<{
    status: TeamCollectionStatus;
}> = (props) => {
    const handleTeamCollectionClick = React.useCallback(() => {
        post("teamCollection/showStatusDialog");
    }, []);

    if (props.status === "None") {
        return <div />;
    }

    const badgeColor = statusBadgeColors[props.status] || "white";
    const statusLabel = statusLabels[props.status] || "Team Collection";
    const iconPath = statusIcons[props.status] || kTeamCollectionIcon;
    return (
        <div
            css={css`
                position: relative;
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
                    grid-template-rows: 40px auto;
                    padding: 4px 16px 6px 16px;
                `}
            />
        </div>
    );
};
