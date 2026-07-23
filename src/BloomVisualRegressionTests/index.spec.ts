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
// The PID of the Bloom actually serving our temp collection. Bloom can relaunch into a new process
// after startup, so the process we spawned is not necessarily the one serving — killing only the
// spawned PID left orphaned Blooms holding the temp folder open. We read the real PID from
// instanceInfo and kill that too.
let bloomServingPid: number | null = null;

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
        // be set directly. This test-only endpoint (registered only in e2e test mode) forces the
        // branding and brings the selected book up to date so it picks up that branding's files.
        let result = await fetch(`${bloomOrigin}/bloom/api/e2e/setBranding`, {
            method: "POST",
            body: branding,
        });
        expect(result.ok).toBe(true);
    }
    async function setTheme(theme: string) {
        // Appearance theme is a per-book setting. This test-only endpoint (registered only in e2e
        // test mode) sets it and brings the selected book up to date so its appearance.css is
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
    // error box in Bloom). This test-only endpoint (e2e test mode only) lets us poll safely instead.
    async function waitForCollectionReady() {
        // Up to 30s: switching to the collection tab reloads its webview, which can be slow on a
        // loaded machine; a too-short wait was an occasional source of spurious failures.
        for (let attempt = 0; attempt < 60; attempt++) {
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
    // publish tab to be active. This test-only endpoint is registered only in e2e test mode.
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

        // Count differing pixels with pixelmatch (its anti-aliasing handling is what keeps the count
        // stable). Pass null for the output so it only counts; we render our own, more legible diff
        // below.
        const numberOfDifferentPixels = Pixelmatch(
            referenceImage.data,
            testImage.data,
            null,
            width,
            height,
            { threshold: 0.1 },
        );
        if (numberOfDifferentPixels > 0) {
            writeDirectionalDiff(referenceImage, testImage, diffPath);
            console.log(
                chalk.black.bgYellow(
                    `${testPath} differed from the reference by ${numberOfDifferentPixels} pixels. The diff image is at ${diffPath}`,
                ),
            );
            console.log(
                chalk.yellow(
                    `Diff colors: blue = darker in the reference (e.g. old text), red = darker in the current (e.g. new text). ` +
                        `If the new version is correct, replace ${referencePath} with ${testPath}`,
                ),
            );
            expect(numberOfDifferentPixels).toBe(0);
        }
    }
});

// Write a human-friendly diff image that shows the DIRECTION of each significant change instead of a
// flat "these pixels differ" mask. Per pixel: if the reference is significantly darker than the
// current, paint blue (something dark was here in the reference and is gone/lighter now — e.g. text
// at its old position); if the reference is significantly lighter, paint red (something dark is here
// now that was not — e.g. text at its new position). Unchanged pixels are white so the changes stand
// out. "Significant" is a luminance-delta threshold, which reads well for dark-text-on-light pages.
function writeDirectionalDiff(reference: PNG, current: PNG, diffPath: string) {
    const { width, height } = reference;
    const out = new PNG({ width, height });
    const THRESHOLD = 32; // luminance units (0..255) that count as a "significant" change
    const luminance = (data: Buffer, i: number) =>
        0.299 * data[i] + 0.587 * data[i + 1] + 0.114 * data[i + 2];
    for (let p = 0; p < width * height; p++) {
        const i = p * 4;
        const delta = luminance(reference.data, i) - luminance(current.data, i);
        let r = 255,
            g = 255,
            b = 255; // unchanged -> white
        if (delta < -THRESHOLD) {
            r = 0; // reference darker than current -> blue
            g = 0;
            b = 255;
        } else if (delta > THRESHOLD) {
            r = 255; // reference lighter than current -> red
            g = 0;
            b = 0;
        }
        out.data[i] = r;
        out.data[i + 1] = g;
        out.data[i + 2] = b;
        out.data[i + 3] = 255;
    }
    fs.writeFileSync(diffPath, PNG.sync.write(out) as Uint8Array);
}

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
): Promise<{ origin: string; processId?: number } | null> {
    for (const port of CANDIDATE_PORTS) {
        const origin = `http://localhost:${port}`;
        try {
            const r = await fetch(`${origin}/bloom/api/common/instanceInfo`);
            if (!r.ok) continue;
            const info = (await r.json()) as {
                editableCollectionFolder?: string;
                processId?: number;
            };
            if (
                info.editableCollectionFolder &&
                samePath(info.editableCollectionFolder, wantFolder)
            )
                return { origin, processId: info.processId };
        } catch (e) {
            // Nothing responding on that port; keep looking.
        }
    }
    return null;
}

