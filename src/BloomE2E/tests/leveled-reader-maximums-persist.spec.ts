// The maximums a person ticks and unticks for a level in the Leveled Reader's "Set Up Levels"
// dialog are as they left them when they click OK and open the dialog again. Automates the manual
// test "Maximums Persist After Reopen" (Test Case ID 468): "Check/unchecking various maximums.
// click ok, then reopen, they should be as you left them." Its prior issue, BL-9002, was that an
// unticked maximum came back ticked after OK and reopen, and the level list down the left kept
// showing it.
//
// The level starts with two maximums set ("Per Sentence" 5 and "Per Book" 40) and the others off.
// The test unticks those two, the way the case's longer source (TC314) does: it deletes the number
// and then unticks the box. It ticks three that were off and types a number into each. After OK it
// checks what Bloom saved, then reopens the dialog and checks each maximum's check box and text box,
// and the level list, against what it left. The maximums it did not touch must still be off.

import { expect, test } from "../fixtures/bloomTest";
import { makeBookFromTemplate } from "../helpers/bookMaking";
import {
    acceptLeveledReaderSetup,
    changeSavedReaderSettings,
    enableLeveledReaderTool,
    getLevelListMaximums,
    getSavedReaderSettings,
    getShownLevelMaximums,
    type IReaderLevel,
    LEVEL_MAXIMUMS,
    type LevelMaximum,
    openLeveledReaderLevels,
    setLevelMaximum,
} from "../helpers/readerSetup";

test.use({
    collectionSpec: {
        name: "leveled-reader-maximums-persist",
        languages: ["en"],
    },
});

/** The level's maximums before the test changes them; 0 is none. */
const STARTING_LEVEL: IReaderLevel = {
    name: "1",
    thingsToRemember: [],
    maxGlyphsPerWord: 0,
    maxAverageGlyphsPerWord: 0,
    maxWordsPerSentence: 5,
    maxAverageWordsPerSentence: 0,
    maxWordsPerPage: 0,
    maxAverageWordsPerPage: 0,
    maxWordsPerBook: 40,
    maxUniqueWordsPerBook: 0,
    maxSentencesPerPage: 0,
    maxAverageSentencesPerPage: 0,
};

/** What the test does to each maximum it changes: a number turns it on, null turns it off. */
const CHANGES: Partial<Record<LevelMaximum, number | null>> = {
    "glyphs-per-word": 6, // "In any Word", off before
    "words-per-sentence": null, // "Per Sentence", 5 before
    "average-words-per-page": 2, // "Average per Page", off before
    "words-per-book": null, // "Per Book", 40 before
    "sentences-per-page": 3, // sentences "Per Page", off before
};

/** Which field of a saved level holds each maximum. */
const SAVED_FIELD: Record<LevelMaximum, keyof IReaderLevel> = {
    "glyphs-per-word": "maxGlyphsPerWord",
    "average-glyphs-per-word": "maxAverageGlyphsPerWord",
    "words-per-sentence": "maxWordsPerSentence",
    "average-words-per-sentence": "maxAverageWordsPerSentence",
    "words-per-page": "maxWordsPerPage",
    "average-words-per-page": "maxAverageWordsPerPage",
    "words-per-book": "maxWordsPerBook",
    "unique-words-per-book": "maxUniqueWordsPerBook",
    "sentences-per-page": "maxSentencesPerPage",
    "average-sentences-per-page": "maxAverageSentencesPerPage",
};

/** Each maximum's value as the person left it: the starting one unless the test changed it. */
function expectedValue(maximum: LevelMaximum): number {
    if (maximum in CHANGES) {
        return CHANGES[maximum] ?? 0;
    }
    return STARTING_LEVEL[SAVED_FIELD[maximum]] as number;
}

function savedMaximums(level: IReaderLevel): Record<LevelMaximum, number> {
    const saved = {} as Record<LevelMaximum, number>;
    for (const maximum of LEVEL_MAXIMUMS) {
        saved[maximum] = (level[SAVED_FIELD[maximum]] as number) || 0;
    }
    return saved;
}

function expectedSavedMaximums(): Record<LevelMaximum, number> {
    const expected = {} as Record<LevelMaximum, number>;
    for (const maximum of LEVEL_MAXIMUMS) {
        expected[maximum] = expectedValue(maximum);
    }
    return expected;
}

test("maximums ticked and unticked for a level are as left after OK and reopen (Test Case ID 468)", async ({
    page,
}) => {
    await makeBookFromTemplate(page, "Basic Book");
    await enableLeveledReaderTool(page);
    // A new collection may arrive with levels copied from this machine's app-data folder, so the
    // test states the one level it works on.
    await changeSavedReaderSettings(page, (settings) => {
        settings.levels = [{ ...STARTING_LEVEL }];
    });

    await openLeveledReaderLevels(page);

    // Sanity check: the dialog shows the starting maximums, so the changes below are changes.
    const before = await getShownLevelMaximums(page);
    for (const maximum of LEVEL_MAXIMUMS) {
        const value = STARTING_LEVEL[SAVED_FIELD[maximum]] as number;
        expect(
            before[maximum].checked,
            `Before any change, "${maximum}" should be ${value ? "ticked" : "unticked"}.`,
        ).toBe(value !== 0);
        if (value) {
            expect(before[maximum].value).toBe(value.toString());
        }
    }

    for (const [maximum, value] of Object.entries(CHANGES)) {
        await setLevelMaximum(page, maximum as LevelMaximum, value);
    }
    await acceptLeveledReaderSetup(page);

    // OK closes the dialog before Bloom has finished saving.
    await expect
        .poll(
            async () => {
                const levels = (await getSavedReaderSettings(page)).levels;
                return levels?.length === 1 ? savedMaximums(levels[0]) : levels;
            },
            {
                timeout: 30000,
                message:
                    "Bloom did not save the maximums as the dialog left them.",
            },
        )
        .toEqual(expectedSavedMaximums());

    await openLeveledReaderLevels(page);

    const after = await getShownLevelMaximums(page);
    for (const maximum of LEVEL_MAXIMUMS) {
        const value = expectedValue(maximum);
        expect(
            after[maximum],
            `After OK and reopen, "${maximum}" should be ${value ? `ticked with ${value}` : "unticked, its box empty and disabled"}.`,
        ).toEqual({
            checked: value !== 0,
            enabled: value !== 0,
            value: value ? value.toString() : "",
        });
    }

    const list = await getLevelListMaximums(page, 0);
    for (const maximum of LEVEL_MAXIMUMS) {
        const value = expectedValue(maximum);
        expect(
            list[maximum],
            `After OK and reopen, the level list should show ${value ? value : "no"} "${maximum}" for level 1.`,
        ).toBe(value ? value.toString() : "");
    }
});
