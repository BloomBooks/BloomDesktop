// A book's license, chosen in the Copyright and License dialog, shows on the credits page with the
// right Creative Commons icon and license version. Automates the manual test "Copyright Licensing
// CC Icons" (Test Case ID 86), which asks the tester to try the copyright and licensing options,
// including the steps of BL-11032 (after a book was given CC0, or any other license that is not
// Creative Commons, choosing Creative Commons gave version 1.0 and CC BY-NC-SA instead of CC BY 4.0).
//
// The credits page shows the license as an image, license.png in the book's folder, which Bloom
// writes from libpalaso's picture of the license whenever the license changes
// (BookCopyrightAndLicense.UpdateBookLicenseIcon), and as the license's URL, which names its version.
// "The correct icon" is checked by comparing, pixel for pixel, the image the credits page shows with
// the one Bloom serves for the license's token (copyrightAndLicense/ccImage, which the dialog's own
// badge uses), so a stale or wrong license.png fails the test.
//
// Each test makes a book of its own, so any one of them can run alone.

import { expect, test, type IBloomApp } from "../fixtures/bloomTest";
import type { Locator, Page } from "@playwright/test";
import { editablePageFrame, makeBookFromTemplate } from "../helpers/bookMaking";
import { copyrightAndLicenseDialog } from "../helpers/copyrightAndLicense";
import {
    goToCreditsPage,
    openCopyrightDialogFromCreditsPage,
} from "../helpers/creditsPage";
import {
    getCurrentUiLanguageTag,
    setUiStateViaApi,
} from "../helpers/uiLanguage";

test.use({
    collectionSpec: { name: "book-license-cc-icons", languages: ["en"] },
});

// A copyright holder unusual enough that finding it on the page cannot be a coincidence. OK stays
// disabled until the book has one.
const HOLDER = "Wren Valley Readers";

// The License tab's words for a book, as LicensePanel.tsx has them.
const kPublicDomain = "Anyone can use it however they want.";
const kCreativeCommons = "Everyone is allowed to:";
const kAllRightsReserved =
    "No one can modify or copy it without explicit permission. We will not be sharing it via Bloom Reader or BloomLibrary.org.";
const kOther = "Other";
const kCommercial = "use the book in a commercial way";
const kNewVersions =
    "make new versions of this book, but they must keep the author, illustrator, and other credits";
const kDifferentLicense =
    "apply a different license to new versions of this book";
const kCustomLicenseText = "Free for use in the schools of Wren Valley";

/** A license choice: how to make it on the License tab, and what it should give the book. */
interface ILicenseChoice {
    name: string;
    choose: (page: Page) => Promise<void>;
    /** The radio button that is chosen afterwards. */
    radio: string;
    /** The token of the dialog's Creative Commons badge, and of the credits page's icon; "" for none. */
    token: string;
    /** What the credits page's license URL must be, or undefined when it must be empty. */
    url?: RegExp;
    /**
     * The custom license's text, which the credits page shows under the license; undefined when
     * the page must not show it.
     */
    notes?: string;
}

function dialog(page: Page): Locator {
    return copyrightAndLicenseDialog(page);
}

function radio(page: Page, name: string): Locator {
    return dialog(page).getByRole("radio", { name });
}

function checkbox(page: Page, name: string): Locator {
    return dialog(page).getByRole("checkbox", { name });
}

/** The dialog's license version chooser, under the badge: "4.0" or "Intergov 3.0". */
function versionChooser(page: Page): Locator {
    return dialog(page).getByRole("combobox");
}

async function setCheckbox(
    page: Page,
    name: string,
    checked: boolean,
): Promise<void> {
    await checkbox(page, name).setChecked(checked);
    await expect(checkbox(page, name)).toBeChecked({ checked });
}

/** Choose Creative Commons with the three permissions given, at version 4.0. */
function creativeCommons(
    commercial: boolean,
    newVersions: boolean,
    differentLicense: boolean,
) {
    return async (page: Page) => {
        await radio(page, kCreativeCommons).check();
        await setCheckbox(page, kCommercial, commercial);
        await setCheckbox(page, kNewVersions, newVersions);
        if (newVersions)
            await setCheckbox(page, kDifferentLicense, differentLicense);
        await chooseVersion(page, "4.0");
    };
}

