// The warning on Book and Page Settings > Theme & Layout that Edge to Edge needs full bleed to
// reach the edge of a printed page (BL-15958).
// Automates Test Case ID 847 in the Notion test inventory.
//
// Edge to Edge takes the margins off content pages so pictures can run to the edge of the page.
// On paper that only works when the book also has "Use full bleed page layout" on, because
// otherwise the printer has nothing to trim off and a white edge is left. So when the theme is
// Edge to Edge, the page size is a paper size, and full bleed is off, the Theme & Layout page
// warns, and its "Print Publishing" link goes to the page where full bleed is turned on.
//
// Full bleed needs an Enterprise subscription, so the collection has one. The book's theme and page
// size are set up through the API; the dialog itself is driven through its real controls.

import { expect, test } from "../fixtures/bloomTest";
import { addPage, makeBookFromTemplate } from "../helpers/bookMaking";
import {
    cancelBookSettings,
    expectEdgeToEdgeFullBleedWarning,
    followEdgeToEdgeFullBleedWarningLink,
    getBookAppearance,
    openBookSettings,
    setBookAppearance,
    setFullBleedInBookSettings,
    showBookSettingsPage,
} from "../helpers/bookSettings";
import { kEnterpriseSubscriptionCode } from "../helpers/collectionSettings";
import { getPageSize, setPageSize } from "../helpers/pageSize";

test.use({
    collectionSpec: {
        name: "edge-to-edge-warning",
        languages: ["en"],
        subscriptionCode: kEnterpriseSubscriptionCode,
    },
});

test.describe.configure({ mode: "serial" });

test.describe("Edge to Edge full bleed warning", () => {
    test("builds an A5 book in Edge to Edge without full bleed", async ({
        page,
    }) => {
        test.setTimeout(300000);
        await makeBookFromTemplate(page, "Basic Book");
        await addPage(page, "Basic Text & Image");
        expect(await getPageSize(page)).toBe("A5Portrait");
        await setBookAppearance(page, {
            cssThemeName: "edge-to-edge",
            fullBleed: false,
        });
    });

    test("warns, links to Print Publishing, and stops warning once full bleed is ticked [Test Case ID 847]", async ({
        page,
    }) => {
        await openBookSettings(page, "Theme & Layout");
        await expectEdgeToEdgeFullBleedWarning(page, true);

        // THE ACTIONS UNDER TEST: follow the warning's link, and tick full bleed there.
        await followEdgeToEdgeFullBleedWarningLink(page);
        await setFullBleedInBookSettings(page, true);
        await showBookSettingsPage(page, "Theme & Layout");
        await expectEdgeToEdgeFullBleedWarning(page, false);

        // Unticking it brings the warning back, before anything is saved.
        await showBookSettingsPage(page, "Print Publishing");
        await setFullBleedInBookSettings(page, false);
        await showBookSettingsPage(page, "Theme & Layout");
        await expectEdgeToEdgeFullBleedWarning(page, true);

        await cancelBookSettings(page);
        expect((await getBookAppearance(page)).fullBleed).toBe(false);
    });

    test("does not warn about the Default theme [Test Case ID 847]", async ({
        page,
    }) => {
        await setBookAppearance(page, { cssThemeName: "default" });
        await openBookSettings(page, "Theme & Layout");
        await expectEdgeToEdgeFullBleedWarning(page, false);
        await cancelBookSettings(page);
    });

    test("does not warn at a device size, which has no bleed [Test Case ID 847]", async ({
        page,
    }) => {
        await setPageSize(page, "Device16x9Portrait");
        await setBookAppearance(page, { cssThemeName: "edge-to-edge" });
        await openBookSettings(page, "Theme & Layout");
        await expectEdgeToEdgeFullBleedWarning(page, false);
        await cancelBookSettings(page);
    });
});
