import { css } from "@emotion/react";
import * as React from "react";
import {
    ConfigrBoolean,
    ConfigrGroup,
    ConfigrInput,
    ConfigrPage,
    ConfigrSelect,
} from "@sillsdev/config-r";
import { ListItem } from "@mui/material";
import { useL10n } from "../../react_components/l10nHooks";
import { useApiObject } from "../../utils/bloomApi";
import {
    ICollectionSettingsResponse,
    ICollectionSettingsValues,
} from "../collectionSettingsTypes";
// Books style the badge with this too.
import "../../../content/branding/bloom-badge.less";

/**
 * The "Made with Bloom" badge as books show it, following the values as the user changes them.
 * C# builds it with the code that puts it in books (GET collection/qrBadge).
 */
const QrCodeBadgePreview: React.FunctionComponent<{
    showQrCode: boolean;
    // As it will be saved; books get the default caption when it is empty.
    caption: string;
    // The badge's language: the sign language if the collection has one, else the first language.
    languageTag: string;
    languageName: string;
}> = (props) => {
    const badge = useApiObject<{ html: string }>(
        `collection/qrBadge?showQrCode=${props.showQrCode}` +
            `&tag=${encodeURIComponent(props.languageTag)}` +
            `&languageName=${encodeURIComponent(props.languageName)}` +
            `&caption=${encodeURIComponent(props.caption)}`,
        { html: "" },
    );
    return (
        <div
            data-testid="qr-code-badge-preview"
            css={css`
                color: black;
                /* What a book's branding area (basePage.less) does to the badge's link. */
                a {
                    color: unset;
                    text-decoration: unset;
                }
                /* Books center the badge; here it lines up with the rows above it. */
                .bloom-branding-wrapper {
                    margin-left: 0;
                }
            `}
            dangerouslySetInnerHTML={{ __html: badge.html }}
        />
    );
};

/**
 * The Front & Back Matter page of the Collection Settings dialog: the front/back matter pack, the
 * QR code on the badge, and the place names some front matter shows.
 */
export function useFrontBackMatterPage(props: {
    // Undefined until the dialog's GET collection/settings has answered.
    settings: ICollectionSettingsResponse | undefined;
    // The values as they would be saved now, which the pack's description and the badge preview
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

    // The packs offered follow the saved subscription: a branding can choose the pack, and if a
    // newly typed code's does, C# applies it as it saves.
    const xmatterPacks = props.settings?.xmatterPacks ?? [];
    const frontBackMatter = props.values?.frontBackMatter;
    const selectedPackDescription = xmatterPacks.find(
        (pack) => pack.value === frontBackMatter?.xmatter,
    )?.description;
    const badgeLanguage =
        props.values?.languages.signLanguage ??
        props.values?.languages.language1;

    return (
        <ConfigrPage label={label} pageKey="frontBackMatter" topLevel={true}>
            <ConfigrGroup>
                <ConfigrSelect
                    label={styleLabel}
                    path="frontBackMatter.xmatter"
                    options={xmatterPacks}
                    disabled={props.settings?.xmatterChosenByBranding}
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
                {frontBackMatter && badgeLanguage && (
                    // A list item, like Config-R's rows, so the badge lines up with their labels.
                    <ListItem>
                        <QrCodeBadgePreview
                            showQrCode={frontBackMatter.showQrCode}
                            caption={frontBackMatter.qrcodeCaption}
                            languageTag={badgeLanguage.tag}
                            languageName={badgeLanguage.name}
                        />
                    </ListItem>
                )}
            </ConfigrGroup>
            <ConfigrGroup label={placesLabel} description={placesDescription}>
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
