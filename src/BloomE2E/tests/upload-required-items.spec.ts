// What a book must have before Bloom will upload it to BloomLibrary.org: a title, a copyright,
// the agreements, and a signed-in user. Automates the manual test "Required Items Before Upload"
// (Test Case ID 606).
//
// The manual test ends by actually uploading the book and looking at it on Blorg. That part is not
// automated: it needs a real Bloom Library account and would put a book on a real server. The
// login half of the sign-in step is automated against a pretended login state, because the real
// login lives in machine-wide settings shared with the developer's own Bloom — signing out for
// real would sign the developer out. See AUTOMATION-DEBT.md.
//
// The tests are serial: each one starts from the book the one before it left behind, which is how
// the manual test reads too.

import * as Path from "node:path";
import { fileURLToPath } from "node:url";
import { type Page } from "@playwright/test";
import { expect, test } from "../fixtures/bloomTest";
import {
    addPage,
    getContentPages,
    getPages,
    getShownPageId,
    goToPage,
    makeBookFromTemplate,
    typeInGroup,
} from "../helpers/bookMaking";
import { setCopyrightHolder } from "../helpers/copyrightAndLicense";
import { chooseImageFile } from "../helpers/images";
import {
    acceptAllAgreements,
    clickToFixMissingItem,
    expectAgreementsShowing,
    expectMissingRequirements,
    expectUploadStepButtons,
    openPublishToWeb,
    setPretendLoginState,
} from "../helpers/libraryPublish";
import { switchTab, waitForActiveTab } from "../helpers/workspace";

test.use({
    collectionSpec: { name: "upload-required-items", languages: ["en"] },
});

test.describe.configure({ mode: "serial" });

// The picture the picture-only book gets. Shipped with the suite, so the test needs nothing from
// outside this folder.
const IMAGE_FILE = Path.resolve(
    Path.dirname(fileURLToPath(import.meta.url)),
    "..",
    "fixtures",
    "images",
    "bird.png",
);

const TITLE = "A Book With A Title";
const COPYRIGHT_HOLDER = "Test Publisher";

/**
 * Make a book that has neither a title nor a copyright, which is what a book made from a template
 * starts as, and give it one content page of the kind asked for. Leaves the book selected and the
 * Edit tab showing it.
 */
async function makeBookWithNothingRequired(
    page: Page,
    kind: "text-less" | "picture-only",
): Promise<void> {
    await switchTab(page, "collection");
    await makeBookFromTemplate(page, "Basic Book");
    if (kind === "text-less") {
        // A "Just Text" page, with nothing typed in it: the book has no text anywhere.
        await addPage(page, "Just Text");
    } else {
        await addPage(page, "Just an Image");
        const [contentPage] = await getContentPages(page);
        await goToPage(page, contentPage.id);
        await chooseImageFile(page, IMAGE_FILE);
    }
}

