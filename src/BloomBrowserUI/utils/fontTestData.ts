import { IFontMetaData } from "../bookEdit/StyleEditor/fontSelectComponent";

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
    version: "Version 6.0.0",
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

const fontTestData = [font1, font2, font3, font4];

export default fontTestData;
