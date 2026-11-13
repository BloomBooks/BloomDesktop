import * as fs from "node:fs";
import * as path from "node:path";
import { describe, it, expect } from "vitest";
import { ALL_TABLE_UI_STRINGS } from "bloom-table";

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

describe("bloom-table UI strings", () => {
    it("each has an English XLF entry with the library's English", () => {
        const sources = readEnglishXlfSources();
        // Sanity check that the XLF files were found and read.
        expect(sources.get("Common.OK")).toBe("OK");
        const libraryIds = Object.keys(ALL_TABLE_UI_STRINGS);
        expect(libraryIds.length).toBeGreaterThan(50);

        const problems = Object.entries(ALL_TABLE_UI_STRINGS).flatMap(
            ([xlfId, english]) => {
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
