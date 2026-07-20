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

// The Bloom HTTP origin the suite talks to. launchBloomIfNeeded resolves this at startup so we use
// whatever port Bloom actually opened on, instead of assuming 8089.
let bloomOrigin = "http://localhost:8089";

describe("All books", () => {
    let page: Page;
    // A second page dedicated to bloom-player captures, with its own fixed viewport, so that
    // navigating/resizing it never disturbs the book-preview screenshots taken on `page`.
    let playerPage: Page;
    let browser: Browser;
    let context: BrowserContext;

    beforeAll(async () => {
        await launchBloomIfNeeded();
        // If a Bloom was already running on a different collection, every selectBook would fail
        // with a confusing NullReferenceException. Fail fast with a clear message instead.
        await assertOnBasicCollection();
        browser = await chromium.launch();
        context = await browser.newContext();
        page = await context.newPage();
        playerPage = await context.newPage();
        await playerPage.setViewportSize({ width: 900, height: 1200 });
    });
    afterAll(async () => {
        // Leave every book on the Default branding. Otherwise each book stays in whatever
        // branding was tested last, which leaves the fixture in a non-default state: an extra
        // xmatter page (with a fresh page id), a changed brandingProjectName in meta.json, and
        // branding-specific files copied into the book folder. Switching back to Default also
        // makes Bloom clean up those branding-specific support files.
        // Park the capture pages first (see parkCapturePages): the last test left them showing a
        // book / staged BloomPUB, and the resets below rewrite those files.
        await parkCapturePages();
        // Selecting a book requires the collection tab (see selectBook); the last test left us in
        // the publish tab. Switch back once and wait for it to be ready; selecting books and
        // setting branding/theme below does not change tabs, so we stay ready.
        await selectTab("collection");
        await waitForCollectionReady();
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
        // Park the capture pages before we mutate this book. Otherwise the previous case's still-open
        // book-preview / bloom-player page keeps requesting book and staged-BloomPUB files while this
        // case rewrites them, which caused mid-run "file is being used by another process" and
        // "PlaceForStagingBook not found" errors (and, under a debugger, timeouts).
        await parkCapturePages();
        // Bloom does not expect the selected book to change while in the publish tab (a previous
        // case's player capture leaves us there), and switching to the collection tab reloads it.
        // So return to the collection tab and wait for it to be ready before selecting.
        await selectTab("collection");
        await waitForCollectionReady();
        // Select the book first, then set branding and theme: each of setBranding/setTheme brings
        // the currently-selected book up to date so it picks up the corresponding files/appearance.
        await selectBook(testCase.bookFolder);
        await setBranding(testCase.branding);
        await setTheme(testCase.theme);
        // Each of the calls above brings the book up to date, which rewrites the book's support
        // files (basePage.css, previewMode.css, etc.) and triggers an async re-render. Give that a
        // moment to settle so our book-preview/player requests below don't race the tail of a write
        // (which Bloom logs and retries, but which stops a debugger set to break on the exception).
        await new Promise((resolve) => setTimeout(resolve, 1000));
        const screenshotsDir = ensureDir(
            Path.join(testCase.bookFolder, "screenshots"),
        );

        // (1) The book-preview (edit/preview) rendering: one screenshot of the whole book.
        await captureOrCompare(testCase.label, screenshotsDir, (imagePath) =>
            saveScreenshot(imagePath),
        );

        // (2) The bloom-player rendering of the STAGED BloomPUB, one screenshot per player page.
        // A book can look different in bloom-player even when the preview is unchanged, so we check
        // it too. The player's page set can differ from the preview (device xmatter changes the
        // number/order of pages), so these have their own per-page reference images, enumerated
        // from the player itself.
        await selectTab("publish");
        const stagedUrl = await makeBloomPubPreview();
        await capturePlayerPages(stagedUrl, testCase.label, screenshotsDir);
    });

    // Create the reference image if it does not exist yet; otherwise capture a current image and
    // compare it to the reference. `shoot(path)` writes a screenshot to the given path.
    async function captureOrCompare(
        label: string,
        screenshotsDir: string,
        shoot: (imagePath: string) => Promise<void>,
    ) {
        const referencePath = Path.join(
            screenshotsDir,
            `${label}-reference.png`,
        );
        if (!fs.existsSync(referencePath)) {
            console.log(
                chalk.blueBright(`Creating reference image for ${label}`),
            );
            await shoot(referencePath);
            return;
        }
        const currentPath = Path.join(screenshotsDir, `${label}-current.png`);
        await shoot(currentPath);
        await comparePreviewImage(
            referencePath,
            currentPath,
            Path.join(screenshotsDir, `${label}-diff.png`),
        );
    }

    // Navigate both capture pages to about:blank so neither keeps requesting book files or staged
    // BloomPUB files. Bloom rewrites a book's files when it is brought up to date, and re-creates
    // the single PlaceForStagingBook folder for each new BloomPUB preview; a page still pointed at
    // the old content will re-request files mid-rewrite (file-lock IOException) or after the staging
    // folder is gone (DirectoryNotFoundException). Park them whenever we are about to mutate.
    async function parkCapturePages() {
        await page.goto("about:blank");
        await playerPage.goto("about:blank");
    }

    async function saveScreenshot(imagePath: string) {
        await page.goto(`${bloomOrigin}/bloom/book-preview/index.htm`);
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
        let result = await fetch(`${bloomOrigin}/bloom/api/e2e/setBranding`, {
            method: "POST",
            body: branding,
        });
        expect(result.ok).toBe(true);
    }
    async function setTheme(theme: string) {
        // Appearance theme is a per-book setting. This test-only endpoint (present in DEBUG builds
        // only) sets it and brings the selected book up to date so its appearance.css is
        // regenerated for that theme.
        let result = await fetch(`${bloomOrigin}/bloom/api/e2e/setTheme`, {
            method: "POST",
            body: theme,
        });
        expect(result.ok).toBe(true);
    }
    async function selectBook(bookPath: string) {
        // Enhance: get us on the correct collection (currently we can only handle the one collection)

        // get us on the correct book
        let result = await fetch(
            `${bloomOrigin}/bloom/api/collections/selected-book?path=${bookPath}&collection-id=${encodeURIComponent(
                Path.dirname(bookPath),
            )}`,
            {
                method: "POST",
            },
        );
        expect(result.ok).toBe(true);
    }

    // Switch Bloom to the given workspace tab ("collection" | "edit" | "publish"), going through
    // the same code path the UI uses. Staging a BloomPUB requires the publish tab; selecting a book
    // requires the collection tab.
    async function selectTab(tab: string) {
        const result = await fetch(
            `${bloomOrigin}/bloom/api/workspace/selectTab`,
            {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify({ tab }),
            },
        );
        expect(result.ok).toBe(true);
    }

    // Wait until the editable collection is loaded and its books are enumerable. Switching to the
    // collection tab reloads its webview; selecting a book during that window throws (and pops an
    // error box in Bloom). This test-only endpoint (DEBUG builds only) lets us poll safely instead.
    async function waitForCollectionReady() {
        for (let attempt = 0; attempt < 30; attempt++) {
            const result = await fetch(
                `${bloomOrigin}/bloom/api/e2e/isCollectionReady`,
            );
            if (result.ok && (await result.text()) === "true") return;
            await new Promise((resolve) => setTimeout(resolve, 500));
        }
        throw new Error("The collection tab did not become ready in time");
    }

    // Stage the currently selected book as a BloomPUB (exactly as Publish:BloomPub does) and return
    // the localhost URL of the staged book's .htm file, for loading in bloom-player. Requires the
    // publish tab to be active. This test-only endpoint is present in DEBUG builds only.
    async function makeBloomPubPreview(): Promise<string> {
        const result = await fetch(
            `${bloomOrigin}/bloom/api/e2e/makeBloomPubPreview`,
            {
                method: "POST",
                body: "",
            },
        );
        expect(result.ok).toBe(true);
        return result.text();
    }

    // Build the bloom-player URL for the staged book at a specific page (0-based). Mirrors what the
    // desktop app does (see RecordVideoWindow): the staged URL is already single-encoded, and
    // URLSearchParams encodes it again so it survives as a query parameter (see BL-11319).
    function playerUrl(stagedUrl: string, startPage: number) {
        const params = new URLSearchParams({
            url: stagedUrl,
            host: "bloomdesktop",
            independent: "false",
            initiallyShowAppBar: "false",
            hideNavButtons: "true",
            skipActivities: "true",
            "start-page": String(startPage),
        });
        return `${bloomOrigin}/bloom/bloom-player/dist/bloomplayer.htm?${params.toString()}`;
    }

    // Render the staged BloomPUB in bloom-player and capture (or compare) one clean image per page.
    async function capturePlayerPages(
        stagedUrl: string,
        labelBase: string,
        screenshotsDir: string,
    ) {
        // Discover how many pages the player shows. bloom-player lazy-renders pages, so we can't
        // count .bloom-page elements; count the (non-duplicate) swiper slides instead.
        await playerPage.goto(playerUrl(stagedUrl, 0), {
            waitUntil: "networkidle",
        });
        await playerPage
            .waitForSelector(".bloom-page", { timeout: 30000 })
            .catch(() => {});
        await playerPage.waitForTimeout(1500);
        const pageCount = await playerPage.evaluate(
            () =>
                (globalThis as any).document.querySelectorAll(
                    ".swiper-slide:not(.swiper-slide-duplicate)",
                ).length,
        );
        if (pageCount < 1) {
            throw new Error(
                `bloom-player showed no pages for ${labelBase}; staging or the player URL is wrong`,
            );
        }

        for (let n = 0; n < pageCount; n++) {
            await playerPage.goto(playerUrl(stagedUrl, n), {
                waitUntil: "networkidle",
            });
            // Screenshot just the active page element, so the image is the book page itself with no
            // player chrome or letterbox around it.
            const pageElement = await playerPage.waitForSelector(
                ".swiper-slide-active .bloom-page",
                { timeout: 30000 },
            );
            // Hide scrollbars: on pages whose text overflows the device page, bloom-player shows a
            // scrollbar (via the niceScroll plugin) whose thumb renders slightly differently from
            // run to run (a few hundred pixels of noise), which would make the comparison flaky. It
            // is player chrome, not book content, and its rails are position:absolute overlays (so
            // hiding them does not reflow the page), so we remove it for a stable capture.
            await playerPage.addStyleTag({
                content:
                    ".nicescroll-rails,.nicescroll-cursors{display:none!important}",
            });
            await playerPage.waitForTimeout(1500); // let fonts/layout settle
            await captureOrCompare(
                `${labelBase}-player-p${n}`,
                screenshotsDir,
                async (imagePath) => {
                    await pageElement.screenshot({ path: imagePath });
                },
            );
        }
    }

    // Fail fast if a Bloom instance was already running on a different collection: selecting one of
    // our test books would otherwise fail with a confusing NullReferenceException.
    async function assertOnBasicCollection() {
        const result = await fetch(
            `${bloomOrigin}/bloom/api/common/instanceInfo`,
        );
        expect(result.ok).toBe(true);
        const info = (await result.json()) as { collectionName: string };
        if (info.collectionName !== "basic") {
            throw new Error(
                `Bloom is running the '${info.collectionName}' collection, not the 'basic' test collection. ` +
                    `Close that Bloom instance (or open collections/basic/basic.bloomCollection) and re-run.`,
            );
        }
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

// Ports Bloom uses: it takes the next free block starting at 8089 (8089, 8092, 8095, ...). We probe
// these to attach to an already-running Bloom on whatever port it happens to have opened, rather
// than assuming 8089.
const CANDIDATE_PORTS = [8089, 8092, 8095, 8098, 8101, 8104];

// Return the origin (e.g. "http://localhost:8092") of a running Bloom that has the test ("basic")
// collection open, or null if none is found. Matching on the collection avoids attaching to some
// other Bloom the developer has open on a different collection.
async function findRunningBloomOnBasic(): Promise<string | null> {
    for (const port of CANDIDATE_PORTS) {
        const origin = `http://localhost:${port}`;
        try {
            const r = await fetch(`${origin}/bloom/api/common/instanceInfo`);
            if (!r.ok) continue;
            const info = (await r.json()) as { collectionName?: string };
            if (info.collectionName === "basic") return origin;
        } catch (e) {
            // Nothing responding on that port; keep looking.
        }
    }
    return null;
}

async function launchBloomIfNeeded() {
    // Prefer an already-running Bloom that is on the test collection and reuse it on whatever port
    // it opened on. A developer usually already has one running (e.g. via ./go.sh); this is also
    // the reliable path, since it doesn't depend on us building/launching Bloom correctly here.
    const existing = await findRunningBloomOnBasic();
    if (existing) {
        bloomOrigin = existing;
        console.log(`Using running Bloom at ${bloomOrigin}`);
        return;
    }

    // Otherwise launch one ourselves on the test collection. We launch the built exe with the
    // collection path because the source-aware launcher (./go.sh) can't be told which collection to
    // open — and opening the right collection is what makes selecting the test books work. The exe
    // lands in a platform-specific folder depending on the build, so try the known locations.
    const collection = Path.join(
        process.cwd(),
        "collections",
        "basic",
        "basic.bloomCollection",
    );
    const exeCandidates = [
        "../../output/Debug/x64/Bloom.exe",
        "../../output/Debug/AnyCPU/Bloom.exe",
        "../../output/Debug/Bloom.exe",
    ];
    const exe = exeCandidates.find((c) => fs.existsSync(c));
    if (!exe) {
        throw new Error(
            `Could not find a built Bloom.exe (looked in: ${exeCandidates.join(", ")}). ` +
                `Build Bloom, or start it yourself on collections/basic/basic.bloomCollection, then re-run.`,
        );
    }
    console.log(`Launching ${exe} on ${collection}`);
    execFile(exe, [collection]);

    // Wait for it to come up on the test collection (a freshly launched Bloom takes the standard
    // 8089 block when it is free, but we still discover the actual port rather than assume it).
    const startTime = Date.now();
    while (Date.now() - startTime < 60000) {
        const origin = await findRunningBloomOnBasic();
        if (origin) {
            bloomOrigin = origin;
            console.log(`Launched Bloom is ready at ${bloomOrigin}`);
            return;
        }
        await new Promise((resolve) => setTimeout(resolve, 1500));
    }
    expect(
        false,
        "Bloom did not start on the basic collection within 60s",
    ).toBe(true);
}
