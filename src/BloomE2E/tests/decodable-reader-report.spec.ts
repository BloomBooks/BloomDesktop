// The Decodable Reader tool's "Generate Report" button writes a report of the collection's stages
// to "Decodable Books Letters and Words.txt" in the collection's folder: for each stage, its
// letters, its new sight words, the sample-text words that become decodable there, and every word
// usable so far. This is the "Generate Report" step of the manual test "Build a Spanish Decodable
// From Scratch" (Test Case ID 427); the rest of that case is decodable-reader-spanish.spec.ts.
//
// Bloom then opens the report in the program the machine opens .txt files with
// (ProcessExtra.SafeStartInFront, in ReadersApi.MakeLetterAndWordList), under --dont-disturb too, so
// this test runs only where Bloom may take the foreground: on CI, or with BLOOM_E2E_DONT_DISTURB=0.
// See "Collection menu commands open Notepad and File Explorer" in AUTOMATION-DEBT.md. The test
// reads the report from disk, so nothing it checks depends on that window.

import * as fs from "node:fs";
import * as Path from "node:path";
import { expect, test } from "../fixtures/bloomTest";
import { launchWithDontDisturb } from "../fixtures/launchBloom";
import { makeBookFromTemplate } from "../helpers/bookMaking";
import {
    changeSavedReaderSettings,
    setSampleTexts,
} from "../helpers/readerSetup";
import { expandReaderTool } from "../helpers/readerTools";
import { toolboxFrame } from "../helpers/toolbox";
import { switchTab } from "../helpers/workspace";

test.use({
    collectionSpec: { name: "spanish-decodable-report", languages: ["es"] },
});

const kReportName = "Decodable Books Letters and Words.txt";

/** What the report should say about one stage, as sets of words. */
interface IStageReport {
    letters: string[];
    sightWords: string[];
    newDecodableWords: string[];
    allUsableWords: string[];
}

/**
 * Split the report into its stages, reading each "Label: words" line. Letters and sight words are
 * separated by commas, the word lists by spaces.
 */
function readStageReports(report: string): IStageReport[] {
    const stages = report.split(/^Stage \d+\s*$/m).slice(1);
    return stages.map((stage) => {
        const field = (label: string) => {
            const match = new RegExp(`^${label}: ?(.*)$`, "m").exec(stage);
            if (!match)
                throw new Error(
                    `The report's stage has no "${label}" line:\n${stage}`,
                );
            return match[1]
                .split(/[ ,]+/)
                .filter((word) => word)
                .sort();
        };
        return {
            letters: field("Letters"),
            sightWords: field("New Sight Words"),
            newDecodableWords: field("New Decodable Words"),
            allUsableWords: field("All Usable Words"),
        };
    });
}

test("Generate Report lists each stage's letters, sight words, new decodable words and usable words [Test Case ID 427]", async ({
    page,
    bloomApp,
}) => {
    test.skip(
        launchWithDontDisturb(),
        "Bloom opens the report in Notepad, so this runs only where Bloom may take the " +
            "foreground: on CI, or with BLOOM_E2E_DONT_DISTURB=0.",
    );

    await makeBookFromTemplate(page, "Decodable Reader");
    // The stages that decodable-reader-spanish.spec.ts builds in the setup dialog, set here
    // directly, since this test is about the report.
    await changeSavedReaderSettings(page, (settings) => {
        settings.letters =
            "a á b c d e é f g h i í j k l m n ñ o ó p q r s t u ú v w x y z";
        settings.moreWords = "";
        settings.useAllowedWords = 0;
        settings.stages = [
            { name: "1", letters: "a m s e l", sightWords: "uno" },
            { name: "2", letters: "o p t", sightWords: "dos" },
            { name: "3", letters: "i n d u", sightWords: "" },
            { name: "4", letters: "c r b ñ", sightWords: "" },
        ];
    });
    setSampleTexts(bloomApp.collectionDir, {
        "cuento uno.txt": "mesa mesa sala lata pato ama",
        "cuento dos.txt": "mesa mesa sala pato niño",
    });
    // The toolbox reads the settings and the sample texts when the Edit tab comes up.
    await switchTab(page, "collection");
    await switchTab(page, "edit");
    await expandReaderTool(page, "decodableReader");

    const reportPath = Path.join(bloomApp.collectionDir, kReportName);
    fs.rmSync(reportPath, { force: true });
    const button = toolboxFrame(page).getByTestId(
        "EditTab.Toolbox.DecodableReaderTool.GenerateReport",
    );
    await button.scrollIntoViewIfNeeded();
    await button.click();
    await expect
        .poll(() => fs.existsSync(reportPath), {
            timeout: 30000,
            message: `Generate Report never wrote ${reportPath}.`,
        })
        .toBe(true);

    const stages = readStageReports(fs.readFileSync(reportPath, "utf8"));
    expect(
        stages,
        "The report should have one section per stage.",
    ).toHaveLength(4);
    expect(stages).toEqual([
        {
            letters: ["a", "e", "l", "m", "s"],
            sightWords: ["uno"],
            newDecodableWords: ["ama", "mesa", "sala"],
            allUsableWords: ["ama", "mesa", "sala", "uno"],
        },
        {
            letters: ["o", "p", "t"],
            sightWords: ["dos"],
            newDecodableWords: ["lata", "pato"],
            allUsableWords: [
                "ama",
                "dos",
                "lata",
                "mesa",
                "pato",
                "sala",
                "uno",
            ],
        },
        {
            letters: ["d", "i", "n", "u"],
            sightWords: [],
            newDecodableWords: [],
            allUsableWords: [
                "ama",
                "dos",
                "lata",
                "mesa",
                "pato",
                "sala",
                "uno",
            ],
        },
        {
            letters: ["b", "c", "r", "ñ"],
            sightWords: [],
            newDecodableWords: ["niño"],
            allUsableWords: [
                "ama",
                "dos",
                "lata",
                "mesa",
                "niño",
                "pato",
                "sala",
                "uno",
            ],
        },
    ]);
});
