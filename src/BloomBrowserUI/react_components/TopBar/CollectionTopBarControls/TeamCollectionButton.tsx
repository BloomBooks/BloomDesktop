import { css } from "@emotion/react";
import * as React from "react";
import { TopBarButton } from "../../TopBarButton";
import { getBloomApiPrefix, post } from "../../../utils/bloomApi";
import { kBloomYellow } from "../../../bloomMaterialUITheme";
import { kBloomGray } from "../../../utils/colorUtils";
import { TeamCollectionStatus } from "../../../teamCollection/TeamCollectionStatus";
import { useTeamCollectionStatusMetadata } from "../../../teamCollection/teamCollectionApi";
import { useL10n2 } from "../../l10nHooks";

const bloomApiPrefix = getBloomApiPrefix(false);

const kTeamCollectionIcon = `${bloomApiPrefix}images/Team32x32.png`;
const kNewStuffIcon = `${bloomApiPrefix}images/TC Button Updates Available.png`;
const kDisconnectedIcon = `${bloomApiPrefix}images/Disconnected.svg`;
const kWarningIcon = `${bloomApiPrefix}images/TC Button Warning.png`;
const kSmallTcIcon = `${bloomApiPrefix}images/TC Button Grey Small Team.png`;

const statusColors: Partial<Record<TeamCollectionStatus, string>> = {
    Nominal: "white",
    NewStuff: "rgb(88, 210, 85)",
    Disconnected: kBloomYellow,
    Error: kBloomYellow,
    ClobberPending: kBloomYellow,
};

// We mostly don't localize Team Collections yet.
// See comment below on nominalLabel.
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
    // The eventual "right" solution here is probably to change
    // the map to use l10nIds rather than the English strings, but only
    // "Team Collection" has been internationalized thus far.
    const nominalLabel = useL10n2({ key: "TeamCollection.TeamCollection" });

    // Cloud Team Collections: when the backend can tell us how many books have a newer version
    // in the repo, show that count instead of the generic "Updates Available" label. This hook
    // only calls its (currently mocked) endpoint when the cloud-team-collections experimental
    // feature is on, so folder Team Collections keep the exact label above unchanged.
    const { updatesAvailableCount } = useTeamCollectionStatusMetadata();
    const updatesAvailableWithCountLabel = useL10n2({
        english: "Updates Available (%0 books)",
        key: "TeamCollection.UpdatesAvailableWithCount",
        params: [String(updatesAvailableCount ?? 0)],
    });

    const handleTeamCollectionClick = React.useCallback(() => {
        post("teamCollection/showStatusDialog");
    }, []);

    if (props.status === "None") {
        return <div />;
    }

    const statusColor = statusColors[props.status] || "white";
    let statusLabel = statusLabels[props.status] || "Team Collection";
    if (statusLabel === statusLabels["Nominal"]) statusLabel = nominalLabel;
    if (
        props.status === "NewStuff" &&
        typeof updatesAvailableCount === "number"
    ) {
        statusLabel = updatesAvailableWithCountLabel;
    }
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
                temporarilyDisableI18nWarning={true}
                onClick={handleTeamCollectionClick}
                backgroundColor={kBloomGray}
                textColor={statusColor}
                cssOverrides={css`
                    border-radius: 8px;
                    grid-template-rows: 40px auto;
                    padding: 4px 16px 6px 16px;
                `}
            />
        </div>
    );
};
