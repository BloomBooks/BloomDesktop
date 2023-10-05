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
                    !f.startsWith("Sample Texts")
            )
            .map((f) => Path.join(collectionPath, f));
        return paths;
    });
    const brandings = ["Default", "Local-Community", "UEEP[Uzbek]"];
    // create a two dimensional array with all combinations of bookFolders and brandings
    const bookFoldersAndBrandings = bookFolders.flatMap((bookFolder) => {
        return brandings.map((branding) => {
            return [bookFolder, branding];
        });
    });

    test.each(bookFoldersAndBrandings)(
        `%# "%s" Branding:%s`,
        async (bookFolder, branding) => {
            await setBranding(branding);
            await selectBook(bookFolder);
            var screenshotsDir = ensureDir(
                Path.join(bookFolder, "screenshots")
            );
            var referenceScreenPath = Path.join(
                screenshotsDir,
                `${branding}-reference.png`
            );
            if (!fs.existsSync(referenceScreenPath)) {
                console.log(
                    chalk.blueBright(
                        `Creating reference image for ${bookFolder}`
                    )
                );
                await saveScreenshot(referenceScreenPath);
                return;
            }
            var currentScreenshotPath = Path.join(
                screenshotsDir,
                `${branding}-current.png`
            );
            await saveScreenshot(currentScreenshotPath);
            await comparePreviewImage(
                referenceScreenPath,
                currentScreenshotPath,
                Path.join(screenshotsDir, `${branding}-diff.png`)
            );
        }
    );

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

        // get us on the correct branding
        let result = await fetch(
            `http://localhost:8089/bloom/api/settings/branding`,
            {
                method: "POST",
                body: branding,
            }
        );
        expect(result.ok).toBe(true);
    }
    async function selectBook(bookPath: string) {
        // Enhance: get us on the correct collection (currently we can only handle the one collection)

        // get us on the correct book
        let result = await fetch(
            `http://localhost:8089/bloom/api/collections/selected-book?path=${bookPath}&collection-id=${encodeURIComponent(
                Path.dirname(bookPath)
            )}`,
            {
                method: "POST",
            }
        );
        expect(result.ok).toBe(true);
    }

    async function comparePreviewImage(
        referencePath: string,
        testPath: string,
        diffPath: string
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
            }
        );
        if (numberOfDifferentPixels > 0) {
            fs.writeFileSync(diffPath, PNG.sync.write(diff));
            console.log(
                chalk.black.bgYellow(
                    `${testPath} differed from the reference by ${numberOfDifferentPixels} pixels. The diff image is at ${diffPath}`
                )
            );
            console.log(
                chalk.yellow(
                    `If the new version is correct, replace ${referencePath} with ${testPath}`
                )
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
        "basic.bloomCollection"
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
