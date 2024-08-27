/** @jsx jsx **/
import { jsx, css } from "@emotion/react";
import * as React from "react";
import { Button, Link, Typography } from "@mui/material";

import { IFontMetaData } from "../bookEdit/StyleEditor/fontSelectComponent";
import InfoOutlinedIcon from "@mui/icons-material/InfoOutlined";
import { useL10n } from "./l10nHooks";
import { kBloomBlue, kDisabledControlGray } from "../bloomMaterialUITheme";
import CloseIcon from "@mui/icons-material/Close";

const FontInformationPane: React.FunctionComponent<{
    metadata: IFontMetaData | undefined;
}> = props => {
    const variantString =
        props.metadata && props.metadata.variants
            ? props.metadata.variants.join(", ")
            : undefined;

    const suitability = props.metadata?.determinedSuitability;

    const textColor =
        suitability === "ok"
            ? "primary" // BloomBlue
            : suitability === "unknown"
            ? "textPrimary" // black
            : "error"; // red

    const OkayFontMessage = useL10n(
        "The metadata inside this font indicates that it is legal to use for all Bloom purposes.",
        "FontInformationPane.FontOkay",
        "This shows in the popup when hovering over a useable font."
    );

    const UnknownFontMessage = useL10n(
        "Bloom cannot determine what rules govern the use of this font. Please read the license and make sure it allows embedding in ebooks and the web. Before publishing to bloomlibrary.org, you will probably have to make a special request to the Bloom team to investigate this font so that we can make sure we won't get in trouble for hosting it.",
        "FontInformationPane.FontUnknown",
        "This shows in the popup when hovering over a font when Bloom can't determine if it is useable legally."
    );

    const UnsuitableFontFormatMessage = useL10n(
        "This font is in a file format that Bloom cannot use for ebooks. Please use a different font.",
        "FontInformationPane.FontFormatUnsuitable",
        "This shows in the popup when hovering over a font that Bloom can't use in ebooks due to its file format."
    );

    const GeneralUnsuitableFontLicenseMessage = useL10n(
        "The metadata inside this font tells us that it may not be embedded for free in ebooks and the web. Please use a different font.",
        "FontInformationPane.FontUnsuitable",
        "This shows in the popup when hovering over a font that Bloom can't legally host on its website."
    );
    const MicrosoftFontsArentFree = useL10n(
        "This is a font supplied by Microsoft for use on your computer alone. Microsoft does not allow its fonts to be used freely on the web or distributed in eBooks. Please use a different font.",
        "PublishTab.FontProblem.Microsoft",
        "This shows in the popup when hovering over a font that Microsoft owns (or distributes)."
    );
    const UnsuitableFontLicenseMessage =
        props.metadata?.licenseURL ===
        "https://learn.microsoft.com/en-us/typography/fonts/font-faq"
            ? MicrosoftFontsArentFree
            : GeneralUnsuitableFontLicenseMessage;

    const mainMessage =
        suitability === "ok"
            ? OkayFontMessage
            : suitability === "unknown"
            ? UnknownFontMessage
            : suitability === "invalid"
            ? UnsuitableFontFormatMessage
            : UnsuitableFontLicenseMessage;

    const styleWording = useL10n(
        "Styles",
        "FontInformationPane.Styles",
        "This shows in the popup before the types of variants in the font (e.g. bold, italic)."
    );

    const versionWording = useL10n(
        "Version",
        "FontInformationPane.Version",
        "This shows in the popup before the font's version number."
    );

    const licenseWording = useL10n("License", "Common.License");

    const showFontDeveloperData = (fontData: IFontMetaData | undefined) => {
        if (!fontData) return;
        let message = `name: ${fontData.name}\n`;
        message += `version: ${fontData.version}\n`;
        message += `license: ${fontData.license}\n`;
        message += `licenseURL: ${fontData.licenseURL}\n`;
        message += `copyright: ${fontData.copyright}\n`;
        message += `designer: ${fontData.designer}\n`;
        message += `designerURL: ${fontData.designerURL}\n`;
        message += `manufacturer: ${fontData.manufacturer}\n`;
        message += `manufacturerURL: ${fontData.manufacturerURL}\n`;
        message += `fsType: ${fontData.fsType}\n`;
        message += `styles: ${fontData.variants?.toString()}\n`;
        message += `determinedSuitability: ${fontData.determinedSuitability}\n`;
        message += `determinedSuitabilityNotes: ${fontData.determinedSuitabilityNotes}\n`;
        alert(message);
    };

    const SmallLink: React.FunctionComponent<{
        href: string;
        linkText: string;
    }> = props => (
        <Link variant="body2" underline="always" href={props.href}>
            {props.linkText}
        </Link>
    );

    return (
        <div
            css={css`
                display: flex;
                flex-direction: row;
                align-items: flex-start;
            `}
        >
            <Button
                startIcon={<CloseIcon htmlColor="white" />}
                size="small"
                // Clicking anywhere on the pane works, but w/o the button the user might not know this.
                onClick={() => {
                    // Do nothing
                }}
                css={css`
                    order: 1; // put icon in upper right corner
                    padding: 2px !important;
                    min-width: unset !important;
                    background-color: ${kBloomBlue} !important;
                    margin-top: 2px !important;
                    margin-right: 2px !important;
                    span span {
                        margin: 0 !important;
                    }
                `}
            />
            {!props.metadata || (
                <div
                    css={css`
                        display: flex;
                        flex: 1;
                        flex-direction: column;
                        padding: 10px;
                        padding-right: 2px; // because close icon takes up space
                        width: 200px;
                    `}
                >
                    {/* Primary text message */}
                    <Typography
                        color={textColor}
                        variant="body2"
                        css={css`
                            margin-bottom: 10px !important;
                        `}
                    >
                        {mainMessage}
                    </Typography>
                    {/* Font name */}
                    <Typography
                        variant="subtitle2"
                        css={css`
                            // 'subtitle2' variant ought to be enough, but wasn't; encourage actual bolding
                            font-weight: 800;
                        `}
                    >
                        {props.metadata.name}
                    </Typography>
                    {/* Variant style (bold, italic, etc.) */}
                    {variantString && (
                        <Typography variant="body2">
                            {styleWording}: {variantString}
                        </Typography>
                    )}
                    {/* Designer and DesignerURL */}
                    {props.metadata.designerURL && props.metadata.designer ? (
                        <SmallLink
                            href={props.metadata.designerURL}
                            linkText={props.metadata.designer}
                        />
                    ) : props.metadata.designer ? (
                        <Typography variant="body2">
                            {props.metadata.designer}
                        </Typography>
                    ) : (
                        <SmallLink
                            href={props.metadata.designerURL!}
                            linkText={props.metadata.designerURL!}
                        />
                    )}
                    {/* Manufacturer and ManufacturerURL */}
                    {props.metadata.manufacturerURL &&
                    props.metadata.manufacturer ? (
                        <SmallLink
                            href={props.metadata.manufacturerURL}
                            linkText={props.metadata.manufacturer}
                        />
                    ) : props.metadata.manufacturer ? (
                        <Typography variant="body2">
                            {props.metadata.manufacturer}
                        </Typography>
                    ) : (
                        <SmallLink
                            href={props.metadata.manufacturerURL!}
                            linkText={props.metadata.manufacturerURL!}
                        />
                    )}
                    {/* Font version number */}
                    {props.metadata.version && (
                        <Typography variant="body2">
                            {versionWording} {props.metadata.version}
                        </Typography>
                    )}
                    {/* LicenseURL */}
                    {props.metadata.licenseURL && (
                        <SmallLink
                            href={props.metadata.licenseURL}
                            linkText={licenseWording}
                        />
                    )}
                    {suitability !== "ok" && (
                        <InfoOutlinedIcon
                            htmlColor={kDisabledControlGray}
                            css={css`
                                position: absolute !important;
                                bottom: 4px;
                                right: 4px;
                            `}
                            onClick={() =>
                                showFontDeveloperData(props.metadata)
                            }
                        />
                    )}
                </div>
            )}
        </div>
    );
};

export default FontInformationPane;
