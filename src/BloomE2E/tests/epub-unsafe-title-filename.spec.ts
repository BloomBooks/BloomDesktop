// A book whose title is full of characters that are unsafe in a file name still publishes as an
// ePUB: Bloom names the book's folder, and so the ePUB file it suggests in Save As, after the title
// with each unsafe character replaced by a space and no space or period left at either end
// (BookStorage.SanitizeNameForFileSystem; a trailing period broke ePUB making in BL-9040). The
// ePUB itself keeps the whole title, except "|": in any text box Bloom takes a typed "|" for a
// Talking Book phrase marker, which shows to nobody (talkingBookTool.tsx enshroudPhraseDelimiters).
//
// Automates the manual test "Unsafe Title ePUB Filename" (Test Case ID 394). The case's title is
// typed as the case gives it, "&amp;" included. As BL-9040 notes, the bug showed only with a cover
// picture, so the book has one.
//
// Not checked: opening the ePUB in a separate ePUB reader program (the case used epub+). The test
// checks instead that the saved file is a well-formed ePUB package that carries the title.

import * as fs from "node:fs";
import * as Path from "node:path";
import { fileURLToPath } from "node:url";
import { expect, test } from "../fixtures/bloomTest";
import { launchWithDontDisturb } from "../fixtures/launchBloom";
import {
    getPages,
    goToPage,
    makeBookFromTemplate,
    typeInGroup,
} from "../helpers/bookMaking";
import { readZip, type IArchiveEntry } from "../helpers/bulkBloomPub";
import { findBookFolderById, readBookId } from "../helpers/collection";
import { kEnterpriseSubscriptionCode } from "../helpers/collectionSettings";
import {
    getEpubPackageDublinCore,
    getEpubPackageSpineHrefs,
    getEpubXhtmlText,
} from "../helpers/epubPublish";
import { chooseImageFile, getImagePlacement } from "../helpers/images";
import { setUiStateViaApi } from "../helpers/uiLanguage";
import {
    answerSaveAsDialog,
    getForegroundWindow,
} from "../helpers/nativeDialogs";
import {
    closePublishProgressDialog,
    selectPublishDestination,
} from "../helpers/publish";

test.use({
    collectionSpec: {
        name: "epub-unsafe-title-filename",
        languages: ["en"],
        // Publishing an ePUB needs Pro.
        subscriptionCode: kEnterpriseSubscriptionCode,
    },
});

// The title as the case has it.
const kTitle = "Unsafe ~!@#$%^&amp;*()_-+={}[]|\\:;\"'<>.?/";
// The title as a reader sees it: the "|" typed is a phrase marker, not text.
const kShownTitle = kTitle.replace("|", "");

// Characters that Windows refuses in a file name, and those that Bloom keeps out of file names
// besides because they trouble Bloom Player, JSON and URLs.
const kWindowsInvalid = '<>:"/\\|?*';
const kBloomUnsafe = "&'{},;()$@";

const kPicture = Path.resolve(
    Path.dirname(fileURLToPath(import.meta.url)),
    "..",
    "fixtures",
    "images",
    "house-landscape.png",
);

/** `text` with every run of white space made one space. */
function collapseSpaces(text: string): string {
    return text.replace(/\s+/g, " ");
}

/**
 * The name a person would expect from `title`: each unsafe character a space, no space or period
 * at either end. Runs of spaces are made one, so that only which characters stay is compared.
 */
function expectedSafeName(title: string): string {
    let name = title;
    for (const c of kWindowsInvalid + kBloomUnsafe)
        name = name.split(c).join(" ");
    return collapseSpaces(name).replace(/^[\s.]+|[\s.]+$/g, "");
}

