import { execFile } from "child_process";
import fetch from "node-fetch";
import chalk from "chalk";
import {
    Browser,
    BrowserContext,
    chromium,
    expect,
    Page,
} from "@playwright/test";
import { afterAll, beforeAll, describe, test } from "vitest";
import { argosScreenshot } from "@argos-ci/playwright";
import * as fs from "fs";
import { PNG } from "pngjs";
import Pixelmatch from "pixelmatch";
import * as Path from "path";

describe("All books", () => {
    let page: Page;
    let browser: Browser;
    let context: BrowserContext;

    beforeAll(async () => {
        await launchBloomIfNeeded();
        browser = await chromium.launch();
        context = await browser.newContext();
        page = await context.newPage();
    });
    afterAll(async () => {
        // Leave every book on the Default branding. Otherwise each book stays in whatever
        // branding was tested last, which leaves the fixture in a non-default state: an extra
        // xmatter page (with a fresh page id), a changed brandingProjectName in meta.json, and
        // branding-specific files copied into the book folder. Switching back to Default also
        // makes Bloom clean up those branding-specific support files.
        for (const bookFolder of bookFolders) {
            await selectBook(bookFolder);
            await setBranding("Default");
            await setTheme("default");
        }
        await browser.close();
    });

    // NB: currently, we don't have a way of making Bloom change collections, or re-running it with a different collection
    // Our test "collections/" directory currently only has the one collection, so this is ok for now.
    const collectionFolders = fs
        .readdirSync("collections")
        .filter((f) => fs.statSync(Path.join("collections", f)).isDirectory())
        .map((f) => Path.join(process.cwd(), "collections", f));

    const bookFolders = collectionFolders.flatMap((collectionPath) => {
        var paths = fs
            .readdirSync(collectionPath)
            .filter(
                (f) =>
                    fs.statSync(Path.join(collectionPath, f)).isDirectory() &&
                    !f.startsWith("Sample Texts"),
            )
            .map((f) => Path.join(collectionPath, f));
        return paths;
    });
    const brandings = ["Default", "Local-Community", "UEEP[Uzbek]"];
    // The appearance themes to test. These match the files in src/content/appearanceThemes/.
    const themes = [
        "default",
        "legacy-5-6",
        "rounded-border-ebook",
        "zero-margin-ebook",
    ];

    // We test each branding with the default theme, and each non-default theme with the default
    // branding. Testing every branding x theme combination would be many more reference images
    // for little extra coverage; this way each branding and each theme is exercised at least once.
    // "label" becomes the screenshot base name, so it must be unique per book and stable.
    const cases = bookFolders.flatMap((bookFolder) => {
        const bookName = Path.basename(bookFolder);
        const brandingCases = brandings.map((branding) => ({
            bookFolder,
            branding,
            theme: "default",
            label: `branding-${branding}`,
            title: `${bookName} branding:${branding}`,
        }));
        const themeCases = themes
            .filter((theme) => theme !== "default") // default theme is already covered by branding:Default
            .map((theme) => ({
                bookFolder,
                branding: "Default",
                theme,
                label: `theme-${theme}`,
                title: `${bookName} theme:${theme}`,
            }));
        return [...brandingCases, ...themeCases];
    });

    test.each(cases)("$title", async (testCase) => {
        // Select the book first, then set branding and theme: each of setBranding/setTheme brings
        // the currently-selected book up to date so it picks up the corresponding files/appearance.
        await selectBook(testCase.bookFolder);
        await setBranding(testCase.branding);
        await setTheme(testCase.theme);
        var screenshotsDir = ensureDir(
            Path.join(testCase.bookFolder, "screenshots"),
        );
        var referenceScreenPath = Path.join(
            screenshotsDir,
            `${testCase.label}-reference.png`,
        );
        if (!fs.existsSync(referenceScreenPath)) {
            console.log(
                chalk.blueBright(
                    `Creating reference image for ${testCase.title}`,
                ),
            );
            await saveScreenshot(referenceScreenPath);
            return;
        }
        var currentScreenshotPath = Path.join(
            screenshotsDir,
            `${testCase.label}-current.png`,
        );
        await saveScreenshot(currentScreenshotPath);
        await comparePreviewImage(
            referenceScreenPath,
            currentScreenshotPath,
            Path.join(screenshotsDir, `${testCase.label}-diff.png`),
        );
    });

    async function saveScreenshot(imagePath: string) {
        await page.goto("http://localhost:8089/bloom/book-preview/index.htm");
        await page.waitForSelector("body");

        await argosScreenshot(page, imagePath.replace(".png", ""), {
            scale: "device",
        });
    }

    function ensureDir(path: string) {
        if (!fs.existsSync(path)) {
            fs.mkdirSync(path);
        }
        return path;
    }

    async function setBranding(branding: string) {
        // Enhance: get us on the correct collection (currently we can only handle the one collection)

        // Branding is normally derived from the (checksum-validated) subscription code and can't
        // be set directly. This test-only endpoint (present in DEBUG builds only) forces the
        // branding and brings the selected book up to date so it picks up that branding's files.
        let result = await fetch(
            `http://localhost:8089/bloom/api/e2e/setBranding`,
            {
                method: "POST",
                body: branding,
            },
        );
        expect(result.ok).toBe(true);
    }
    async function setTheme(theme: string) {
        // Appearance theme is a per-book setting. This test-only endpoint (present in DEBUG builds
        // only) sets it and brings the selected book up to date so its appearance.css is
        // regenerated for that theme.
        let result = await fetch(
            `http://localhost:8089/bloom/api/e2e/setTheme`,
            {
                method: "POST",
                body: theme,
            },
        );
        expect(result.ok).toBe(true);
    }
    async function selectBook(bookPath: string) {
        // Enhance: get us on the correct collection (currently we can only handle the one collection)

        // get us on the correct book
        let result = await fetch(
            `http://localhost:8089/bloom/api/collections/selected-book?path=${bookPath}&collection-id=${encodeURIComponent(
                Path.dirname(bookPath),
            )}`,
            {
                method: "POST",
            },
        );
        expect(result.ok).toBe(true);
    }

    async function comparePreviewImage(
        referencePath: string,
        testPath: string,
        diffPath: string,
    ) {
        const referenceImage = PNG.sync.read(fs.readFileSync(referencePath));
        const testImage = PNG.sync.read(fs.readFileSync(testPath));
        const { width, height } = referenceImage;
        const diff = new PNG({ width, height });

        const numberOfDifferentPixels = Pixelmatch(
            referenceImage.data,
            testImage.data,
            diff.data,
            width,
            height,
            {
                threshold: 0.1,
                aaColor: [255, 0, 0], // the default is yellow. this sets it to red, the same as the non-anti-aliased pixels
            },
        );
        if (numberOfDifferentPixels > 0) {
            // PNG.sync.write returns a Node Buffer. TypeScript 6.0's newer lib types
            // made Buffer/ArrayBufferView generic, so cast to Uint8Array (which Buffer
            // extends) to satisfy fs.writeFileSync's parameter type.
            fs.writeFileSync(diffPath, PNG.sync.write(diff) as Uint8Array);
            console.log(
                chalk.black.bgYellow(
                    `${testPath} differed from the reference by ${numberOfDifferentPixels} pixels. The diff image is at ${diffPath}`,
                ),
            );
            console.log(
                chalk.yellow(
                    `If the new version is correct, replace ${referencePath} with ${testPath}`,
                ),
            );
            expect(numberOfDifferentPixels).toBe(0);
        }
    }
});

async function launchBloomIfNeeded() {
    if (await isBloomRunning()) {
        return;
    }
    const p = `${Path.join(
        process.cwd(),
        "collections",
        "basic",
        "basic.bloomCollection",
    )}`;
    console.log(`Launching Bloom with ${p}`);
    execFile("../../output/Debug/Bloom.exe ", [p]);

    const startTime = Date.now();
    while (Date.now() - startTime < 20000) {
        if (await isBloomRunning()) {
            return;
        }
        await new Promise((resolve) => setTimeout(resolve, 1000));
    }
    expect(false, "Bloom did not start").toBe(true);
}

async function isBloomRunning() {
    try {
        const r = await fetch("http://localhost:8089/bloom/testconnection");
        return r.ok;
    } catch (e) {
        return false;
    }
}
