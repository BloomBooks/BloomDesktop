/** @jsx jsx **/
import { jsx, css } from "@emotion/core";
import * as React from "react";
import { Link, Typography } from "@material-ui/core";

import { IFontMetaData } from "../bookEdit/StyleEditor/fontSelectComponent";
import InfoOutlinedIcon from "@material-ui/icons/InfoOutlined";
import { useL10n } from "./l10nHooks";
import { kDisabledControlGray } from "../bloomMaterialUITheme";

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

    const UnsuitableFontMessage = useL10n(
        "The metadata inside this font tells us that it may not be embedded for free in ebooks and the web. Please use a different font.",
        "FontInformationPane.FontUnsuitable",
        "This shows in the popup when hovering over a font that Bloom can't legally host on its website."
    );

    const mainMessage =
        suitability === "ok"
            ? OkayFontMessage
            : suitability === "unknown"
            ? UnknownFontMessage
            : UnsuitableFontMessage;

    const styleWording = useL10n(
        "Styles",
        "FontInformationPane.Styles",
        "This shows in the popup before the types of variants in the font (e.g. bold, italic)."
    );

    // There is one other 'License' in BookMetaData, but I would like to have the comment here.
    // The other option would be to put it in Common and maybe port the BookMetaData one to Common too.
    const licenseWording = useL10n(
        "License",
        "FontInformationPane.License",
        "This shows in the popup as a link to the font's license."
    );

    const showFontDeveloperData = (fontData: IFontMetaData | undefined) => {
        if (!fontData) return;
        let message = `name: ${fontData.name}\n`;
        message += `ver: ${fontData.version}\n`;
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
        <React.Fragment>
            {!props.metadata || (
                <div
                    css={css`
                        display: flex;
                        flex: 1;
                        flex-direction: column;
                        padding: 10px;
                        max-width: 240px;
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
                    {/* Font version number (which comes to us prefixed with "Version ") */}
                    {props.metadata.version && (
                        <Typography variant="body2">
                            {props.metadata.version}
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
        </React.Fragment>
    );
};

export default FontInformationPane;
