// The Copyright and License dialog for a picture in the Edit tab: the three ways a person opens it,
// whether OK keeps what they chose and Cancel or Esc does not, the license choices with the badge
// and "About" link that name them, the Shortcuts list of the credits already in the book, and
// "Add this info to all images in this book".
//
// A picture's credits are stored in the image file and copied onto the page as the data-copyright
// and data-license attributes of its <img> (ImageUpdater.cs), which C# writes after OK; the page
// is then reloaded. So "kept" is checked twice: on the page, and in the dialog opened again.

import * as Path from "node:path";
import { fileURLToPath } from "node:url";
import type { Locator, Page } from "@playwright/test";
import { expect, test } from "../fixtures/bloomTest";
import {
    addPage,
    editablePageFrame,
    getContentPages,
    goToPage,
    makeBookFromTemplate,
} from "../helpers/bookMaking";
import { clickCanvasElementMenuItem } from "../helpers/canvasElements";
import {
    copyrightAndLicenseDialog,
    waitForCopyrightDialog,
} from "../helpers/copyrightAndLicense";
import { chooseImageFile, selectImage } from "../helpers/images";
import { realClick } from "../helpers/realClick";

test.use({
    collectionSpec: { name: "image-copyright-and-license", languages: ["en"] },
});

const fixtureImage = (name: string) =>
    Path.resolve(
        Path.dirname(fileURLToPath(import.meta.url)),
        "..",
        "fixtures",
        "images",
        name,
    );

// Copyright holders unusual enough that finding one on the page cannot be a coincidence.
const KINGFISHER = "Kingfisher Pictures";
const HERON = "Heron Press";
const TYPED_THEN_DROPPED = "Never Saved Studio";

/** The localization id of "Set image information..." on a picture's "..." menu. */
const kSetImageInformation = "EditTab.Image.EditMetadataOverlay";

/** The picture on the page being edited. */
function picture(page: Page): Locator {
    return editablePageFrame(page)
        .locator(".bloom-backgroundImage .bloom-imageContainer img")
        .first();
}

/** The credits Bloom has written onto the picture on the page being edited. */
async function getPictureCredits(
    page: Page,
): Promise<{ copyright: string; license: string }> {
    return picture(page).evaluate((img) => ({
        copyright: img.getAttribute("data-copyright") ?? "",
        license: img.getAttribute("data-license") ?? "",
    }));
}

/** Wait until the picture's copyright on the page contains `holder`. */
async function expectPictureCopyright(
    page: Page,
    holder: string,
    message: string,
): Promise<void> {
    await expect
        .poll(async () => (await getPictureCredits(page)).copyright, {
            timeout: 30000,
            message,
        })
        .toContain(holder);
}

function holderField(page: Page): Locator {
    return copyrightAndLicenseDialog(page).getByLabel("Copyright Holder");
}

/** Type `holder` as the copyright holder in the open dialog, replacing what is there. */
async function typeHolder(page: Page, holder: string): Promise<void> {
    await waitForCopyrightDialog(page);
    await holderField(page).click();
    await holderField(page).fill(holder);
    await expect(holderField(page)).toHaveValue(holder);
}

/** Click OK in the open dialog and wait until it has closed. */
async function pressOk(page: Page): Promise<void> {
    const ok = copyrightAndLicenseDialog(page).getByRole("button", {
        name: "OK",
        exact: true,
    });
    await expect(ok, "OK should be enabled").toBeEnabled({ timeout: 15000 });
    await ok.click();
    await expect(copyrightAndLicenseDialog(page)).toHaveCount(0, {
        timeout: 30000,
    });
}

/** Click Cancel in the open dialog and wait until it has closed. */
async function pressCancel(page: Page): Promise<void> {
    await copyrightAndLicenseDialog(page)
        .getByRole("button", { name: "Cancel", exact: true })
        .click();
    await expect(copyrightAndLicenseDialog(page)).toHaveCount(0, {
        timeout: 30000,
    });
}

/** Open the dialog for the picture the way a person does from its "..." menu. */
async function openFromMenu(page: Page): Promise<void> {
    await selectImage(page);
    await clickCanvasElementMenuItem(page, kSetImageInformation);
    await waitForCopyrightDialog(page);
}

/** Show the dialog's License tab. */
async function showLicenseTab(page: Page): Promise<void> {
    await copyrightAndLicenseDialog(page)
        .getByRole("tab", { name: /license/i })
        .click();
}

/** The token of the Creative Commons badge the dialog shows, e.g. "cc-by-sa", or "" for none. */
async function getBadgeToken(page: Page): Promise<string> {
    const badge = copyrightAndLicenseDialog(page).locator(
        'img[src*="ccImage?token="]',
    );
    if ((await badge.count()) === 0) return "";
    const src = (await badge.first().getAttribute("src")) ?? "";
    return new URL(src, "http://localhost").searchParams.get("token") ?? "";
}

/** The "About ..." link under the badge, e.g. "About CC-BY-SA". */
function aboutLink(page: Page): Locator {
    return copyrightAndLicenseDialog(page).getByText(/^About /);
}

