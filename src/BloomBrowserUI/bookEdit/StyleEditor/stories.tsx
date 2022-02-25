/** @jsx jsx **/
import { jsx, css } from "@emotion/core";
import * as React from "react";
import { storiesOf } from "@storybook/react";
import FontSelectComponent, { IFontMetaData } from "./fontSelectComponent";
import FontInformationPane from "../../react_components/fontInformationPane";
import { Typography } from "@material-ui/core";

const font1: IFontMetaData = {
    name: "Arial",
    determinedSuitability: "ok",
    variants: ["regular", "bold", "italic", "bold italic"],
    designer: "Monotype",
    fsType: "0",
    designerURL: "http://www.google.com"
};
const font2: IFontMetaData = {
    name: "Chiller",
    determinedSuitability: "unknown"
};
const font3: IFontMetaData = {
    name: "Microsoft YaHei",
    determinedSuitability: "unsuitable",
    determinedSuitabilityNotes: "Bloom does not support TTC fonts."
};
const font4: IFontMetaData = {
    name: "Back Issues BB",
    version: "6.0.0",
    licenseURL: "https://foo.com",
    copyright: "foo 2004-2005",
    manufacturer: "Nate Piekos",
    manufacturerURL: "https://Blambot.com",
    fsType: "Print and preview",
    variants: ["regular"],
    determinedSuitability: "unknown",
    determinedSuitabilityNotes:
        "Has a good fsType, but as this is an unvetted manufacturer, we cannot know unambiguously what is allowed without studying the license."
};

const metadata: IFontMetaData[] = [];
metadata.push(font1);
metadata.push(font2);
metadata.push(font3);
metadata.push(font4);

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

storiesOf("Format dialog", module)
    .add("Font Select-current ok", () => {
        return React.createElement(() => (
            <Frame>
                <FontSelectComponent
                    fontMetadata={metadata}
                    currentFontName={font1.name}
                />
            </Frame>
        ));
    })
    .add("Font Select-current unknown", () => {
        return React.createElement(() => (
            <Frame>
                <FontSelectComponent
                    fontMetadata={metadata}
                    currentFontName={font2.name}
                />
            </Frame>
        ));
    })
    .add("Font Select-current unsuitable", () => {
        return React.createElement(() => (
            <Frame>
                <FontSelectComponent
                    fontMetadata={metadata}
                    currentFontName={font3.name}
                />
            </Frame>
        ));
    })
    .add("FontInformationPane unsuitable", () => {
        return React.createElement(() => (
            <Frame>
                <FontInformationPane metadata={font3} />
                <Typography variant="h6">
                    For some reason, this test needs refreshing to size itself
                    correctly.
                </Typography>
            </Frame>
        ));
    });
