import * as fs from "node:fs";
import * as path from "node:path";
import { describe, it, expect, vi } from "vitest";
import { setTableLocalizer, tableUiStrings } from "bloom-table";
import theOneLocalizationManager from "../../lib/localizationManager/localizationManager";
import {
    kTableUiStringIdPrefix,
    startTableUiLocalization,
} from "./tableUiLocalization";

vi.mock("bloom-table", async (importOriginal) => ({
    ...(await importOriginal<typeof import("bloom-table")>()),
    setTableLocalizer: vi.fn(),
}));

vi.mock("../../lib/localizationManager/localizationManager", () => ({
    default: {
        loadStringsPromise: vi.fn(() => Promise.resolve()),
        // Stands in for a dictionary that the preload filled with French.
        getText: vi.fn((id: string) => `fr:${id}`),
    },
}));

// The bloom-table library's menus show whatever Bloom's XLF holds for each of
// its string ids. A string the library adds in a later version, or whose English
// it changes, must get an XLF entry here, or Bloom shows it in English to every
// user. This test is what notices.

const englishXlfFolder = path.resolve(
    __dirname,
    "../../../../DistFiles/localization/en",
);

/** Every trans-unit id → its English source, across the English XLF files. */
function readEnglishXlfSources(): Map<string, string> {
    const sources = new Map<string, string>();
    for (const file of fs.readdirSync(englishXlfFolder)) {
        if (!file.endsWith(".xlf")) continue;
        const xml = fs.readFileSync(path.join(englishXlfFolder, file), "utf8");
        const doc = new DOMParser().parseFromString(xml, "application/xml");
        doc.querySelectorAll("trans-unit").forEach((unit) => {
            sources.set(
                unit.getAttribute("id")!,
                unit.querySelector("source")!.textContent!,
            );
        });
    }
    return sources;
}

describe("startTableUiLocalization", () => {
    it("preloads every library string, then installs a localizer that reads them", async () => {
        expect(setTableLocalizer).not.toHaveBeenCalled();

        startTableUiLocalization();
        // A second page setup on the same page must not fetch again.
        startTableUiLocalization();

        const loadStrings = vi.mocked(
            theOneLocalizationManager.loadStringsPromise,
        );
        expect(loadStrings).toHaveBeenCalledTimes(1);
        const preloaded = loadStrings.mock.calls[0][0] as {
            [id: string]: string;
        };
        expect(Object.keys(preloaded).length).toBe(
            Object.keys(tableUiStrings).length,
        );
        expect(preloaded["EditTab.Table.RowMenu.AddAbove"]).toBe(
            "Add Row Above",
        );

        await vi.waitFor(() => expect(setTableLocalizer).toHaveBeenCalled());
        const localizer = vi.mocked(setTableLocalizer).mock.calls[0][0]!;
        expect(localizer("Add Row Above", "RowMenu.AddAbove")).toBe(
            "fr:EditTab.Table.RowMenu.AddAbove",
        );
    });
});

describe("bloom-table UI strings", () => {
    it("each has an English XLF entry with the library's English", () => {
        const sources = readEnglishXlfSources();
        // Sanity check that the XLF files were found and read.
        expect(sources.get("Common.OK")).toBe("OK");
        const libraryIds = Object.keys(tableUiStrings);
        expect(libraryIds.length).toBeGreaterThan(50);

        const problems = Object.entries(tableUiStrings).flatMap(
            ([id, english]) => {
                const xlfId = kTableUiStringIdPrefix + id;
                const source = sources.get(xlfId);
                if (source === undefined) return [`${xlfId} is missing`];
                if (source !== english)
                    return [
                        `${xlfId} is "${source}" in the XLF but "${english}" in bloom-table`,
                    ];
                return [];
            },
        );
        expect(problems).toEqual([]);
    });
});
