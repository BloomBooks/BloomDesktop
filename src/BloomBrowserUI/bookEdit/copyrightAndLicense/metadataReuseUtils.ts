// Shared helpers for reusing copyright/license/creator metadata: building a stable identity
// for a metadata set and a short license shorthand. Kept separate from the React components so
// the MetadataChooser can use them.
import { ICopyrightAndLicenseData } from "./CopyrightAndLicenseDialog";
import { ILicenseInfo, LicenseType } from "./LicensePanel";

// A stable identity for a copyright/license combination, used to de-duplicate offered
// packages and to tell which one matches the dialog's current values. Two metadata sets with
// the same key are considered the same.
export function computePackageKey(data: ICopyrightAndLicenseData): string {
    const cc = data.licenseInfo.creativeCommonsInfo;
    return JSON.stringify({
        illustrator: (data.copyrightInfo.imageCreator || "").trim(),
        year: (data.copyrightInfo.copyrightYear || "").trim(),
        holder: (data.copyrightInfo.copyrightHolder || "").trim(),
        licenseType: data.licenseInfo.licenseType,
        rightsStatement: (data.licenseInfo.rightsStatement || "").trim(),
        allowCommercial: cc?.allowCommercial,
        allowDerivatives: cc?.allowDerivatives,
        intergovernmentalVersion: cc?.intergovernmentalVersion,
    });
}

// A plain (non-hook) version of useGetLicenseShorthand from LicenseBadge.tsx, so callers can
// compute a shorthand without violating the rules of hooks. The labels for the non-CC cases
// must be passed in (the caller gets them from useL10n).
export function getLicenseShorthand(
    licenseInfo: ILicenseInfo,
    allRightsReservedLabel: string,
    customLabel: string,
): string {
    switch (licenseInfo.licenseType) {
        case LicenseType.CreativeCommons:
        case LicenseType.PublicDomain:
            return getCcToken(licenseInfo).toUpperCase();
        case LicenseType.Contact:
            return allRightsReservedLabel;
        case LicenseType.Custom:
            return customLabel;
        default:
            return "";
    }
}

function getCcToken(licenseInfo: ILicenseInfo): string {
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
    return token.replace(/-\s*$/, "");
}
