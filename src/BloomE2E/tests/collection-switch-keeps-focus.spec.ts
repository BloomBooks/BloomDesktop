// When the user changes from one collection to another, Bloom must stay the active application
// (Test Case ID 103, from BL-11092 and BL-16784). Bloom closes its whole window and opens a new one
// on the other collection; when the new window comes up without the foreground, its title bar is
// grey, its tooltips don't show and the user's first click only activates it.
//
// The Other Collection dialog offers the collections Bloom opened recently, so the test first
// visits a second collection. The collection it then chooses in the dialog is the one it started
// in, which holds a book, so that the test can wait for the Collections tab to finish showing it
// (the book's preview is the last thing the tab draws) and see that Bloom is still active then.
//
// Bloom takes the foreground only where it may: not under --dont-disturb, which keeps Bloom out of
// a developer's way, so this test runs on CI or with BLOOM_E2E_DONT_DISTURB=0.

import { expect, test } from "../fixtures/bloomTest";
import { launchWithDontDisturb } from "../fixtures/launchBloom";
import { makeBookFromTemplate, typeInGroup } from "../helpers/bookMaking";
import {
    chooseOtherCollection,
    expectBookInPreview,
    makeAnotherCollection,
    openOtherCollection,
} from "../helpers/collection";
import { switchTab } from "../helpers/workspace";
import {
    expectBloomActive,
    kBloomActive,
    readBloomFocus,
} from "../helpers/windowFocus";

test.use({
    collectionSpec: { name: "switch-focus-first", languages: ["en"] },
});

test("Bloom stays the active application when the user changes collections [Test Case ID 103]", async ({
    bloomApp,
    step,
}) => {
    test.skip(
        launchWithDontDisturb(),
        "Under --dont-disturb Bloom never takes the foreground, so this runs only where it may: " +
            "on CI, or with BLOOM_E2E_DONT_DISTURB=0.",
    );
    // A book made, and a whole workspace closed and built again twice.
    test.setTimeout(400000);
    const firstCollection = bloomApp.collectionDir;
    const bookTitle = "Focus Book";

    await step("Make a book here, then visit a second collection", async () => {
        await makeBookFromTemplate(bloomApp.page, "Basic Book");
        await typeInGroup(bloomApp.page, ".bookTitle", "en", bookTitle);
        await switchTab(bloomApp.page, "collection");
        const secondCollection = makeAnotherCollection(bloomApp, {
            name: "switch-focus-second",
            languages: ["en"],
        });
        await openOtherCollection(bloomApp, secondCollection);
        // Sanity check: Bloom is active before the change, so the check after it means
        // that Bloom kept the foreground, not that it never had it.
        await expectBloomActive(
            bloomApp.page,
            bloomApp.bloomPid,
            "Sanity check: Bloom should be the active application before the user changes " +
                "collections",
            60000,
        );
    });

    await step(
        "Choose the first collection in the Other Collection dialog",
        async () => {
            await chooseOtherCollection(bloomApp, firstCollection);
        },
    );

    await step("See that Bloom is the active application", async () => {
        const page = bloomApp.page;
        await expectBloomActive(
            page,
            bloomApp.bloomPid,
            "Bloom's new window should have the foreground and the keyboard after the change " +
                "of collection",
        );
        // From here on, any moment the page loses the keyboard is recorded, so that a window
        // that is active for an instant and then loses it again (as 5.2 did at startup) fails.
        await page.evaluate(() => {
            (window as unknown as { e2eBlurs: number }).e2eBlurs = 0;
            window.addEventListener("blur", () => {
                (window as unknown as { e2eBlurs: number }).e2eBlurs++;
            });
        });
        await expectBookInPreview(page, bookTitle);
        const blurs = await page.evaluate(
            () => (window as unknown as { e2eBlurs: number }).e2eBlurs,
        );
        expect(
            blurs,
            "Bloom's window lost the keyboard while the Collections tab finished showing the " +
                "collection",
        ).toBe(0);
        expect(
            await readBloomFocus(page, bloomApp.bloomPid),
            "Bloom should still be the active application once the collection is shown",
        ).toEqual(kBloomActive);
    });
});
