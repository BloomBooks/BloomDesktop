import { describe, expect, it } from "vitest";
import { changedRestartPaths } from "./collectionSettingsRestart";
import {
    ICollectionSettingsLanguage,
    ICollectionSettingsValues,
} from "./collectionSettingsTypes";

function makeLanguage(tag: string): ICollectionSettingsLanguage {
    return {
        tag,
        name: tag.toUpperCase(),
        isCustomName: false,
        fontName: "Andika",
        isRightToLeft: false,
        lineHeight: 0,
        breaksLinesOnlyAtSpaces: false,
        baseUIFontSizeInPoints: 10,
    };
}

function makeValues(): ICollectionSettingsValues {
    return {
        languages: {
            language1: makeLanguage("xkal"),
            language2: makeLanguage("en"),
            language3: null,
            signLanguage: { tag: "", name: "", isCustomName: false },
        },
        frontBackMatter: {
            xmatter: "Traditional",
            pageNumberStyle: "Decimal",
            showQrCode: false,
            qrcodeCaption: "",
            country: "",
            province: "",
            district: "",
        },
        advanced: { autoUpdate: true, collectionName: "Test Collection" },
        experimental: { "team-collections": false },
    };
}

const restartPaths = [
    "languages.language1.tag",
    "languages.language3",
    "frontBackMatter.xmatter",
    "experimental.team-collections",
];

describe("changedRestartPaths", () => {
    it("finds a nested value that changed", () => {
        const initial = makeValues();
        const current = makeValues();
        // Sanity check that the two start out matching, so a pass below means something.
        expect(changedRestartPaths(initial, current, restartPaths)).toEqual([]);

        current.languages.language1.tag = "fr";

        expect(changedRestartPaths(initial, current, restartPaths)).toEqual([
            "languages.language1.tag",
        ]);
    });

    it("reports nothing when a non-restart value changes", () => {
        const initial = makeValues();
        const current = makeValues();
        current.advanced.collectionName = "Renamed";
        current.frontBackMatter.qrcodeCaption = "Find more books";

        expect(changedRestartPaths(initial, current, restartPaths)).toEqual([]);
    });

    it("reports nothing when a value is changed and then changed back", () => {
        const initial = makeValues();
        const current = makeValues();
        current.frontBackMatter.xmatter = "Device";
        expect(changedRestartPaths(initial, current, restartPaths)).toEqual([
            "frontBackMatter.xmatter",
        ]);

        current.frontBackMatter.xmatter = "Traditional";

        expect(changedRestartPaths(initial, current, restartPaths)).toEqual([]);
    });

    it("treats a null language3 as unchanged, but notices one being added", () => {
        const initial = makeValues();
        const current = makeValues();
        expect(initial.languages.language3).toBeNull();
        expect(changedRestartPaths(initial, current, restartPaths)).toEqual([]);

        current.languages.language3 = makeLanguage("es");

        expect(changedRestartPaths(initial, current, restartPaths)).toEqual([
            "languages.language3",
        ]);
    });

    it("does not walk off the end of a path that runs through a null language3", () => {
        const initial = makeValues();
        const current = makeValues();
        current.languages.language3 = makeLanguage("es");

        expect(
            changedRestartPaths(initial, current, [
                "languages.language3.fontName",
            ]),
        ).toEqual(["languages.language3.fontName"]);
    });

    it("finds several changed paths at once", () => {
        const initial = makeValues();
        const current = makeValues();
        current.frontBackMatter.xmatter = "Device";
        current.experimental["team-collections"] = true;

        expect(changedRestartPaths(initial, current, restartPaths)).toEqual([
            "frontBackMatter.xmatter",
            "experimental.team-collections",
        ]);
    });
});
