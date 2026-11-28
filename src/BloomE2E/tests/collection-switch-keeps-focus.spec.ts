// When the user changes from one collection to another, Bloom must stay the active application
// (Test Case ID 103, from BL-11092 and BL-16784). Bloom closes its whole window and opens a new one
// on the other collection; when the new window comes up without the foreground, its title bar is
// grey, its tooltips don't show and the user's first click only activates it.
//
// The Other Collection dialog offers the collections Bloom opened recently, so the test first
// visits a second collection. The collection it then chooses in the dialog is the one it started
// in, which holds a book, so that the test can wait for the Collections tab to finish showing it
// (the book's preview is the last thing the tab draws).
//
// Who has the foreground and the keyboard is read from Windows every 50 ms from the click on the
// collection's card until then (helpers/windowFocus.ts says why not from the page). Once Bloom's
// new window is active it must stay active to the end, so a window that is active for an instant
// and then loses the foreground or the keyboard fails, even if it gets them back.
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
    describeFocus,
    expectBloomActive,
    type IFocusSample,
    isBloomActive,
    startFocusRecording,
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
            bloomApp.bloomPid,
            "Sanity check: Bloom should be the active application before the user changes " +
                "collections",
            60000,
        );
    });

    let samples: IFocusSample[] = [];
    await step(
        "Choose the first collection in the Other Collection dialog, and wait for it to show",
        async () => {
            const recording = startFocusRecording();
            try {
                await chooseOtherCollection(bloomApp, firstCollection);
                await expectBookInPreview(bloomApp.page, bookTitle);
            } finally {
                samples = await recording.stop();
            }
        },
    );

    await step(
        "See that Bloom became and stayed the active application",
        async () => {
            const bloomPid = bloomApp.bloomPid;
            const history = samples
                .map((s) => describeFocus(s, bloomPid))
                .join("\n");
            console.log(
                `Foreground and keyboard during the change:\n${history}`,
            );
            const last = samples[samples.length - 1];
            expect(
                isBloomActive(last, bloomPid),
                `Bloom should be the active application once the collection is shown.\n${history}`,
            ).toBe(true);
            const newWindow = last.foregroundWindow;
            const firstActive = samples.findIndex(
                (s) =>
                    s.foregroundWindow === newWindow &&
                    isBloomActive(s, bloomPid),
            );
            const lapses = samples
                .slice(firstActive)
                .filter(
                    (s) =>
                        s.foregroundWindow !== newWindow ||
                        !isBloomActive(s, bloomPid),
                );
            expect(
                lapses.map((s) => describeFocus(s, bloomPid)),
                `Once Bloom's new window was active it should have stayed active.\n${history}`,
            ).toEqual([]);
        },
    );
});
