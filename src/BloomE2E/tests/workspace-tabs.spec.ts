// The smoke test for the whole e2e harness: launch Bloom, click each top-bar tab, and confirm
// Bloom itself says that tab is now active. If this fails, nothing else in this package can be
// trusted.
//
// Ported from src/BloomBrowserUI/react_components/TopBar/component-tests/bloom-exe-tabs.uitest.ts,
// which required a developer to have Bloom already running and pointed at whatever collection they
// happened to have open. This version launches its own Bloom on a known collection.
//
// This test has no "[Test Case ID N]" tag because no row in the Notion test inventory covers tab
// switching yet. Add one (and put its id in the title) if this behavior becomes a tracked case.
// See README.md for the convention.

import * as Path from "node:path";
import { expect, test } from "../fixtures/bloomTest";
import { selectBook } from "../helpers/collection";
import { getTabs, switchTab } from "../helpers/workspace";

test.use({ collectionName: "basic" });

test("switching workspace tabs through the real top bar", async ({
    page,
    bloomApp,
}) => {
    // Sanity check the start state, so a failure below means a click failed rather than that we
    // were already on the tab we were about to click. This reads the state once rather than waiting
    // for it: a Bloom that is not on the collection tab here has been left that way by an earlier
    // test sharing this worker's Bloom, and waiting would only turn that into a slow failure.
    expect((await getTabs(page)).tabStates.collection).toBe("active");

    // Setup, not the behavior under test: Bloom hides the Edit and Publish tabs until a book is
    // selected, so there would be nothing to click.
    await selectBook(page, Path.join(bloomApp.collectionDir, "A5 Portrait"));

    await switchTab(page, "publish");
    expect((await getTabs(page)).tabStates.collection).not.toBe("active");

    await switchTab(page, "edit");
    expect((await getTabs(page)).tabStates.publish).not.toBe("active");

    await switchTab(page, "collection");
});
