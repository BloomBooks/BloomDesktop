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
import { kBloomRed } from "../../bloomMaterialUITheme";
import {
    ICollectionSettingsResponse,
    ICollectionSettingsValues,
} from "../collectionSettingsTypes";

// A few brandings have badge pictures of their own (dark, or white on red), but most use the
// default branding's, so the preview shows those: it is a picture of the badge, not of this
// branding's version of it.
const kBadgeFolder = "/bloom/branding/Default/";

/**
 * The "Made with Bloom" badge as the books show it, drawn here so that it follows the caption as
 * the user types. The layout copies .bloom-branding-wrapper in branding-base.less, and the caption
 * is built as BookStorage.AppendCaptionWithBoldLanguage builds it. With QR codes turned off, a book
 * shows just the badge picture.
 */
const QrCodeBadgePreview: React.FunctionComponent<{
    showQrCode: boolean;
    // As it will be saved; books get the default caption when it is empty.
    caption: string;
    defaultCaption: string;
    // The badge's language: the sign language if the collection has one, else the first language.
    languageTag: string;
    languageName: string;
}> = (props) => {
    const caption = props.caption === "" ? props.defaultCaption : props.caption;
    const placeholderIndex = caption.indexOf("{0}");
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
                    {/* C# makes the QR code, exactly as it puts it in books. */}
                    <img
                        src={`/bloom/api/collection/qrCode?tag=${encodeURIComponent(props.languageTag)}`}
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
                                caption
                            ) : (
                                <>
                                    {caption.substring(0, placeholderIndex)}
                                    <b>{props.languageName}</b>
                                    {caption.substring(placeholderIndex + 3)}
                                </>
                            )}
                        </div>
                        {/* The address as BookStorage.GetLanguagePageDisplayUrl shows it on books. */}
                        <div
                            css={css`
                                color: ${kBloomRed};
                                font-weight: bold;
                            `}
                        >
                            {`BloomLibrary.org/language:${props.languageTag}`}
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
                    <QrCodeBadgePreview
                        showQrCode={frontBackMatter.showQrCode}
                        caption={frontBackMatter.qrcodeCaption}
                        defaultCaption={props.settings!.defaultQrcodeCaption}
                        languageTag={badgeLanguage.tag}
                        languageName={badgeLanguage.name}
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