test("An ePUB of a book with an unsafe title gets a safe file name and keeps the title [Test Case ID 394]", async ({
    bloomApp,
    step,
}, testInfo) => {
    test.skip(
        launchWithDontDisturb(),
        "Save As takes the foreground, so this runs only where Bloom may take the foreground: on " +
            "CI, or with BLOOM_E2E_DONT_DISTURB=0.",
    );
    test.setTimeout(400000);

    // selectPublishDestination finds the ePUB tab by its English label.
    await setUiStateViaApi(bloomApp, "en", false);
    // Changing the UI language can replace the shell page.
    const page = bloomApp.page;
    const collectionDir = bloomApp.collectionDir;
    let bookId = "";
    let pictureFileName = "";

    await step(
        "Make a Basic Book with a cover picture and the unsafe title",
        async () => {
            await makeBookFromTemplate(page, "Basic Book");
            const bookFolders = fs
                .readdirSync(collectionDir, { withFileTypes: true })
                .filter(
                    (entry) =>
                        entry.isDirectory() &&
                        fs.existsSync(
                            Path.join(collectionDir, entry.name, "meta.json"),
                        ),
                );
            expect(
                bookFolders.map((entry) => entry.name),
                "The collection should hold the one new book.",
            ).toHaveLength(1);
            bookId = readBookId(Path.join(collectionDir, bookFolders[0].name));
            await goToPage(page, (await getPages(page))[0].id);
            await chooseImageFile(page, kPicture);
            pictureFileName = (await getImagePlacement(page)).fileName;
            await typeInGroup(page, ".bookTitle", "en", kTitle);
        },
    );

    let bookFolderName = "";
    await step("Open Publish > ePUB and wait for its preview", async () => {
        // Leaving the Edit tab saves the book, and Bloom renames its folder after the new title.
        await selectPublishDestination(page, "ePUB");
        await expect
            .poll(
                () => Path.basename(findBookFolderById(collectionDir, bookId)),
                {
                    timeout: 60000,
                    message:
                        "Bloom should have renamed the book's folder after its new title.",
                },
            )
            .toMatch(/^Unsafe/);
        bookFolderName = Path.basename(
            findBookFolderById(collectionDir, bookId),
        );
        await expect(
            page.getByTestId("PublishTab.Save"),
            "The ePUB screen's Save button should be enabled once the ePUB is made.",
        ).toBeEnabled({ timeout: 180000 });
    });

    // Under --e2e Bloom reports a problem with NonFatalProblem only on stderr; making the ePUB of
    // this book reported one in BL-9040.
    expect(
        bloomApp.takeNonFatalProblems(),
        "Bloom should have made the ePUB without reporting a problem.",
    ).toEqual([]);

    await step("The book's folder name is the title made safe", async () => {
        for (const c of kWindowsInvalid + kBloomUnsafe)
            expect(
                bookFolderName.includes(c),
                `The folder name "${bookFolderName}" should not hold "${c}".`,
            ).toBe(false);
        expect(
            bookFolderName,
            "The folder name should neither start nor end with a space or a period.",
        ).toMatch(/^[^\s.](.*[^\s.])?$/);
        expect(
            collapseSpaces(bookFolderName),
            "The folder name should keep every safe character of the title, in order.",
        ).toBe(expectedSafeName(kShownTitle));
    });

    const saveFolder = testInfo.outputPath("saved-epub");
    fs.mkdirSync(saveFolder, { recursive: true });
    const savedPath = Path.join(saveFolder, `${bookFolderName}.epub`);
    let entries: IArchiveEntry[] = [];

    await step(
        "Save the ePUB: Bloom suggests the folder's name, and saves it under that name",
        async () => {
            const foregroundBefore = await getForegroundWindow();
            const answered = page.waitForResponse(
                (response) =>
                    response.request().method() === "POST" &&
                    new URL(response.url()).pathname.endsWith(
                        "/publish/epub/save",
                    ),
                { timeout: 300000 },
            );
            await page.getByTestId("PublishTab.Save").click();
            const suggested = await answerSaveAsDialog(
                bloomApp.bloomPid,
                savedPath,
                60000,
                foregroundBefore,
            );
            // Windows leaves ".epub" out of the box where Explorer hides known extensions.
            expect(
                [bookFolderName, `${bookFolderName}.epub`],
                `Save As should suggest the book's folder name; it suggested "${suggested}".`,
            ).toContain(suggested);
            expect(
                (await answered).ok(),
                "Bloom should have finished saving the ePUB.",
            ).toBe(true);
            // Bloom answers the save request before Save As opens, and writes the file after it
            // closes. A zip's central directory is the last thing written, so the file is whole
            // once readZip can read it.
            await expect
                .poll(
                    () => {
                        try {
                            entries = readZip(fs.readFileSync(savedPath));
                            return true;
                        } catch {
                            return false;
                        }
                    },
                    {
                        timeout: 120000,
                        message: `Bloom should have saved a whole zip to ${savedPath}`,
                    },
                )
                .toBe(true);
            await closePublishProgressDialog(page, "ePUB");
        },
    );

    await step(
        "The saved ePUB is a whole ePUB package with the full title, its pages and the cover picture",
        async () => {
            expect(
                entries[0]?.name,
                "An ePUB's first file should be its mimetype.",
            ).toBe("mimetype");
            expect(entries[0].content.toString("utf8")).toBe(
                "application/epub+zip",
            );
            const container = entries.find(
                (entry) => entry.name === "META-INF/container.xml",
            );
            expect(
                container,
                "The ePUB should have META-INF/container.xml",
            ).toBeDefined();
            // Bloom quotes the attribute with apostrophes; XML allows either quote.
            const packagePath = /full-path=(["'])([^"']+)\1/.exec(
                container!.content.toString("utf8"),
            )?.[2];
            const packageEntry = entries.find(
                (entry) => entry.name === packagePath,
            );
            expect(
                packageEntry,
                `The ePUB should hold the package that container.xml names, ${packagePath}`,
            ).toBeDefined();
            expect(
                await getEpubPackageDublinCore(
                    page,
                    packageEntry!.content.toString("utf8"),
                    "title",
                ),
                "The ePUB's title should be the book's whole title, unsafe characters and all.",
            ).toEqual([kShownTitle]);
            const packageFolder = Path.posix.dirname(packagePath!);
            const inPackage = (href: string) =>
                packageFolder === "." ? href : `${packageFolder}/${href}`;

            // Every page in the reading order is in the file, and the first, the cover, shows
            // the title.
            const spine = await getEpubPackageSpineHrefs(
                page,
                packageEntry!.content.toString("utf8"),
            );
            expect(
                spine.length,
                "The ePUB's reading order should list its pages.",
            ).toBeGreaterThan(0);
            for (const href of spine)
                expect(
                    entries.some((entry) => entry.name === inPackage(href)),
                    `The ePUB should hold ${inPackage(href)}, a page of its reading order.`,
                ).toBe(true);
            const cover = entries.find(
                (entry) => entry.name === inPackage(spine[0]),
            )!;
            expect(
                await getEpubXhtmlText(page, cover.content.toString("utf8")),
                "The ePUB's first page should show the title.",
            ).toContain(kShownTitle);

            const pictureEntries = entries.filter(
                (entry) =>
                    Path.posix.basename(entry.name) === pictureFileName &&
                    entry.name.startsWith(
                        packageFolder === "." ? "" : `${packageFolder}/`,
                    ),
            );
            expect(
                pictureEntries.map((entry) => entry.content.length),
                `The ePUB should hold the cover picture ${pictureFileName}.`,
            ).toHaveLength(1);
            expect(pictureEntries[0].content.length).toBeGreaterThan(0);
        },
    );
});
