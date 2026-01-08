import { css } from "@emotion/react";
import * as React from "react";
import FontSelectComponent from "./fontSelectComponent";
import FontInformationPane from "../../react_components/fontInformationPane";
import { IFontMetaData } from "./fontSelectComponent";
import { Typography } from "@mui/material";

const Frame: React.FunctionComponent = ({ children }) => (
    <div
        css={css`
            width: 320px;
            height: 320px;
            border: 1px solid green;
            padding: 20px;
        `}
    >
        {children}
    </div>
);

const suitableFont: IFontMetaData = {
    name: "Arial",
    determinedSuitability: "ok",
    variants: ["regular", "bold", "italic", "bold italic"],
    designer: "Monotype",
    fsType: "0",
    designerURL: "http://www.google.com",
};
const unknownFont: IFontMetaData = {
    name: "Chiller",
    determinedSuitability: "unknown",
};
const unsuitableFont: IFontMetaData = {
    name: "Microsoft YaHei",
    determinedSuitability: "invalid",
    determinedSuitabilityNotes: "Bloom does not support .ttc fonts.",
};
const moreCompleteUnknownFont: IFontMetaData = {
    name: "Back Issues BB",
    version: "Version 6.0.0",
    licenseURL: "https://foo.com",
    copyright: "foo 2004-2005",
    manufacturer: "Nate Piekos",
    manufacturerURL: "https://Blambot.com",
    fsType: "Print and preview",
    variants: ["regular"],
    determinedSuitability: "unknown",
    determinedSuitabilityNotes:
        "Has a good fsType, but as this is an unvetted manufacturer, we cannot know unambiguously what is allowed without studying the license.",
};

const fontTestData = [
    suitableFont,
    unknownFont,
    unsuitableFont,
    moreCompleteUnknownFont,
];

export default {
    title: "Format dialog",
};

export const FontSelectCurrentOk = () => {
    return React.createElement(() => (
        <Frame>
            <FontSelectComponent
                fontMetadata={fontTestData}
                currentFontName={suitableFont.name}
                languageNumber={0}
            />
        </Frame>
    ));
};

FontSelectCurrentOk.story = {
    name: "Font Select-current ok",
};

export const FontSelectCurrentUnknown = () => {
    return React.createElement(() => (
        <Frame>
            <FontSelectComponent
                fontMetadata={fontTestData}
                currentFontName={unknownFont.name}
                languageNumber={1}
            />
        </Frame>
    ));
};

FontSelectCurrentUnknown.story = {
    name: "Font Select-current unknown",
};

export const FontSelectCurrentUnsuitable = () => {
    return React.createElement(() => (
        <Frame>
            <FontSelectComponent
                fontMetadata={fontTestData}
                currentFontName={unsuitableFont.name}
                languageNumber={2}
            />
        </Frame>
    ));
};

FontSelectCurrentUnsuitable.story = {
    name: "Font Select-current unsuitable",
};

export const FontInformationPaneUnsuitable = () => {
    return React.createElement(() => (
        <Frame>
            <FontInformationPane metadata={fontTestData[2]} />
            <Typography variant="h6">
                For some reason, this test needs refreshing to size itself
                correctly.
            </Typography>
        </Frame>
    ));
};

FontInformationPaneUnsuitable.story = {
    name: "FontInformationPane unsuitable",
};
