import { css } from "@emotion/react";
import * as React from "react";
import {
    ConfigrBoolean,
    ConfigrGroup,
    ConfigrInput,
    ConfigrPage,
    ConfigrSelect,
    ConfigrStatic,
} from "@sillsdev/config-r";
import { Typography } from "@mui/material";
import { useL10n } from "../../react_components/l10nHooks";
import { get } from "../../utils/bloomApi";
import { kBloomRed } from "../../bloomMaterialUITheme";
import {
    ICollectionSettingsResponse,
    ICollectionSettingsValues,
    IQrCodeBadgeData,
} from "../collectionSettingsTypes";

// One front/back matter pack, as GET settings/xmatter describes it.
interface IXmatterOffering {
    displayName: string;
    internalName: string;
    description: string;
}

// Every branding has the same badge pictures, so the preview uses the default branding's.
const kBadgeFolder = "/bloom/branding/Default/";

/**
 * The "Made with Bloom" badge as the books show it, drawn here so that it follows the caption as
 * the user types. The layout copies .bloom-branding-wrapper in branding-base.less, and the caption
 * is built as BookStorage.AppendCaptionWithBoldLanguage builds it. With QR codes turned off, a book
 * shows just the badge picture.
 */
export const QrCodeBadgePreview: React.FunctionComponent<{
    showQrCode: boolean;
    caption: string;
    badge: IQrCodeBadgeData;
}> = (props) => {
    const placeholderIndex = props.caption.indexOf("{0}");
    return (
        <div
            data-testid="qr-code-badge-preview"
            css={css`
                border: 5px solid ${kBloomRed};
                background-color: white;
                border-radius: 8px;
                width: fit-content;
                max-width: 100%;
                margin: 8px 0;
                color: black;
                img {
                    padding: 10px;
                    box-sizing: border-box;
                    vertical-align: top;
                }
            `}
        >
            <img
                src={
                    kBadgeFolder +
                    (props.showQrCode
                        ? "made-with-bloom-badge-text.svg"
                        : "made-with-bloom-badge.svg")
                }
                css={css`
                    width: 4.7cm;
                `}
            />
            {props.showQrCode && (
                <>
                    <img
                        src={props.badge.qrCode}
                        css={css`
                            width: 2.8cm;
                        `}
                    />
                    <div
                        css={css`
                            font-size: 9pt;
                            line-height: 1.2;
                            box-sizing: border-box;
                            margin: 0 10px 10px 12px;
                            max-width: 7cm;
                        `}
                    >
                        <div data-testid="qr-code-badge-caption">
                            {placeholderIndex < 0 ? (
                                props.caption
                            ) : (
                                <>
                                    {props.caption.substring(
                                        0,
                                        placeholderIndex,
                                    )}
                                    <b>{props.badge.languageName}</b>
                                    {props.caption.substring(
                                        placeholderIndex + 3,
                                    )}
                                </>
                            )}
                        </div>
                        <div
                            css={css`
                                color: ${kBloomRed};
                                font-weight: bold;
                            `}
                        >
                            {props.badge.displayUrl}
                        </div>
                    </div>
                </>
            )}
        </div>
    );
};

/**
 * The Front & Back Matter page of the Collection Settings dialog: the front/back matter pack, the
 * QR code on the badge, and the place names some front matter shows.
 */