test.describe("the items a book needs before it can be uploaded", () => {
    test("a text-less book with no title and no copyright is refused [Test Case ID 606]", async ({
        page,
    }) => {
        test.setTimeout(300000);
        await makeBookWithNothingRequired(page, "text-less");
        await openPublishToWeb(page);

        await expectMissingRequirements(
            page,
            ["Title", "Copyright"],
            "Publish: Web did not warn that a book with no title and no copyright is missing both.",
        );
        await expectAgreementsShowing(
            page,
            false,
            "The Agreements appeared even though the book was missing its title and copyright.",
        );
        await expectUploadStepButtons(
            page,
            { signIn: "absent", uploadBook: "absent", signOut: "absent" },
            "The Upload step offered a button even though the book was missing its title and copyright.",
        );
    });

    test("a picture-only book with no title and no copyright is refused too [Test Case ID 606]", async ({
        page,
    }) => {
        test.setTimeout(300000);
        await makeBookWithNothingRequired(page, "picture-only");
        await openPublishToWeb(page);

        await expectMissingRequirements(
            page,
            ["Title", "Copyright"],
            "Publish: Web did not warn that a picture-only book with no title and no copyright is missing both.",
        );
        await expectAgreementsShowing(
            page,
            false,
            "The Agreements appeared even though the picture-only book was missing its title and copyright.",
        );
    });

    test("Click to fix on the copyright takes that warning away [Test Case ID 606]", async ({
        page,
    }) => {
        // THE ACTION UNDER TEST: the "Click to fix" link, and the dialog it opens.
        await clickToFixMissingItem(page, "Copyright");
        await setCopyrightHolder(page, COPYRIGHT_HOLDER);

        await expectMissingRequirements(
            page,
            ["Title"],
            "The Missing Copyright warning did not go away after a copyright was added.",
        );
        await expectAgreementsShowing(
            page,
            false,
            "The Agreements appeared while the book still had no title.",
        );
    });

    test("Click to fix on the title opens the front cover, and the warning stays until a title is typed [Test Case ID 606]", async ({
        page,
    }) => {
        test.setTimeout(300000);
        // THE ACTION UNDER TEST: the "Click to fix" link of the Missing Title warning.
        await clickToFixMissingItem(page, "Title");

        await waitForActiveTab(page, "edit");
        const frontCover = (await getPages(page))[0];
        expect(frontCover.caption).toBe("Front Cover");
        await expect
            .poll(async () => getShownPageId(page), {
                timeout: 60000,
                message:
                    "Click to fix on the title did not open the book at its front cover.",
            })
            .toBe(frontCover.id);

        // Go back without typing a title: Bloom should still be asking for one.
        await openPublishToWeb(page);
        await expectMissingRequirements(
            page,
            ["Title"],
            "The Missing Title warning went away even though no title was typed.",
        );
    });

    test("adding a title clears the last warning and brings up the Agreements [Test Case ID 606]", async ({
        page,
    }) => {
        test.setTimeout(300000);
        await switchTab(page, "edit");
        const frontCover = (await getPages(page))[0];
        await goToPage(page, frontCover.id);
        await typeInGroup(page, ".bookTitle", "en", TITLE);
        // Bloom writes a page only when the book leaves it, so move off the cover to save it.
        const [contentPage] = await getContentPages(page);
        await goToPage(page, contentPage.id);

        await openPublishToWeb(page);
        await expectMissingRequirements(
            page,
            [],
            "Publish: Web was still asking for something after the book had both a title and a copyright.",
        );
        await expectAgreementsShowing(
            page,
            true,
            "The Agreements did not appear once the book had a title and a copyright.",
        );
    });

    test("uploading is offered only to a signed-in user [Test Case ID 606]", async ({
        page,
    }) => {
        // Nobody signed in. Until the agreements are ticked the Upload step is not even open, so
        // there is nothing to upload with and nothing to sign in with.
        await setPretendLoginState(page, undefined);
        await expectUploadStepButtons(
            page,
            { signIn: "absent", uploadBook: "absent", signOut: "absent" },
            "The Upload step offered a button while the agreements were still unticked.",
        );

        // THE ACTION UNDER TEST for this step: ticking the three agreements, which opens the
        // Upload step — and it asks the user to sign in rather than offering to upload.
        await acceptAllAgreements(page);
        await expectUploadStepButtons(
            page,
            { signIn: "enabled", uploadBook: "absent", signOut: "absent" },
            "With the agreements ticked and nobody signed in, the Upload step should offer Sign in and still no way to upload.",
        );

        // Signed in, everything else being ready: now, and only now, the book can be uploaded.
        await setPretendLoginState(page, "e2e-tester@example.com");
        await expectUploadStepButtons(
            page,
            { signIn: "absent", uploadBook: "enabled", signOut: "enabled" },
            "A signed-in user with the agreements ticked was not offered the Upload Book button.",
        );

        // Leave the pretended login as we found it, so nothing later in this Bloom sees it.
        await setPretendLoginState(page, undefined);
    });
});
