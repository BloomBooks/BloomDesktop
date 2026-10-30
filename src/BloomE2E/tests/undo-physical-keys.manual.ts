// Ctrl+Z and Ctrl+Y pressed on a real keyboard reach the page's own handlers. This test needs a
// person, so it is a .manual.ts file: a normal run (and so the nightly) never collects it, and it
// runs only when PLAYWRIGHT_INCLUDE_MANUAL is set (see playwright.config.ts).
//
// Why it exists: every other test presses keys through Playwright, which sends them straight to the
// WebView2 over CDP and so never passes through Bloom's WinForms shell. If the shell claimed a key,
// those tests could not tell. This one waits for the keys a person presses, which do go through the
// shell, and checks that the page history undid and redid the typing and that CKEditor's own undo
// and redo did not run, exactly as undo-routing.spec.ts finds for the same keys sent over CDP.
//
// It takes the keyboard only when the person gives it, by clicking in Bloom's window, so like the rest
// of the suite it never steals the focus itself; Bloom's window just has to be on a screen (leave
// BLOOM_AUTOMATION_MONITOR unset or naming a monitor, not "headless"). To run it, from
// src/BloomE2E:
//
//     $env:PLAYWRIGHT_INCLUDE_MANUAL = "1"
//     pnpm exec playwright test tests/undo-physical-keys.manual.ts
//
// When the run prints "Now press Ctrl+Z", click at the end of the word "cat" in Bloom's window,
// then press Ctrl+Z on the keyboard; when it prints "Now press Ctrl+Y", press Ctrl+Y.

import { expect, test } from "../fixtures/bloomTest";
import {
    addPage,
    clickInGroup,
    getContentPages,
    goToPage,
    makeBookFromTemplate,
} from "../helpers/bookMaking";
import { typeWithKeys } from "../helpers/keys";
import {
    expectUndoAndRedoCalls,
    waitForPageHistory,
    watchUndoAndRedo,
} from "../helpers/undo";

test.use({
    collectionSpec: { name: "undo-physical-keys", languages: ["en"] },
});

// How long to wait for a person to press each key.
const WAIT_FOR_A_PERSON_MS = 180000;

test("Ctrl+Z and Ctrl+Y on a real keyboard reach the page history, not CKEditor", async ({
    page,
}) => {
    test.setTimeout(600000);
    await makeBookFromTemplate(page, "Basic Book");
    await addPage(page, "Just Text");
    const [textPage] = await getContentPages(page);
    await goToPage(page, textPage.id);
    await waitForPageHistory(page);
    const box = await clickInGroup(page, ".bloom-translationGroup", "en");
    await typeWithKeys(page, "cat");
    await expect(box).toHaveText("cat");
    await watchUndoAndRedo(page);

    console.log(
        "Now press Ctrl+Z: click at the end of the word 'cat' in Bloom's window, then press Ctrl+Z.",
    );
    await expectUndoAndRedoCalls(
        page,
        { historyUndo: 1, historyRedo: 0, ckeditorCommands: [] },
        "a person pressing Ctrl+Z",
        WAIT_FOR_A_PERSON_MS,
    );
    await expect(box).not.toHaveText("cat");
    console.log("Now press Ctrl+Y.");
    await expectUndoAndRedoCalls(
        page,
        { historyUndo: 1, historyRedo: 1, ckeditorCommands: [] },
        "a person pressing Ctrl+Y",
        WAIT_FOR_A_PERSON_MS,
    );
    await expect(box).toHaveText("cat");
});
