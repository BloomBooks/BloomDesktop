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
                <div
                    css={css`
                        background-color: #555;
                        border-radius: ${kBorderRadiusForSpecialBlocks};
                        padding: 5px 8px;
                        margin-left: auto !important;
                        width: fit-content;
                    `}
                >
                    <img
                        src={`${getBloomApiPrefix()}copyrightAndLicense/ccImage?token=${token}`}
                        css={css`
                            width: 100px;
                            display: block;
                        `}
                    />
                </div>
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
                    href={getAboutUrl(token, licenseInfo)}
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

    // Use the exact URL stored in metadata when it matches the currently-selected
    // license type (so e.g. an AOR CC-BY-SA 3.0 image links to 3.0, not 4.0).
    // If the user has changed the license type via the controls the token will no
    // longer match the stored URL, so we fall back to the reconstructed 4.0 link.
    function getAboutUrl(token: string, info: typeof licenseInfo) {
        if (info.licenseUrl && token !== "cc0") {
            // Extract the license type segment from the URL path, e.g. "by-sa" from
            // "…/licenses/by-sa/3.0/".  Compare it to the controls-derived token
            // (strip the "cc-" prefix that the token carries but the URL does not).
            const urlTypePart = info.licenseUrl.match(
                /\/licenses\/([^/]+)\//,
            )?.[1];
            const tokenTypePart = token.startsWith("cc-")
                ? token.slice(3)
                : token;
            if (urlTypePart === tokenTypePart) {
                return info.licenseUrl;
            }
        }
        return getCcUrl(
            token,
            info.creativeCommonsInfo.intergovernmentalVersion,
        );
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