async function chooseVersion(
    page: Page,
    version: "4.0" | "Intergov 3.0",
): Promise<void> {
    const chooser = versionChooser(page);
    if ((await chooser.innerText()).trim() === version) return;
    await chooser.click();
    await page.getByRole("option", { name: version, exact: true }).click();
    await expect(chooser).toHaveText(version);
}

const cc4 = (type: string) =>
    new RegExp(`^https?://creativecommons\\.org/licenses/${type}/4\\.0/$`);

const kLicenseChoices: ILicenseChoice[] = [
    {
        name: "CC0 (anyone can use it however they want)",
        radio: kPublicDomain,
        choose: async (page) => {
            await radio(page, kPublicDomain).check();
        },
        token: "cc0",
        url: /^https?:\/\/creativecommons\.org\/publicdomain\/zero\/1\.0\/$/,
    },
    {
        name: "CC BY",
        radio: kCreativeCommons,
        choose: creativeCommons(true, true, true),
        token: "cc-by",
        url: cc4("by"),
    },
    {
        name: "CC BY-SA",
        radio: kCreativeCommons,
        choose: creativeCommons(true, true, false),
        token: "cc-by-sa",
        url: cc4("by-sa"),
    },
    {
        name: "CC BY-ND",
        radio: kCreativeCommons,
        choose: creativeCommons(true, false, false),
        token: "cc-by-nd",
        url: cc4("by-nd"),
    },
    {
        name: "CC BY-NC",
        radio: kCreativeCommons,
        choose: creativeCommons(false, true, true),
        token: "cc-by-nc",
        url: cc4("by-nc"),
    },
    {
        name: "CC BY-NC-SA",
        radio: kCreativeCommons,
        choose: creativeCommons(false, true, false),
        token: "cc-by-nc-sa",
        url: cc4("by-nc-sa"),
    },
    {
        name: "CC BY-NC-ND",
        radio: kCreativeCommons,
        choose: creativeCommons(false, false, false),
        token: "cc-by-nc-nd",
        url: cc4("by-nc-nd"),
    },
    {
        name: "CC BY, intergovernmental version 3.0",
        radio: kCreativeCommons,
        choose: async (page) => {
            await creativeCommons(true, true, true)(page);
            await chooseVersion(page, "Intergov 3.0");
        },
        token: "cc-by",
        url: /^https?:\/\/creativecommons\.org\/licenses\/by\/3\.0\/igo\/$/,
    },
    {
        name: "All rights reserved",
        radio: kAllRightsReserved,
        choose: async (page) => {
            await radio(page, kAllRightsReserved).check();
        },
        token: "",
    },
    {
        name: "a custom license",
        radio: kOther,
        choose: async (page) => {
            await radio(page, kOther).check();
            const text = dialog(page)
                .locator("div")
                .filter({ hasText: /^Avoid writing a custom license/ })
                .filter({ has: page.getByRole("textbox") })
                .first()
                .getByRole("textbox");
            await text.fill(kCustomLicenseText);
            await expect(text).toHaveValue(kCustomLicenseText);
        },
        token: "",
        notes: kCustomLicenseText,
    },
];

/** The token of the Creative Commons badge the dialog shows, e.g. "cc-by-sa", or "" for none. */
async function getBadgeToken(page: Page): Promise<string> {
    const badge = dialog(page).locator('img[src*="ccImage?token="]');
    if ((await badge.count()) === 0) return "";
    const src = (await badge.first().getAttribute("src")) ?? "";
    return new URL(src, "http://localhost").searchParams.get("token") ?? "";
}

async function showLicenseTab(page: Page): Promise<void> {
    await dialog(page)
        .getByRole("tab", { name: /license/i })
        .click();
    await expect(radio(page, kCreativeCommons)).toBeVisible();
}

/**
 * Open the dialog from the credits page, make sure the book has a copyright holder, show the
 * License tab, and leave the dialog open.
 */
async function openLicenseTab(page: Page): Promise<void> {
    await openCopyrightDialogFromCreditsPage(page);
    const holder = dialog(page).getByLabel("Copyright Holder");
    await expect(holder).toBeEnabled({ timeout: 15000 });
    if ((await holder.inputValue()) !== HOLDER) {
        await holder.fill(HOLDER);
        await expect(holder).toHaveValue(HOLDER);
    }
    await showLicenseTab(page);
}

