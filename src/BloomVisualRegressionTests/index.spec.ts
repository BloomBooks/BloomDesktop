import { execFile, execFileSync, ChildProcess } from "child_process";
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
import * as os from "os";
import { PNG } from "pngjs";
import Pixelmatch from "pixelmatch";
import * as Path from "path";

// The Bloom HTTP origin the suite talks to. launchDedicatedBloom resolves this at startup to the
// port our launched instance actually opened on, instead of assuming 8089.
let bloomOrigin = "http://localhost:8089";

// We must not let Bloom mutate the committed collections: opening a book brings it up to date
// (rewriting .htm/.css), regenerates thumbnail.png, copies branding files into the book folder, etc.
// So each run copies the collections to a throwaway temp folder and launches a dedicated Bloom on
// THAT. Reference/current/diff images still live under the repo book folders (they are committed and
// the "accept the new render" workflow needs a stable path), so the two are decoupled: Bloom operates
// on the temp copy (see toTempBookFolder); screenshots are read/written in the repo copy.
const repoCollectionsRoot = Path.join(process.cwd(), "collections");
let tempCollectionsRoot: string | null = null;
// The dedicated Bloom we launch, kept so we can shut it down afterwards.
let bloomProcess: ChildProcess | null = null;

describe("All books", () => {
    let page: Page;
    // A second page dedicated to bloom-player captures, with its own fixed viewport, so that
    // navigating/resizing it never disturbs the book-preview screenshots taken on `page`.
    let playerPage: Page;
    let browser: Browser;
    let context: BrowserContext;

    beforeAll(async () => {
        await launchDedicatedBloom();
        browser = await chromium.launch();
        context = await browser.newContext();
        page = await context.newPage();
        playerPage = await context.newPage();
        await playerPage.setViewportSize({ width: 900, height: 1200 });
    });
    afterAll(async () => {
        // We drove a dedicated, throwaway Bloom on a temp copy of the collection, so there is
        // nothing in the repo to reset (that is the whole point of the temp copy). Just shut
        // everything down and delete the temp copy. This runs whether the tests passed or failed;
        // cleanupOnExit is a backstop for the case where the run is aborted before we get here.
        await browser?.close();
        stopBloom();
        cleanupTempCollections();
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
        // Bloom operates on the temp copy; screenshots (below) still go to the repo book folder.
        await selectBook(toTempBookFolder(testCase.bookFolder));
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
        // The first preview load right after a book is brought up to date can occasionally come back
        // before the book content is in the DOM (a cold-start race), which made the very first case
        // time out waiting for the page. Retry the navigation until the book page is actually present,
        // then screenshot. Waiting for .bloom-page (not just body) is also the real "content ready"
        // signal we want before capturing.
        for (let attempt = 0; ; attempt++) {
            await page.goto(`${bloomOrigin}/bloom/book-preview/index.htm`, {
                waitUntil: "networkidle",
            });
            const ready = await page
                .waitForSelector(".bloom-page", { timeout: 15000 })
                .then(() => true)
                .catch(() => false);
            if (ready) break;
            if (attempt >= 2)
                throw new Error(
                    "book-preview never rendered a .bloom-page after 3 attempts",
                );
        }

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

    // Read the number of real (non-duplicate) player pages, waiting until the count stops changing.
    // bloom-player builds its swiper slides asynchronously (and clones duplicates for looping), so a
    // single early read can catch a partial set — which made the page count, and therefore which
    // page each screenshot captured, vary from run to run. Poll until several consecutive reads agree.
    async function stablePlayerPageCount(): Promise<number> {
        let last = -1;
        let agreements = 0;
        for (let attempt = 0; attempt < 40; attempt++) {
            const count = await playerPage.evaluate(
                () =>
                    (globalThis as any).document.querySelectorAll(
                        ".swiper-slide:not(.swiper-slide-duplicate)",
                    ).length,
            );
            if (count > 0 && count === last) {
                if (++agreements >= 3) return count;
            } else {
                agreements = 0;
                last = count;
            }
            await playerPage.waitForTimeout(250);
        }
        if (last < 1)
            throw new Error("bloom-player page count never stabilized above 0");
        return last;
    }

    // Wait until the active player page is actually ready to screenshot — not just present. The two
    // things that otherwise render differently from run to run are the web font (bloom-player loads
    // Andika asynchronously, and text metrics/shaping change the instant it arrives) and images; a
    // fixed timeout raced both. Wait for document.fonts.ready, for every image on the active page to
    // finish, and then for one more layout frame. Returns the active .bloom-page element to shoot.
    async function waitForActivePageReady() {
        const active = await playerPage.waitForSelector(
            ".swiper-slide-active .bloom-page",
            { timeout: 30000 },
        );
        await playerPage.evaluate(async () => {
            const g = globalThis as any;
            const doc = g.document;
            await doc.fonts.ready;
            const page = doc.querySelector(".swiper-slide-active .bloom-page");
            const imgs = page ? Array.from(page.querySelectorAll("img")) : [];
            await Promise.all(
                imgs.map((img: any) =>
                    img.complete
                        ? Promise.resolve()
                        : new Promise((resolve) => {
                              img.addEventListener("load", resolve, {
                                  once: true,
                              });
                              img.addEventListener("error", resolve, {
                                  once: true,
                              });
                          }),
                ),
            );
            // Fonts/images are in; let layout settle for two frames before we capture.
            await new Promise((resolve) =>
                g.requestAnimationFrame(() => g.requestAnimationFrame(resolve)),
            );
        });
        return active;
    }

    // Render the staged BloomPUB in bloom-player and capture (or compare) one clean image per page.
    async function capturePlayerPages(
        stagedUrl: string,
        labelBase: string,
        screenshotsDir: string,
    ) {
        // Discover how many pages the player shows. bloom-player lazy-renders pages, so we can't
        // count .bloom-page elements; count the (non-duplicate) swiper slides, once stable.
        await playerPage.goto(playerUrl(stagedUrl, 0), {
            waitUntil: "networkidle",
        });
        await playerPage
            .waitForSelector(".bloom-page", { timeout: 30000 })
            .catch(() => {});
        const pageCount = await stablePlayerPageCount();

        for (let n = 0; n < pageCount; n++) {
            await playerPage.goto(playerUrl(stagedUrl, n), {
                waitUntil: "networkidle",
            });
            // Hide scrollbars: on pages whose text overflows the device page, bloom-player shows a
            // scrollbar (via the niceScroll plugin) whose thumb renders slightly differently from
            // run to run (a few hundred pixels of noise), which would make the comparison flaky. It
            // is player chrome, not book content, and its rails are position:absolute overlays (so
            // hiding them does not reflow the page), so we remove it for a stable capture. Add it
            // before the settle wait so it can't perturb layout timing afterward.
            await playerPage.addStyleTag({
                content:
                    ".nicescroll-rails,.nicescroll-cursors{display:none!important}",
            });
            // Screenshot just the active page element, so the image is the book page itself with no
            // player chrome or letterbox around it — once fonts/images/layout have settled.
            const pageElement = await waitForActivePageReady();
            await captureOrCompare(
                `${labelBase}-player-p${n}`,
                screenshotsDir,
                async (imagePath) => {
                    await pageElement.screenshot({ path: imagePath });
                },
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
// these to find the port our launched instance opened on. A developer's own Bloom may also be on one
// of these ports, so we match on the open collection folder (below) rather than assuming a port.
const CANDIDATE_PORTS = [8089, 8092, 8095, 8098, 8101, 8104];

// Map a repo book folder to the corresponding folder in the temp copy that Bloom actually has open.
// selectBook must point Bloom at the temp copy; screenshots stay under the repo book folder.
function toTempBookFolder(repoBookFolder: string): string {
    if (!tempCollectionsRoot)
        throw new Error("Temp collection copy has not been created yet");
    return Path.join(
        tempCollectionsRoot,
        Path.relative(repoCollectionsRoot, repoBookFolder),
    );
}

// Resolve a path to its canonical on-disk form. On Windows this is essential because os.tmpdir()
// returns an 8.3 short path (e.g. C:\Users\JOHNTH~1\...) while Bloom reports the long form
// (C:\Users\JohnThomson\...); realpathSync.native expands the short name and fixes casing so the two
// actually compare equal. Falls back to Path.resolve if the path does not exist yet.
function canonicalPath(p: string): string {
    try {
        return fs.realpathSync.native(p);
    } catch (e) {
        return Path.resolve(p);
    }
}

// Windows paths compare case-insensitively; canonicalize (see above) then lowercase before comparing.
function samePath(a: string, b: string): boolean {
    return canonicalPath(a).toLowerCase() === canonicalPath(b).toLowerCase();
}

// Return the origin (e.g. "http://localhost:8092") of the running Bloom whose open editable
// collection is wantFolder, or null if none is found yet. Matching the collection folder (rather
// than just the collection name "basic") is what distinguishes our temp-copy instance from a Bloom
// the developer may already have open on the repo copy.
async function findBloomServingCollection(
    wantFolder: string,
): Promise<string | null> {
    for (const port of CANDIDATE_PORTS) {
        const origin = `http://localhost:${port}`;
        try {
            const r = await fetch(`${origin}/bloom/api/common/instanceInfo`);
            if (!r.ok) continue;
            const info = (await r.json()) as {
                editableCollectionFolder?: string;
            };
            if (
                info.editableCollectionFolder &&
                samePath(info.editableCollectionFolder, wantFolder)
            )
                return origin;
        } catch (e) {
            // Nothing responding on that port; keep looking.
        }
    }
    return null;
}

// Copy the committed collections to a throwaway temp folder and launch a dedicated Bloom on it, then
// wait until that instance is serving the temp collection. We always launch our own (rather than
// reusing a developer's Bloom) so the run is deterministic and never touches the repo collection.
async function launchDedicatedBloom() {
    // Canonicalize immediately: os.tmpdir() is an 8.3 short path on Windows, but Bloom reports the
    // long form, so we normalize here (and in samePath) to make the discovery match work.
    tempCollectionsRoot = canonicalPath(
        fs.mkdtempSync(Path.join(os.tmpdir(), "bloom-vr-")),
    );
    fs.cpSync(repoCollectionsRoot, tempCollectionsRoot, { recursive: true });
    // Backstop: tidy up even if the run is aborted (e.g. by an unhandled rejection) before afterAll.
    process.once("exit", cleanupOnExit);

    const collection = Path.join(
        tempCollectionsRoot,
        "basic",
        "basic.bloomCollection",
    );
    // The exe lands in a platform-specific folder depending on the build; try the known locations.
    const exeCandidates = [
        "../../output/Debug/x64/Bloom.exe",
        "../../output/Debug/AnyCPU/Bloom.exe",
        "../../output/Debug/Bloom.exe",
    ];
    const exe = exeCandidates.find((c) => fs.existsSync(c));
    if (!exe) {
        throw new Error(
            `Could not find a built Bloom.exe (looked in: ${exeCandidates.join(", ")}). ` +
                `Build Bloom, then re-run.`,
        );
    }
    console.log(`Launching ${exe} on ${collection}`);
    // --e2e: skip the DEBUG "Attach debugger now" prompt and suppress modal error dialogs so a
    // Bloom problem fails the test instead of hanging the run. --automation: allow this instance to
    // run alongside a Bloom the developer already has open (bypasses the single-instance token).
    bloomProcess = execFile(exe, [collection, "--e2e", "--automation"]);

    // Discover which port our instance opened on by matching the collection folder it has open.
    const wantFolder = Path.join(tempCollectionsRoot, "basic");
    const startTime = Date.now();
    while (Date.now() - startTime < 90000) {
        const origin = await findBloomServingCollection(wantFolder);
        if (origin) {
            bloomOrigin = origin;
            console.log(`Dedicated Bloom is ready at ${bloomOrigin}`);
            return;
        }
        await new Promise((resolve) => setTimeout(resolve, 1500));
    }
    // Timed out: report which instances we could see so a mismatch is diagnosable rather than opaque.
    const seen: string[] = [];
    for (const port of CANDIDATE_PORTS) {
        try {
            const r = await fetch(
                `http://localhost:${port}/bloom/api/common/instanceInfo`,
            );
            if (!r.ok) continue;
            const info = (await r.json()) as {
                editableCollectionFolder?: string;
            };
            if (info.editableCollectionFolder)
                seen.push(`${port} -> ${info.editableCollectionFolder}`);
        } catch (e) {
            // nothing on this port
        }
    }
    throw new Error(
        `The dedicated Bloom did not open the temp collection within 90s.\n` +
            `  wanted: ${wantFolder}\n` +
            `  Bloom instances seen: ${seen.length ? seen.join("; ") : "none"}`,
    );
}

// Kill the dedicated Bloom we launched, along with its WebView2 child processes. Idempotent.
function stopBloom() {
    const pid = bloomProcess?.pid;
    bloomProcess = null;
    if (pid == null) return;
    try {
        if (process.platform === "win32")
            // /T kills the whole tree (Bloom spawns WebView2 child processes); /F forces it.
            execFileSync("taskkill", ["/pid", String(pid), "/t", "/f"], {
                stdio: "ignore",
            });
        else process.kill(pid, "SIGTERM");
    } catch (e) {
        // Already gone; nothing to do.
    }
}

// Delete the throwaway collection copy. Bloom may release file handles slightly after it dies, so
// let rmSync retry a few times. Idempotent.
function cleanupTempCollections() {
    const dir = tempCollectionsRoot;
    tempCollectionsRoot = null;
    if (!dir) return;
    try {
        fs.rmSync(dir, {
            recursive: true,
            force: true,
            maxRetries: 10,
            retryDelay: 300,
        });
    } catch (e) {
        console.warn(`Could not remove temp collection copy at ${dir}: ${e}`);
    }
}

// Synchronous last-resort cleanup for the process 'exit' event (afterAll may not run if the run is
// aborted). Both helpers are synchronous, as an 'exit' handler requires.
function cleanupOnExit() {
    stopBloom();
    cleanupTempCollections();
}
