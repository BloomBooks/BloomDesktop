// How Bloom hosts EthnoLib's language chooser in the Collection Settings dialog: the dialog it
// opens in fits Bloom's window, the browser prompt that Ctrl-clicking the Customize dialog's tag
// opens works in Bloom's WebView2, which has no dialog handling of its own, and both Escape and
// Cancel close the chooser without changing the collection's settings. The chooser itself (search,
// cards, tag building, its Customize dialog) has its own tests in EthnoLib.
//
// Test Case ID 802, "Language Chooser". Not here: its steps in the New Collection wizard, a
// WinForms dialog that the suite cannot open (AUTOMATION-DEBT.md, "WinForms surfaces are invisible
// to CDP"). Its checks at 100%, 125% and 150% Windows display scaling are emulated in the page (a run
// cannot change the display's own setting), so the WinForms window around the page is not checked
// at those scales.

import type { Page } from "@playwright/test";
import { expect, test } from "../fixtures/bloomTest";
import { resetViewportSize, setViewportSize } from "../fixtures/viewport";
import {
    getCollectionSettingsLanguage,
    readSavedLanguageSetting,
} from "../helpers/collectionSettings";
import {
    cancelLanguageChooser,
    cancelSettingsDialog,
    chooseLanguage,
    escapeLanguageChooser,
    getChosenLanguageTag,
    getCustomizeTag,
    getLanguageChooserFit,
    isSettingsDialogOpen,
    kEnglishLanguageChooserLabels,
    okLanguageChooser,
    openCustomizeDialog,
    openLanguageChooser,
    openLanguagesPage,
    searchForLanguage,
    typeLanguageTagByHand,
} from "../helpers/languagePicker";
import { setLanguageChooserName } from "../helpers/languageChooser";
import { setUiStateViaApi } from "../helpers/uiLanguage";

test.use({
    collectionSpec: { name: "language-chooser-hosting", languages: ["en"] },
});

const kLanguage1 = kEnglishLanguageChooserLabels.language1;
// What the chooser says when a typed tag is not one (EthnoLib's English).
const kInvalidTagWarning = "This is not in a valid IETF BCP 47 format";
const kHandTypedName = "Tok Pisin by hand";

test.beforeEach(async ({ bloomApp }) => {
    // The helpers find Bloom's controls by their English names, and the warning is English.
    await setUiStateViaApi(bloomApp, "en", false);
});

// The display scalings the card names, each emulated for a maximized Bloom on a 1920x1080 screen,
// whatever the scaling of the monitor the run is on: Bloom's page area there is about 1920x1000
// screen pixels (the taskbar and the window's title bar take the rest), which is that size divided
// by the scale in CSS pixels.
const kScaledScreens = [
    { scale: 1, size: { width: 1920, height: 1000 } },
    { scale: 1.25, size: { width: 1536, height: 800 } },
    { scale: 1.5, size: { width: 1280, height: 667 } },
];

test("the language chooser opens inside Bloom's window, with no control cut off and only the language list scrolling, at 100%, 125% and 150% display scaling [Test Case ID 802]", async ({
    page,
    step,
}) => {
    await step("Open the Languages page of Collection Settings", () =>
        openLanguagesPage(page),
    );

    // The run's own size too: the window's, or the one BLOOM_E2E_VIEWPORT asks for (the nightly's
    // 1008x681 is narrower than any screen below).
    await step(
        "At the run's own window size, open the chooser and check it fits",
        () => expectLanguageChooserFits(page),
    );

    try {
        for (const { scale, size } of kScaledScreens) {
            await step(
                `At ${scale * 100}% scaling (${size.width}x${size.height} CSS pixels), open the chooser and check it fits`,
                async () => {
                    await setViewportSize(page, size, scale);
                    await expect
                        .poll(() =>
                            page.evaluate(() => [
                                window.innerWidth,
                                window.devicePixelRatio,
                            ]),
                        )
                        .toEqual([size.width, scale]);
                    await expectLanguageChooserFits(page);
                },
            );
        }
    } finally {
        await resetViewportSize(page);
    }

    await step("Close the Settings dialog", () => cancelSettingsDialog(page));
});

/**
 * Open the chooser from Change... beside Language 1, check that it lies inside Bloom's window with
 * none of its controls cut off by its edge and nothing scrolling but its list of languages, and
 * close it again.
 */
