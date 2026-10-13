// The journey through the Decodable Reader's "Set Up Stages" dialog: from the toolbox button,
// through the Letters and Decodable Stages tabs, to OK -- and back in again to see that what was
// typed survived.
//
// This is the dedicated journey test for that UI path, so every step here is a real click; no
// part of the dialog is reached through an API. The one API call is an assertion at the end,
// reading the collection's saved reader settings to see what actually reached disk.
//
// Why this path earns a journey test: the dialog is React-rendered in the WORKSPACE root while
// the button that opens it lives in the TOOLBOX frame, and saving hands the settings back across
// that same frame boundary (beginSaveChangedSettings) to be written and re-read. None of that
// boundary is exercised by the unit tests, which cover the dialog's pure helpers only. (BL-16607)
//
// The test adds to what the collection already has rather than assuming what that is. A new
// collection arrives with a full alphabet and a set of worked-out stages, and two dialog rules
// make anything else brittle: a letter already used by an EARLIER stage cannot be added to a
// later one (selectLetter ignores the click), and a stage with no letters, no sight words and no
// word list is dropped when the settings are saved. So the letter this test uses is one it adds
// to the alphabet itself, and the stage it adds is given sight words so that it survives.

import { expect, test } from "../fixtures/bloomTest";
import { makeBookFromTemplate } from "../helpers/bookMaking";
import {
    acceptReaderSetup,
    addStage,
    enableDecodableReaderTool,
    getAlphabetLetters,
    getSavedReaderSettings,
    getStageCount,
    getStageSightWords,
    openDecodableStagesSetup,
    openReaderSetupTab,
    selectStageLetter,
    setAlphabetLetters,
    setStageSightWords,
} from "../helpers/readerSetup";

// A letter the collection's alphabet does not already have, so no earlier stage can be using it
// and the first stage is free to take it.
const NEW_LETTER = "ñ";
const STAGE_1_SIGHT_WORDS = "the and";
const NEW_STAGE_SIGHT_WORDS = "look see";

test.use({
    collectionSpec: { name: "decodable-reader-setup", languages: ["en"] },
});

test("setting up decodable stages keeps the letters and sight words that were typed", async ({
    page,
}) => {
    await makeBookFromTemplate(page, "Basic Book");
    await enableDecodableReaderTool(page);

    await openDecodableStagesSetup(page);

    // Teach the collection a new letter, so the stage below has one it is allowed to take.
    await openReaderSetupTab(page, "letters");
    const startingAlphabet = await getAlphabetLetters(page);
    // sanity check: a new collection comes with an alphabet, and not this letter
    expect(
        startingAlphabet,
        "The collection should start with an alphabet.",
    ).not.toBe("");
    expect(
        startingAlphabet,
        `The collection should not already know "${NEW_LETTER}".`,
    ).not.toContain(NEW_LETTER);
    const alphabet = `${startingAlphabet} ${NEW_LETTER}`;
    await setAlphabetLetters(page, alphabet);

    await openReaderSetupTab(page, "stages");
    const startingStageCount = await getStageCount(page);
    // sanity check: there is a stage selected to type into
    expect(
        startingStageCount,
        "The dialog should open showing at least one stage.",
    ).toBeGreaterThan(0);

    // The first stage is the one selected when the dialog opens, and nothing comes before it,
    // so it may take any letter in the alphabet.
    await selectStageLetter(page, NEW_LETTER);
    await setStageSightWords(page, STAGE_1_SIGHT_WORDS);

    // Adding a stage selects the new one, so the sight words below go into it.
    expect(await addStage(page)).toBe(startingStageCount + 1);
    await setStageSightWords(page, NEW_STAGE_SIGHT_WORDS);

    await acceptReaderSetup(page);

    // What reached disk. This is the half no unit test can see: the settings went from the
    // dialog, across the frame boundary, through Bloom's api, and into the collection.
    const saved = await getSavedReaderSettings(page);
    expect(saved.letters).toBe(alphabet);
    expect(saved.stages).toHaveLength(startingStageCount + 1);
    expect(saved.stages[0].letters).toContain(NEW_LETTER);
    expect(saved.stages[0].sightWords).toBe(STAGE_1_SIGHT_WORDS);
    expect(saved.stages[startingStageCount].sightWords).toBe(
        NEW_STAGE_SIGHT_WORDS,
    );

    // And what the dialog shows when it is opened again, which is the other half: the settings
    // have to come back out of the model the toolbox frame reloaded them into.
    await openDecodableStagesSetup(page);
    await openReaderSetupTab(page, "letters");
    expect(await getAlphabetLetters(page)).toBe(alphabet);

    await openReaderSetupTab(page, "stages");
    expect(await getStageCount(page)).toBe(startingStageCount + 1);
    expect(await getStageSightWords(page)).toBe(STAGE_1_SIGHT_WORDS);
});