async function clickOk(page: Page): Promise<void> {
    const ok = dialog(page).getByRole("button", { name: "OK", exact: true });
    await expect(ok).toBeEnabled({ timeout: 15000 });
    await ok.click();
    await expect(dialog(page)).toHaveCount(0, { timeout: 30000 });
}

/** What the credits page shows of the book's license. */
interface ICreditsLicense {
    licenseUrl: string;
    /** The text under the license that a reader can see; "" when it is hidden or empty. */
    licenseNotes: string;
    /** The license image's src; "" when the page has no license image. */
    imageSrc: string;
    /** Whether the license image is on the page with a picture loaded in it. */
    imageShown: boolean;
    /**
     * Whether the image shown is, pixel for pixel, the one Bloom serves for `token`; undefined when
     * no image is shown or no token was given.
     */
    imageMatchesToken?: boolean;
    /** The image's src and size, for the failure message. */
    image: string;
    /** Why the credits page could not be read, when it could not. */
    error?: string;
}

/** Read the book's license off the credits page, comparing its image with the one for `token`. */
async function getCreditsLicense(
    page: Page,
    token: string,
): Promise<ICreditsLicense> {
    return editablePageFrame(page)
        .locator(".bloom-page .licenseAndCopyrightBlock")
        .first()
        .evaluate(async (block, token) => {
            const url = (
                (
                    block.querySelector(
                        '[data-derived="licenseUrl"]',
                    ) as HTMLElement | null
                )?.innerText ?? ""
            ).trim();
            // Only notes a reader can see count: innerText reads hidden text too.
            const notesElement = block.querySelector(
                '[data-derived="licenseNotes"]',
            ) as HTMLElement | null;
            const notes = notesElement?.checkVisibility()
                ? notesElement.innerText.trim()
                : "";
            const img = block.querySelector(
                'img[data-derived="licenseImage"]',
            ) as HTMLImageElement | null;
            const src = img?.getAttribute("src") ?? "";
            const shown =
                !!img &&
                !!src &&
                img.complete &&
                img.naturalWidth > 0 &&
                img.getBoundingClientRect().width > 0;
            const describe = img
                ? `src="${src}" ${img.naturalWidth}x${img.naturalHeight}`
                : "no image element";
            const read = {
                licenseUrl: url,
                licenseNotes: notes,
                imageSrc: src,
                imageShown: shown,
                image: describe,
            };
            if (!shown || !token) return read;

            const pixels = (
                source: CanvasImageSource,
                w: number,
                h: number,
            ) => {
                const canvas = document.createElement("canvas");
                canvas.width = w;
                canvas.height = h;
                const context = canvas.getContext("2d")!;
                context.drawImage(source, 0, 0);
                return context.getImageData(0, 0, w, h).data;
            };
            const response = await fetch(
                `/bloom/api/copyrightAndLicense/ccImage?token=${token}`,
                { cache: "no-store" },
            );
            const expected = await createImageBitmap(await response.blob());
            let matches =
                expected.width === img!.naturalWidth &&
                expected.height === img!.naturalHeight;
            if (matches) {
                const a = pixels(img!, expected.width, expected.height);
                const b = pixels(expected, expected.width, expected.height);
                for (let i = 0; i < a.length && matches; i++)
                    matches = a[i] === b[i];
            }
            return {
                ...read,
                imageMatchesToken: matches,
                image: `${describe}; the ${token} image is ${expected.width}x${expected.height}`,
            };
        }, token);
}

/**
 * Wait until the credits page shows `choice`: its license URL (or none), its icon (or no license
 * image at all) and the custom license's text (or not).
 * Saving the dialog makes Bloom rebuild the page, so for a moment the old one is still showing.
 */