function radio(page: Page, name: string): Locator {
    return copyrightAndLicenseDialog(page).getByRole("radio", { name });
}

function checkbox(page: Page, name: string): Locator {
    return copyrightAndLicenseDialog(page).getByRole("checkbox", { name });
}

const kPublicDomain = "Anyone can use it however they want.";
const kCreativeCommons = "Everyone is allowed to:";
const kAllRightsReserved =
    "No one can modify or copy it without explicit permission. We will not be sharing it via Bloom Reader or BloomLibrary.org.";
const kOther = "Other";
const kCommercial = "use the image in a commercial way";
const kNewVersions =
    "make new versions of this image, but they must keep the illustrator and other credits";
const kDifferentLicense =
    "apply a different license to new versions of this image";

/** A license choice: how to make it on the License tab, and how the dialog names it. */
interface ILicenseChoice {
    name: string;
    choose: (page: Page) => Promise<void>;
    /** The radio button that is chosen afterwards. */
    radio: string;
    /** The badge's token, or "" when the license has no Creative Commons badge. */
    token: string;
}

/** Tick or untick a Creative Commons check box, and wait until it shows that. */
async function setCheckbox(
    page: Page,
    name: string,
    checked: boolean,
): Promise<void> {
    await checkbox(page, name).setChecked(checked);
    await expect(checkbox(page, name)).toBeChecked({ checked });
}

const kLicenseChoices: ILicenseChoice[] = [
    {
        name: "Public Domain",
        radio: kPublicDomain,
        token: "cc0",
        choose: async (page) => {
            await radio(page, kPublicDomain).check();
        },
    },
    {
        name: "CC-BY",
        radio: kCreativeCommons,
        token: "cc-by",
        choose: async (page) => {
            await radio(page, kCreativeCommons).check();
            await setCheckbox(page, kCommercial, true);
            await setCheckbox(page, kNewVersions, true);
            await setCheckbox(page, kDifferentLicense, true);
        },
    },
    {
        name: "CC-BY-NC-SA",
        radio: kCreativeCommons,
        token: "cc-by-nc-sa",
        choose: async (page) => {
            await radio(page, kCreativeCommons).check();
            await setCheckbox(page, kCommercial, false);
            await setCheckbox(page, kNewVersions, true);
            await setCheckbox(page, kDifferentLicense, false);
        },
    },
    {
        name: "CC-BY-ND",
        radio: kCreativeCommons,
        token: "cc-by-nd",
        choose: async (page) => {
            await radio(page, kCreativeCommons).check();
            await setCheckbox(page, kCommercial, true);
            await setCheckbox(page, kNewVersions, false);
        },
    },
    {
        name: "All rights reserved",
        radio: kAllRightsReserved,
        token: "",
        choose: async (page) => {
            await radio(page, kAllRightsReserved).check();
        },
    },
    {
        name: "a custom license",
        radio: kOther,
        token: "",
        choose: async (page) => {
            await radio(page, kOther).check();
            await customLicenseText(page).fill(kCustomLicense);
        },
    },
];

const kCustomLicense = "Free for use in schools";

/**
 * The box for a custom license's text. It has no label of its own: the advice above it is a
 * separate element, so it is found as the box in the part of the dialog that starts with that
 * advice.
 */
function customLicenseText(page: Page): Locator {
    const dialog = copyrightAndLicenseDialog(page);
    return dialog
        .locator("div")
        .filter({ hasText: /^Avoid writing a custom license/ })
        .filter({ has: page.getByRole("textbox") })
        .first()
        .getByRole("textbox");
}