async function expectLanguageChooserFits(page: Page): Promise<void> {
    await openLanguageChooser(page, kLanguage1, "change");
    const fit = await getLanguageChooserFit(page);
    expect(
        fit.dialog.right - fit.dialog.left,
        "sanity check: the dialog has a size",
    ).toBeGreaterThan(0);
    const edgesOutside = [
        fit.dialog.left < 0 && "left",
        fit.dialog.top < 0 && "top",
        fit.dialog.right > fit.window.width + 0.5 && "right",
        fit.dialog.bottom > fit.window.height + 0.5 && "bottom",
    ].filter(Boolean);
    expect(
        edgesOutside,
        `edges of the chooser ${JSON.stringify(fit.dialog)} outside Bloom's ${fit.window.width}x${fit.window.height} window`,
    ).toEqual([]);
    expect(fit.controlsCutOff, "controls cut off by the dialog's edge").toEqual(
        [],
    );
    // The search results hold every language, so their list scrolls; nothing else should.
    expect(fit.scrollingParts).toEqual(["language-card-list (up and down)"]);
    await cancelLanguageChooser(page);
}

test("a tag typed by hand after Ctrl-clicking the Customize tag is taken when valid and refused with a warning when not [Test Case ID 802]", async ({
    page,
    step,
}) => {
    await step(
        "Open the chooser on English, then its Customize dialog",
        async () => {
            await openLanguagesPage(page);
            await openLanguageChooser(page, kLanguage1, "change");
            await expect.poll(() => getChosenLanguageTag(page)).toBe("en");
            await openCustomizeDialog(page);
        },
    );

    await step(
        "Type a tag that is not valid: a warning, and nothing changes",
        async () => {
            const typed = await typeLanguageTagByHand(page, "not a tag!");
            expect(typed.prompt).toMatch(/ISO 639/);
            expect(typed.alerts).toHaveLength(1);
            expect(typed.alerts[0]).toContain(kInvalidTagWarning);
            expect(typed.alerts[0]).toContain("not a tag!");
            // The Customize dialog stays open (the chooser behind it is hidden from reading while it
            // does), still building the tag it had.
            expect(await getCustomizeTag(page)).toBe("en");
        },
    );

    await step(
        "Type a valid tag: the chooser takes it, and OK puts it in Language 1",
        async () => {
            const typed = await typeLanguageTagByHand(page, "tpi-Latn-PG");
            expect(typed.alerts).toEqual([]);
            await expect
                .poll(() => getChosenLanguageTag(page))
                .toBe("tpi-Latn-PG");
            // A tag typed by hand comes with no name, and the chooser requires one.
            await setLanguageChooserName(page, kHandTypedName);
            await okLanguageChooser(page);
            expect(await getCollectionSettingsLanguage(page, kLanguage1)).toBe(
                `${kHandTypedName} (tpi-Latn-PG)`,
            );
        },
    );

    await step("Cancel the Settings dialog", () => cancelSettingsDialog(page));
});

test("Escape and Cancel each close the chooser and leave the collection's settings as they were [Test Case ID 802]", async ({
    page,
    bloomApp,
    step,
}) => {
    expect(
        readSavedLanguageSetting(
            bloomApp.collectionDir,
            1,
            "LanguageIso639Code",
        ),
        "sanity check: the collection is saved with English as Language 1",
    ).toBe("en");

    await step("Open the chooser and pick Tok Pisin", async () => {
        await openLanguagesPage(page);
        await openLanguageChooser(page, kLanguage1, "change");
        await searchForLanguage(page, "tok pisin");
        await chooseLanguage(page, "tpi");
        expect(
            await getChosenLanguageTag(page),
            "sanity check: Tok Pisin is picked",
        ).toBe("tpi");
    });

    await step(
        "Press Escape: the chooser closes and Language 1 is still English",
        async () => {
            await escapeLanguageChooser(page);
            expect(
                await isSettingsDialogOpen(page),
                "Escape closed the Settings dialog too",
            ).toBe(true);
            expect(await getCollectionSettingsLanguage(page, kLanguage1)).toBe(
                "English (en)",
            );
        },
    );

    await step(
        "Open it again, pick Tok Pisin, click Cancel: Language 1 is still English",
        async () => {
            await openLanguageChooser(page, kLanguage1, "change");
            await expect.poll(() => getChosenLanguageTag(page)).toBe("en");
            await searchForLanguage(page, "tok pisin");
            await chooseLanguage(page, "tpi");
            await cancelLanguageChooser(page);
            expect(await isSettingsDialogOpen(page)).toBe(true);
            expect(await getCollectionSettingsLanguage(page, kLanguage1)).toBe(
                "English (en)",
            );
        },
    );

    await step(
        "Cancel the Settings dialog: the saved settings still have English",
        async () => {
            await cancelSettingsDialog(page);
            expect(
                readSavedLanguageSetting(
                    bloomApp.collectionDir,
                    1,
                    "LanguageIso639Code",
                ),
            ).toBe("en");
        },
    );
});
