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
import {
    expandReaderTool,
    generateDecodableReport,
} from "../helpers/readerTools";
import { getUiStrings } from "../helpers/uiLanguage";
import { switchTab } from "../helpers/workspace";

test.use({
    collectionSpec: { name: "spanish-decodable-report", languages: ["es"] },
});

const kReportName = "Decodable Books Letters and Words.txt";

/**
 * The report's headings and labels, by their ids in Bloom's localization files, with their
 * English. Bloom writes the report in the UI language it is running in, putting the stage's number
 * or words in place of {0}.
 */
const kReportLabels = {
    stage: {
        id: "EditTab.Toolbox.DecodableReaderTool.LetterWordReportStage",
        english: "Stage {0}",
    },
    letters: {
        id: "EditTab.Toolbox.DecodableReaderTool.LetterWordReportLetters",
        english: "Letters: {0}",
    },
    sightWords: {
        id: "EditTab.Toolbox.DecodableReaderTool.LetterWordReportSightWords",
        english: "New Sight Words: {0}",
    },
    newDecodableWords: {
        id: "EditTab.Toolbox.DecodableReaderTool.LetterWordReportNewDecodableWords",
        english: "New Decodable Words: {0}",
    },
    allUsableWords: {
        id: "EditTab.Toolbox.DecodableReaderTool.LetterWordReportAllUsableWords",
        english: "All Usable Words: {0}",
    },
};
type ReportLabel = keyof typeof kReportLabels;

/** What the report says about one stage: its number, and its lists as sorted words. */
interface IStageReport {
    number: number;
    letters: string[];
    sightWords: string[];
    newDecodableWords: string[];
    allUsableWords: string[];
}

/** A pattern for a whole line of the report written with `format`, capturing what fills {0}. */
function linePattern(format: string): RegExp {
    const [before, after] = format
        .split("{0}")
        .map((part) => part.replace(/[.*+?^${}()|[\]\\]/g, "\\$&"));
    return new RegExp(`^${before}(.*?)${after}\\s*$`, "m");
}

/**
 * Split the report into its stages, at each stage heading, and read each stage's lines. Letters and
 * sight words are separated by commas, the word lists by spaces.
 */
function readStageReports(
    report: string,
    formats: Record<ReportLabel, string>,
): IStageReport[] {
    const heading = new RegExp(linePattern(formats.stage).source, "gm");
    const headings = [...report.matchAll(heading)];
    return headings.map((match, index) => {
        const text = report.slice(
            match.index,
            headings[index + 1]?.index ?? report.length,
        );
        const field = (label: Exclude<ReportLabel, "stage">) => {
            const line = linePattern(formats[label]).exec(text);
            if (!line)
                throw new Error(
                    `The report's stage has no "${formats[label]}" line:\n${text}`,
                );
            return line[1]
                .split(/[ ,]+/)
                .filter((word) => word)
                .sort();
        };
        return {
            number: Number(match[1]),
            letters: field("letters"),
            sightWords: field("sightWords"),
            newDecodableWords: field("newDecodableWords"),
            allUsableWords: field("allUsableWords"),
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
    await generateDecodableReport(page);
    expect(
        fs.existsSync(reportPath),
        `Generate Report did not write ${reportPath}.`,
    ).toBe(true);

    const labels = Object.keys(kReportLabels) as ReportLabel[];
    const strings = await getUiStrings(
        page,
        Object.fromEntries(
            labels.map((label) => [
                kReportLabels[label].id,
                kReportLabels[label].english,
            ]),
        ),
    );
    const formats = Object.fromEntries(
        labels.map((label) => [label, strings[kReportLabels[label].id]]),
    ) as Record<ReportLabel, string>;
    const stages = readStageReports(
        fs.readFileSync(reportPath, "utf8"),
        formats,
    );
    expect(
        stages,
        "The report should have one section per stage.",
    ).toHaveLength(4);
    expect(stages).toEqual([
        {
            number: 1,
            letters: ["a", "e", "l", "m", "s"],
            sightWords: ["uno"],
            newDecodableWords: ["ama", "mesa", "sala"],
            allUsableWords: ["ama", "mesa", "sala", "uno"],
        },
        {
            number: 2,
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
            number: 3,
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
            number: 4,
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