export function useFrontBackMatterPage(props: {
    dialogOpen: boolean;
    // Undefined until the dialog's GET collection/settings has answered.
    settings: ICollectionSettingsResponse | undefined;
    // The values as the user has them now, which the pack's description and the badge preview
    // follow.
    values: ICollectionSettingsValues | undefined;
}): React.ReactElement {
    const label = useL10n(
        "Front & Back Matter",
        "CollectionSettingsDialog.FrontBackMatterPage",
    );
    const styleLabel = useL10n(
        "Style",
        "CollectionSettingsDialog.FrontBackMatterPage.Style",
    );
    const qrCodesLabel = useL10n(
        "QR Codes",
        "CollectionSettingsDialog.AdvancedTab.QrCodes",
    );
    const showQrCodesLabel = useL10n(
        "Show QR Codes",
        "CollectionSettingsDialog.AdvancedTab.ShowQrCodes",
    );
    const showQrCodesDescription = useL10n(
        "Scanning the code or clicking the badge will take the user to the collection of books in this collection's primary language. Note, if you want to use this but your branding does not show it, contact us.",
        "CollectionSettingsDialog.AdvancedTab.ShowQrCodes.Description",
    );
    const captionLabel = useL10n(
        "Caption",
        "CollectionSettingsDialog.AdvancedTab.Caption",
    );
    const captionDescription = useL10n(
        'Bloom shows the BloomLibrary.org address on its own line beneath this caption, so you don\'t need to include it here. If your caption contains "{0}", Bloom will fill this in with the name of the language.',
        "CollectionSettingsDialog.AdvancedTab.Caption.Description",
    );
    const placesLabel = useL10n(
        "Places",
        "CollectionSettingsDialog.FrontBackMatterPage.Places",
    );
    const placesDescription = useL10n(
        "Some front matters will show the following place names. If there are any that you don't want to show, leave them empty.",
        "CollectionSettingsDialog.FrontBackMatterPage.Places.Description",
    );
    const countryLabel = useL10n(
        "Country",
        "CollectionSettingsDialog.ProjectInformationTab.Country",
    );
    const provinceLabel = useL10n(
        "Province",
        "CollectionSettingsDialog.ProjectInformationTab.Province",
    );
    const districtLabel = useL10n(
        "District",
        "CollectionSettingsDialog.ProjectInformationTab.District",
    );

    // Both are asked for when the dialog opens, not when Config-R mounts this page (it mounts only
    // the page showing), so they are usually in before anyone gets here. The packs offered follow
    // the saved subscription: a branding can choose the pack, and if a newly typed code's does,
    // C# applies it as it saves.
    const [xmatterOfferings, setXmatterOfferings] = React.useState<
        IXmatterOffering[]
    >([]);
    const [badge, setBadge] = React.useState<IQrCodeBadgeData>();
    React.useEffect(() => {
        if (!props.dialogOpen) return;
        get("settings/xmatter", (result) => {
            setXmatterOfferings(
                result.data.xmatterOfferings as IXmatterOffering[],
            );
        });
        get("collection/qrCodeBadge", (result) => {
            setBadge(result.data as IQrCodeBadgeData);
        });
    }, [props.dialogOpen]);

    const options = xmatterOfferings.map((pack) => ({
        value: pack.internalName,
        label: pack.displayName,
    }));
    // Keep the saved pack choosable even when it is no longer offered (it was uninstalled), so
    // that seeing it does not change it; C# replaces it with one that is offered as it saves.
    // (values is null for a user who may not edit the settings.)
    const savedXmatter = props.settings?.values?.frontBackMatter.xmatter;
    if (savedXmatter && !options.some((o) => o.value === savedXmatter))
        options.push({ value: savedXmatter, label: savedXmatter });

    const frontBackMatter = props.values?.frontBackMatter;
    const selectedPackDescription = xmatterOfferings.find(
        (pack) => pack.internalName === frontBackMatter?.xmatter,
    )?.description;

    return (
        <ConfigrPage label={label} pageKey="frontBackMatter" topLevel={true}>
            <ConfigrGroup>
                <ConfigrSelect
                    label={styleLabel}
                    path="frontBackMatter.xmatter"
                    options={options}
                    // A branding that chooses the pack leaves only that one.
                    disabled={options.length < 2}
                    description={selectedPackDescription}
                />
            </ConfigrGroup>
            <ConfigrGroup label={qrCodesLabel}>
                <ConfigrBoolean
                    label={showQrCodesLabel}
                    path="frontBackMatter.showQrCode"
                    description={showQrCodesDescription}
                />
                <ConfigrInput
                    label={captionLabel}
                    path="frontBackMatter.qrcodeCaption"
                    description={captionDescription}
                    charactersWide={45}
                    allowNewLines={true}
                    maxLinesToShowBeforeScrolling={4}
                    disabled={!frontBackMatter?.showQrCode}
                />
                {badge && frontBackMatter && (
                    <QrCodeBadgePreview
                        showQrCode={frontBackMatter.showQrCode}
                        caption={frontBackMatter.qrcodeCaption}
                        badge={badge}
                    />
                )}
            </ConfigrGroup>
            {/* Config-R does not show a group's description, so this heading and its explanation
                stand in for the group's own label, in the Typography Config-R uses for it. */}
            <ConfigrStatic>
                <Typography variant="h3">{placesLabel}</Typography>
                <Typography
                    color="text.secondary"
                    css={css`
                        margin-top: 8px;
                    `}
                >
                    {placesDescription}
                </Typography>
            </ConfigrStatic>
            <ConfigrGroup>
                <ConfigrInput
                    label={countryLabel}
                    path="frontBackMatter.country"
                />
                <ConfigrInput
                    label={provinceLabel}
                    path="frontBackMatter.province"
                />
                <ConfigrInput
                    label={districtLabel}
                    path="frontBackMatter.district"
                />
            </ConfigrGroup>
        </ConfigrPage>
    );
}