test("image Copyright and License dialog: opening it, OK and Cancel, licenses, shortcuts and Add to all images [Test Case ID 284]", async ({
    page,
    step,
}) => {
    test.setTimeout(600000);

    await step(
        "Make a book with two pictures that have no credits",
        async () => {
            await makeBookFromTemplate(page, "Basic Book");
            await addPage(page, "Basic Text & Image", 2);
        },
    );
    const [first, second] = await getContentPages(page);
    await step("Give each picture page a picture", async () => {
        await goToPage(page, first.id);
        await chooseImageFile(page, fixtureImage("bird.png"));
        await goToPage(page, second.id);
        await chooseImageFile(page, fixtureImage("blue-square.png"));
    });

    await step(
        "Open the dialog from the warning on the picture, then Cancel",
        async () => {
            await goToPage(page, first.id);
            expect(
                (await getPictureCredits(page)).copyright,
                "Sanity check: the picture should start with no copyright",
            ).toBe("");
            await realClick(
                editablePageFrame(page).locator(
                    ".editMetadataButton.imgMetadataProblem",
                ),
            );
            await typeHolder(page, TYPED_THEN_DROPPED);
            await pressCancel(page);
        },
    );

    await step(
        "Open the dialog from the picture's toolbar, then press Esc",
        async () => {
            await selectImage(page);
            await editablePageFrame(page)
                .locator('[data-testid="toolbar-missingMetadata"]')
                .click();
            await typeHolder(page, TYPED_THEN_DROPPED);
            await page.keyboard.press("Escape");
            await expect(copyrightAndLicenseDialog(page)).toHaveCount(0, {
                timeout: 30000,
            });
        },
    );

    await step(
        "Open the dialog from the picture's menu, and see that nothing was kept",
        async () => {
            await openFromMenu(page);
            await expect(
                holderField(page),
                "Neither Cancel nor Esc should have kept the holder typed before it",
            ).toHaveValue("");
            expect(
                (await getPictureCredits(page)).copyright,
                "Neither Cancel nor Esc should have put the typed holder on the page",
            ).toBe("");
        },
    );

    await step("Type a holder and press OK", async () => {
        await typeHolder(page, KINGFISHER);
        await pressOk(page);
        await expectPictureCopyright(
            page,
            KINGFISHER,
            "OK should put the holder on the picture",
        );
    });

    await step("Open the dialog again and see the holder kept", async () => {
        // Going to another page and back reloads this one from the saved book.
        await goToPage(page, second.id);
        await goToPage(page, first.id);
        await expectPictureCopyright(
            page,
            KINGFISHER,
            "The holder should still be on the picture after the page is reloaded",
        );
        await openFromMenu(page);
        await expect(holderField(page)).toHaveValue(KINGFISHER);
        await pressCancel(page);
    });

    for (const choice of kLicenseChoices) {
        await step(
            `Choose ${choice.name}, press OK, and see it kept`,
            async () => {
                await openFromMenu(page);
                await showLicenseTab(page);
                const licenseBefore = (await getPictureCredits(page)).license;
                await choice.choose(page);
                await expect(radio(page, choice.radio)).toBeChecked();
                expect(
                    await getBadgeToken(page),
                    `The badge should be the one for ${choice.name}`,
                ).toBe(choice.token);
                if (choice.token && choice.token !== "cc0") {
                    await expect(
                        aboutLink(page),
                        `The link under the badge should name ${choice.name}`,
                    ).toHaveText(`About ${choice.token.toUpperCase()}`);
                }
                await pressOk(page);
                await expect
                    .poll(async () => (await getPictureCredits(page)).license, {
                        timeout: 30000,
                        message: `OK should change the picture's license to ${choice.name}`,
                    })
                    .not.toBe(licenseBefore);

                await openFromMenu(page);
                await showLicenseTab(page);
                await expect(
                    radio(page, choice.radio),
                    `${choice.name} should still be chosen when the dialog is opened again`,
                ).toBeChecked();
                expect(
                    await getBadgeToken(page),
                    `The badge should still be the one for ${choice.name}`,
                ).toBe(choice.token);
                if (choice.radio === kOther) {
                    await expect(
                        customLicenseText(page),
                        "The custom license's text should be kept",
                    ).toHaveValue(kCustomLicense);
                }
                await pressCancel(page);
            },
        );
    }

    const shortcut = (holder: string) =>
        copyrightAndLicenseDialog(page).getByText(
            new RegExp(`© \\d+ ${holder}`),
        );

    await step("Give the second picture another holder", async () => {
        await goToPage(page, second.id);
        await openFromMenu(page);
        await typeHolder(page, HERON);
        await pressOk(page);
        await expectPictureCopyright(
            page,
            HERON,
            "OK should put the second holder on the second picture",
        );
    });

    await step(
        "See both holders in the Shortcuts list, and use one",
        async () => {
            await openFromMenu(page);
            await expect(
                shortcut(KINGFISHER),
                "The Shortcuts list should offer the first picture's credits",
            ).toBeVisible({ timeout: 30000 });
            await expect(
                shortcut(HERON),
                "The Shortcuts list should offer the second picture's credits",
            ).toBeVisible();
            await expect(
                shortcut(TYPED_THEN_DROPPED),
                "The Shortcuts list should not offer credits that were never kept",
            ).toHaveCount(0);

            await shortcut(KINGFISHER).click();
            await expect(
                holderField(page),
                "Choosing a shortcut should fill in its holder",
            ).toHaveValue(KINGFISHER);
            await shortcut(HERON).click();
            await expect(holderField(page)).toHaveValue(HERON);
        },
    );

    await step("Add this info to all images, then press OK", async () => {
        const dialog = copyrightAndLicenseDialog(page);
        await dialog
            .getByRole("button", {
                name: "Add this info to all images in this book",
            })
            .click();
        await expect(
            dialog.getByText("Done", { exact: true }),
            "Adding the info to all images should end showing Done",
        ).toBeVisible({ timeout: 60000 });
        await pressOk(page);
    });

    await step(
        "See the holder on both pictures, and only one shortcut",
        async () => {
            await goToPage(page, first.id);
            await expectPictureCopyright(
                page,
                HERON,
                "Add to all images should have put the holder on the first picture",
            );
            await goToPage(page, second.id);
            await expectPictureCopyright(
                page,
                HERON,
                "The second picture should keep its holder",
            );
            await openFromMenu(page);
            await expect(shortcut(HERON)).toBeVisible({ timeout: 30000 });
            await expect(
                shortcut(KINGFISHER),
                "Once every picture has the same credits, the Shortcuts list should not offer the old ones",
            ).toHaveCount(0);
            await pressCancel(page);
        },
    );
});
