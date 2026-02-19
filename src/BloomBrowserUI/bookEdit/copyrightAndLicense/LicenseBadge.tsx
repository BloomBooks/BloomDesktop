import { css } from "@emotion/react";

import * as React from "react";
import { MenuItem, Select } from "@mui/material";
import WarningIcon from "@mui/icons-material/Warning";

import {
    kBorderRadiusForSpecialBlocks,
    kMutedTextGray,
} from "../../bloomMaterialUITheme";
import { getBloomApiPrefix } from "../../utils/bloomApi";
import { useL10n } from "../../react_components/l10nHooks";
import { Link } from "../../react_components/link";
import { LicenseType, ILicenseInfo } from "./LicensePanel";

// Displays the license as a "badge" which is either the cc image or icon and text.
// Also includes the control for cc version and a link to the cc license if needed.
export const LicenseBadge: React.FunctionComponent<{
    licenseInfo: ILicenseInfo;
    onChange: (ILicenseInfo) => void;
    disabled?: boolean;
}> = (props) => {
    const licenseInfo: ILicenseInfo = JSON.parse(
        JSON.stringify(props.licenseInfo),
    ); //clone

    const licenseShorthand: string = useGetLicenseShorthand(licenseInfo);

    function createNonCcBadge(text: string) {
        const kPadding = "5px";

        return (
            <div
                css={css`
                    display: inline-flex;
                    &,
                    * {
                        color: white !important;
                    }
                    background-color: black;
                    border-radius: ${kBorderRadiusForSpecialBlocks};
                    padding: ${kPadding};
                    align-items: center;
                    font-size: 0.75em;
                    font-weight: normal;
                `}
            >
                <WarningIcon
                    css={css`
                        height: 0.75em !important; // important needed to override default mui behavior
                        margin-right: ${kPadding};
                        color: white;
                    `}
                />
                {text}
            </div>
        );
    }

    function createCcBadge() {
        const token = getCcToken(licenseInfo);
        return (
            <div
                css={css`
                    font-weight: normal;
                    display: flex;
                    flex-direction: column;
                `}
            >
                <img
                    src={`${getBloomApiPrefix()}copyrightAndLicense/ccImage?token=${token}`}
                    css={css`
                        width: 100px;
                        margin-left: auto !important;
                    `}
                />
                {token !== "cc0" && (
                    <div
                        css={css`
                            margin-left: auto !important;
                        `}
                    >
                        <Select
                            variant="standard"
                            css={css`
                                color: ${kMutedTextGray} !important;
                                // I was trying to prevent the gray background when focused, but this isn't working:
                                .MuiSelect-select:focus {
                                    background-color: rgba(
                                        0,
                                        0,
                                        0,
                                        0
                                    ) !important;
                                }
                            `}
                            disableUnderline={true}
                            value={
                                licenseInfo.creativeCommonsInfo
                                    .intergovernmentalVersion
                                    ? "igo3.0"
                                    : "4.0"
                            }
                            onChange={(e) => {
                                licenseInfo.creativeCommonsInfo.intergovernmentalVersion =
                                    e.target.value === "igo3.0";
                                props.onChange(licenseInfo);
                            }}
                            disabled={props.disabled}
                        >
                            <MenuItem value="4.0">4.0</MenuItem>
                            <MenuItem value="igo3.0">
                                {/* I decided not to internationalize for now */}
                                Intergov 3.0
                            </MenuItem>
                        </Select>
                    </div>
                )}
                <Link
                    href={getCcUrl(
                        token,
                        licenseInfo.creativeCommonsInfo
                            .intergovernmentalVersion,
                    )}
                    l10nKey="License.About"
                    l10nComment='%0 is a shorthand version of the Creative Commons license, such as "CC-BY"'
                    l10nParam0={token.toUpperCase()}
                    css={css`
                        color: ${kMutedTextGray} !important;
                        text-decoration: underline !important;
                        margin-left: auto !important;
                    `}
                >
                    About %0
                </Link>
            </div>
        );
    }

    function getCcUrl(token: string, intergovernmentalVersion: boolean) {
        if (token == "cc0") {
            // this one is weird in a couple ways, including that it doesn't have /licenses/ in the path
            return "https://creativecommons.org/publicdomain/zero/1.0/";
        }

        let urlSuffix = token + "/";
        if (token.startsWith("cc-"))
            urlSuffix = urlSuffix.substring("cc-".length); // don't want this as part of URL.

        if (intergovernmentalVersion) {
            urlSuffix += "3.0/igo/";
        } else {
            urlSuffix += "4.0/";
        }

        return "https://creativecommons.org/licenses/" + urlSuffix;
    }

    switch (licenseInfo.licenseType) {
        case LicenseType.CreativeCommons:
        case LicenseType.PublicDomain:
            return createCcBadge();
        case LicenseType.Contact:
        case LicenseType.Custom:
            return createNonCcBadge(licenseShorthand);
        default:
            return <React.Fragment></React.Fragment>;
    }
};

export function useGetLicenseShorthand(licenseInfo?: ILicenseInfo): string {
    // Hooks rules require we get these up front
    const allRightsReserved = useL10n(
        "All Rights Reserved",
        "License.AllRightsReserved",
    );
    const custom = useL10n("Custom", "License.Custom");

    if (!licenseInfo) return "";

    switch (licenseInfo.licenseType) {
        case LicenseType.CreativeCommons:
        case LicenseType.PublicDomain:
            return getCcToken(licenseInfo).toUpperCase();
        case LicenseType.Contact:
            return allRightsReserved;
        case LicenseType.Custom:
            return custom;
        default:
            return "";
    }
}

function getCcToken(licenseInfo: ILicenseInfo) {
    if (licenseInfo.licenseType === LicenseType.PublicDomain) return "cc0";

    let token = "cc-by-";
    if (licenseInfo.creativeCommonsInfo.allowCommercial === "no")
        token += "nc-";
    switch (licenseInfo.creativeCommonsInfo.allowDerivatives) {
        case "no":
            token += "nd";
            break;
        case "sharealike":
            token += "sa";
            break;
        case "yes":
            break;
    }
    // Remove trailing dash
    return token.replace(/-\s*$/, "");
}
