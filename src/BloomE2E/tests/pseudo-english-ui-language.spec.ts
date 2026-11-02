// Bloom offers a "Pseudo-English (i18n test)" UI language on developer, alpha and internal builds, so that
// a tester can see at a glance which on-screen strings really go through localization: everything
// that does comes back bracketed and vowel-doubled, so anything still in plain English is a string
// that was never internationalized. See BL-16748.
//
// What this covers is the offer itself -- that the entry exists on a build that should have it,
// that it is named what Bloom's own code says it is named, and that it sits after the real
// languages rather than sorted in among them. That is the part of the feature written by hand in
// WorkspaceView (CreateLanguageItem's special case, and the append-after-sort in GetLanguageItems);
// the transformation itself belongs to L10NSharp and is covered by its own tests.
//
// Actually *choosing* the entry is not covered yet. That makes Bloom reopen the collection, which
// replaces the shell document; chooseUiLanguage in helpers/uiLanguage.ts waits that out and hands
// back the new page (ui-language.spec.ts switches real languages with it), so that the choice
// really does pseudolocalize the UI, and that choosing English again puts it back, can now be
// tested the same way.
//
// This test has no "[Test Case ID N]" tag because no row in the Notion test inventory covers the
// UI language menu yet. Add one (and put its id in the title) if this becomes a tracked case.
// See README.md for the convention.

import { expect, test } from "../fixtures/bloomTest";
import {
    closeUiLanguageMenu,
    getCurrentUiLanguageTag,
    getOfferedUiLanguages,
    getUiLanguageMenuEntries,
    kPseudoEnglishUiLanguage,
    openUiLanguageMenu,
} from "../helpers/uiLanguage";

test.use({ collectionName: "basic" });

test("the UI language menu offers Pseudo-English, after the real languages", async ({
    page,
}) => {
    // Sanity check the start state: a Bloom already in the pseudo-locale would make the
    // assertions below pass for the wrong reason, since every label would be transformed.
    expect(await getCurrentUiLanguageTag(page)).toBe("en");

    const offered = await getOfferedUiLanguages(page);

    // The e2e suite runs a developer build, which is one of the channels that offers the locale.
    // If this fails on a build that should have it, the channel gate in Program.SetUpLocalization
    // is the place to look.
    expect(offered).toContain(kPseudoEnglishUiLanguage);

    // Last, not sorted in among the real languages: it is a testing device, not a translation.
    expect(offered[offered.length - 1]).toBe(kPseudoEnglishUiLanguage);

    // And it is really in the menu, spelled the same way. The menu sends the chosen entry back to
    // C# by display name, so an entry whose text did not match what Bloom offers would be one the
    // user could click with nothing happening.
    await openUiLanguageMenu(page);
    try {
        const entries = await getUiLanguageMenuEntries(page);
        // The languages come first, in order, followed by the menu's own commands.
        expect(entries.slice(0, offered.length)).toEqual(offered);
    } finally {
        // Leave the menu closed even if the assertion failed, so the shared Bloom is usable by
        // whatever test this worker runs next.
        await closeUiLanguageMenu(page);
    }
});