async function expectCreditsShow(
    page: Page,
    choice: ILicenseChoice,
): Promise<void> {
    let last: ICreditsLicense | undefined;
    const matches = () =>
        !!last &&
        !last.error &&
        (choice.url
            ? choice.url.test(last.licenseUrl)
            : last.licenseUrl === "") &&
        (choice.token
            ? last.imageShown && last.imageMatchesToken === true
            : last.imageSrc === "") &&
        (choice.notes
            ? last.licenseNotes.includes(choice.notes)
            : !last.licenseNotes.includes(kCustomLicenseText));
    try {
        await expect
            .poll(
                async () => {
                    last = await getCreditsLicense(page, choice.token).catch(
                        (error) =>
                            ({ error: String(error) }) as ICreditsLicense,
                    );
                    return matches();
                },
                { timeout: 30000 },
            )
            .toBe(true);
    } catch {
        throw new Error(
            `After choosing ${choice.name}, the credits page should show ` +
                (choice.url
                    ? `the license URL ${choice.url}`
                    : "no license URL") +
                (choice.token
                    ? ` and the ${choice.token} icon`
                    : " and no license image") +
                (choice.notes
                    ? `, with "${choice.notes}" under it`
                    : ", without the custom license's text") +
                `.\nLast seen: ${JSON.stringify(last)}`,
        );
    }
}

/** Choose `choice` on the open License tab, check the badge, click OK, and check the credits page. */
async function chooseAndCheck(
    page: Page,
    choice: ILicenseChoice,
): Promise<void> {
    await choice.choose(page);
    await expect
        .poll(() => getBadgeToken(page), {
            message: `The dialog's badge should be the one for ${choice.name}`,
        })
        .toBe(choice.token);
    await clickOk(page);
    await expectCreditsShow(page, choice);
}

/** Make a new Basic Book with Bloom's UI in English, and show its credits page. */
async function startBook(bloomApp: IBloomApp): Promise<Page> {
    // The dialog's choices are found by their English wording, and a freshly launched Bloom
    // takes its UI language from the operating system.
    if ((await getCurrentUiLanguageTag(bloomApp.page)) !== "en")
        await setUiStateViaApi(bloomApp, "en", false);
    const page = bloomApp.page;
    await makeBookFromTemplate(page, "Basic Book");
    await goToCreditsPage(page);
    return page;
}

test.describe("a book's license on the credits page", () => {
    test("each license shows its own Creative Commons icon and version on the credits page [Test Case ID 86]", async ({
        bloomApp,
    }) => {
        test.setTimeout(600000);
        const page = await startBook(bloomApp);

        for (const choice of kLicenseChoices) {
            await test.step(choice.name, async () => {
                await openLicenseTab(page);
                await chooseAndCheck(page, choice);
            });
        }
    });

    // BL-11032: after a license that is not Creative Commons, choosing Creative Commons gave
    // version 1.0, and CC BY-NC-SA rather than the CC BY a new choice starts with.
    for (const from of [
        "CC0 (anyone can use it however they want)",
        "All rights reserved",
        "a custom license",
    ]) {
        test(`choosing Creative Commons after ${from} gives CC BY 4.0 (BL-11032) [Test Case ID 86]`, async ({
            bloomApp,
        }) => {
            test.setTimeout(300000);
            const page = await startBook(bloomApp);
            const before = kLicenseChoices.find((c) => c.name === from)!;
            const ccBy = kLicenseChoices.find((c) => c.name === "CC BY")!;
            await openLicenseTab(page);
            await chooseAndCheck(page, before);

            await openLicenseTab(page);
            await expect(
                radio(page, before.radio),
                `The dialog should open on ${from}`,
            ).toBeChecked();
            // THE ACTION UNDER TEST: choose Creative Commons and change nothing else.
            await radio(page, kCreativeCommons).check();
            for (const permission of [
                kCommercial,
                kNewVersions,
                kDifferentLicense,
            ])
                await expect(
                    checkbox(page, permission),
                    `Creative Commons chosen after ${from} should start with "${permission}" ticked, as CC BY does`,
                ).toBeChecked();
            await expect(
                versionChooser(page),
                `Creative Commons chosen after ${from} should start at version 4.0`,
            ).toHaveText("4.0");
            await expect
                .poll(() => getBadgeToken(page), {
                    message: `The dialog's badge should be CC BY after choosing Creative Commons after ${from}`,
                })
                .toBe("cc-by");
            await clickOk(page);
            await expectCreditsShow(page, ccBy);
        });
    }
});