// Populate the throwaway temp collection that Bloom will open. By default we export the *committed*
// (HEAD) state of collections/, so a run is deterministic and immune to accidental working-tree
// changes — Bloom's own book rewrites, or a stray Bloom editing the repo copy. The reference images
// still live in the working tree (see capturePlayerPages/saveScreenshot), so the
// regenerate -> eyeball -> commit workflow is unaffected by this. Set BLOOM_VR_WORKING_TREE=1 to
// instead render your uncommitted working-tree changes (for deliberately modifying or adding test
// books). If git or tar is unavailable, or any step of the export fails, we fall back to copying the
// working tree so the suite still runs.
function populateTempCollections(dest: string) {
    if (process.env.BLOOM_VR_WORKING_TREE === "1") {
        console.log(
            "BLOOM_VR_WORKING_TREE=1: rendering the working-tree collection (uncommitted changes included).",
        );
        fs.cpSync(repoCollectionsRoot, dest, { recursive: true });
        return;
    }
    try {
        // Export the COMMITTED (HEAD) tracked files under collections/ straight from git. We avoid
        // `git archive` + `tar`: the HEAD:<subtree> tree-ish comes back empty in a git worktree, and
        // system `tar` varies by platform (GNU vs bsdtar; only GNU has --force-local; each treats a
        // "C:" path differently) — both bit us. We copy tracked files only: Bloom regenerates the
        // gitignored support files (origami.css, branding.css, ...) itself, and the screenshots/
        // reference images are read from the repo working tree, not this temp copy. Any git failure
        // (git not on PATH, not a repo, a staged-but-uncommitted new file, ...) throws to the catch.
        const listed = execFileSync("git", [
            "ls-files",
            "-z",
            "--",
            "collections",
        ])
            .toString()
            .split("\0")
            .filter(Boolean)
            .filter((rel) => !rel.includes("/screenshots/"));
        if (listed.length === 0)
            throw new Error("git ls-files found no tracked collection files");
        for (const rel of listed) {
            // rel is cwd-relative, e.g. "collections/basic/basic.bloomCollection". Write its
            // committed content to the matching path under dest (dest/basic/...). Keep it a Buffer,
            // not a string, so binary book images round-trip exactly.
            const content = execFileSync("git", ["show", `HEAD:./${rel}`], {
                maxBuffer: 256 * 1024 * 1024,
            });
            const outPath = Path.join(dest, Path.relative("collections", rel));
            fs.mkdirSync(Path.dirname(outPath), { recursive: true });
            fs.writeFileSync(outPath, content);
        }
        if (!fs.existsSync(Path.join(dest, "basic", "basic.bloomCollection")))
            throw new Error(
                "committed collection is missing basic/basic.bloomCollection",
            );
        warnIfWorkingTreeBookChanges();
        console.log(
            "Rendering the committed (HEAD) test collection. Set BLOOM_VR_WORKING_TREE=1 to render working-tree changes.",
        );
    } catch (e) {
        console.warn(
            `Could not export the committed collection from git (${(e as Error).message}); ` +
                "falling back to a working-tree copy.",
        );
        fs.cpSync(repoCollectionsRoot, dest, { recursive: true });
    }
}

// Warn (never fail) when the working tree has uncommitted changes to book inputs under collections/,
// so it is never a surprise that the default run ignored them (it renders committed HEAD). Reference
// images (under screenshots/) are excluded — an uncommitted reference update is a normal state.
function warnIfWorkingTreeBookChanges() {
    try {
        const out = execFileSync("git", [
            "status",
            "--porcelain",
            "--",
            "collections",
        ]).toString();
        const changed = out
            .split("\n")
            .map((line) => line.slice(3).trim())
            .filter((p) => p && !p.includes("/screenshots/"));
        if (changed.length > 0)
            console.warn(
                `Note: ignoring ${changed.length} uncommitted change(s) under collections/ ` +
                    "(rendering committed HEAD; use BLOOM_VR_WORKING_TREE=1 to render them). e.g. " +
                    changed.slice(0, 5).join(", "),
            );
    } catch (e) {
        // best-effort; a warning is not worth failing the run over
    }
}

// Populate a throwaway temp collection (committed HEAD by default; see populateTempCollections) and
// launch a dedicated Bloom on it, then wait until that instance is serving it. We always launch our
// own (rather than reusing a developer's Bloom) so the run is deterministic and never touches the
// repo collection.
async function launchDedicatedBloom() {
    // Canonicalize immediately: os.tmpdir() is an 8.3 short path on Windows, but Bloom reports the
    // long form, so we normalize here (and in samePath) to make the discovery match work.
    tempCollectionsRoot = canonicalPath(
        fs.mkdtempSync(Path.join(os.tmpdir(), "bloom-vr-")),
    );
    populateTempCollections(tempCollectionsRoot);
    // Backstop: tidy up even if the run is aborted (e.g. by an unhandled rejection) before afterAll.
    process.once("exit", cleanupOnExit);

    const collection = Path.join(
        tempCollectionsRoot,
        "basic",
        "basic.bloomCollection",
    );
    // The exe lands in a config/platform-specific folder depending on the build; try the known
    // locations. Release is included because CI runs the suite against Release builds. Debug is
    // listed first for the common local (go.sh) case; a clean CI checkout only has the config it built.
    const exeCandidates = [
        "../../output/Debug/x64/Bloom.exe",
        "../../output/Debug/AnyCPU/Bloom.exe",
        "../../output/Debug/Bloom.exe",
        "../../output/Release/x64/Bloom.exe",
        "../../output/Release/AnyCPU/Bloom.exe",
        "../../output/Release/Bloom.exe",
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
        const match = await findBloomServingCollection(wantFolder);
        if (match) {
            bloomOrigin = match.origin;
            bloomServingPid = match.processId ?? null;
            console.log(
                `Dedicated Bloom is ready at ${bloomOrigin} (pid ${bloomServingPid ?? "?"})`,
            );
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

// Kill the dedicated Bloom we launched, along with its WebView2 child processes. We kill both the
// PID we spawned and the PID actually serving our collection: Bloom can relaunch into a new process
// after startup, so those can differ, and killing only the spawned one left an orphaned Bloom
// holding the temp folder open (which then failed to delete). Idempotent.
function stopBloom() {
    const pids = [bloomProcess?.pid, bloomServingPid].filter(
        (p): p is number => typeof p === "number",
    );
    bloomProcess = null;
    bloomServingPid = null;
    for (const pid of pids) {
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
            maxRetries: 20,
            retryDelay: 500,
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
