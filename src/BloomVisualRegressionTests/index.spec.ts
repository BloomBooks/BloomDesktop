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
import { fileURLToPath } from "url";

// The Bloom HTTP origin the suite talks to. launchDedicatedBloom resolves this at startup to the
// port our launched instance actually opened on, instead of assuming 8089.
let bloomOrigin = "http://localhost:8089";

// This spec file lives in <repoRoot>/src/BloomVisualRegressionTests. Resolve paths from the file
// rather than from process.cwd() so they hold however vitest is invoked.
const repoRoot = Path.resolve(
    Path.dirname(fileURLToPath(import.meta.url)),
    "..",
    "..",
);

// The books and their reference screenshots are NOT in this repository. They live in
// https://github.com/BloomBooks/bloom-testing-inputs, at the commit named by
// build/testing-inputs.pin, which `node build/get-testing-inputs.mjs` materializes into
// output/testing-inputs. Because that is one exact commit, a run is reproducible without this
// suite doing anything clever. Set BLOOM_TESTING_INPUTS_DIR to a checkout of that repository to
// render your own in-progress input changes instead (see readme.md).
const testingInputsRoot =
    process.env.BLOOM_TESTING_INPUTS_DIR ??
    Path.join(repoRoot, "output", "testing-inputs");
const sourceCollectionsRoot = Path.join(testingInputsRoot, "collections");
// The ONE collection this suite renders. We launch a single Bloom on it (see
// launchDedicatedBloom) and Bloom cannot be made to switch collections mid-run, so a book in any
// other collection of the inputs repository is out of reach: selecting it fails. The inputs
// repository does now hold other collections (page-copy, for the BloomE2E copy-page test), so
// both the launch and the enumeration of books below go through this constant rather than taking
// whatever collections happen to be there.
const TESTED_COLLECTION = "basic";
if (!fs.existsSync(sourceCollectionsRoot)) {
    throw new Error(
        `Could not find the test-input collections at ${sourceCollectionsRoot}.\n` +
            (process.env.BLOOM_TESTING_INPUTS_DIR
                ? `BLOOM_TESTING_INPUTS_DIR is set to ${process.env.BLOOM_TESTING_INPUTS_DIR}; it must be a ` +
                  `checkout of https://github.com/BloomBooks/bloom-testing-inputs (the folder containing collections/).`
                : `Run: node build/get-testing-inputs.mjs`),
    );
}

// We must not let Bloom mutate the source collections: opening a book brings it up to date
// (rewriting .htm/.css), regenerates thumbnail.png, copies branding files into the book folder, etc.
// So each run copies the collections to a throwaway temp folder and launches a dedicated Bloom on
// THAT. Reference/current/diff images are read and written under the SOURCE book folders instead,
// because updating a baseline means changing the inputs repository. So the two are decoupled: Bloom
// operates on the temp copy (see toTempBookFolder); screenshots live in the source copy.
let tempCollectionsRoot: string | null = null;
// The dedicated Bloom we launch, kept so we can shut it down afterwards.
let bloomProcess: ChildProcess | null = null;
// Everything Bloom writes to stdout/stderr, kept so that when a launch fails (Bloom crashed, or its
// server never came up) we can report WHY instead of an opaque "did not open within 90s" timeout.
// Capped so a chatty-but-healthy Bloom can't grow this unbounded.
let bloomOutput = "";
const MAX_BLOOM_OUTPUT = 20000;
function recordBloomOutput(chunk: string) {
    bloomOutput = (bloomOutput + chunk).slice(-MAX_BLOOM_OUTPUT);
}
// Set if the Bloom process we spawned exits before it starts serving our collection. Distinguishes a
// crash-on-startup (fail fast with the exit code) from a still-running-but-not-ready poll.
let bloomExit: { code: number | null; signal: NodeJS.Signals | null } | null =
    null;
// The PID of the Bloom actually serving our temp collection. Bloom can relaunch into a new process
// after startup, so the process we spawned is not necessarily the one serving — killing only the
// spawned PID left orphaned Blooms holding the temp folder open. We read the real PID from
// instanceInfo and kill that too.
let bloomServingPid: number | null = null;

// How many times we will load a page looking for a render that is complete enough to screenshot.
// More than one because the first request can catch Bloom still busy with the book; bounded
// because a book whose image really is missing must fail, not retry forever.
const MAX_CAPTURE_ATTEMPTS = 3;

// The active bloom-player slide: the one page element the player captures screenshot.
const ACTIVE_PLAYER_PAGE = ".swiper-slide-active .bloom-page";

// Wait until what we are about to screenshot has actually finished rendering — not merely
// appeared — and report anything that came out wrong. The two things that otherwise render
// differently from run to run are the web fonts (both Bloom and bloom-player load Andika
// asynchronously, and text metrics and line breaking change the instant the real face arrives)
// and the images; a fixed timeout raced both. So wait for document.fonts.ready, for every image
// to finish, and then for one more layout frame.
//
// Waiting makes the capture DETERMINISTIC (it happens once the browser has finished trying) but
// not necessarily CORRECT: an image or font that ended in an ERROR state is settled too. So we
// also report those, rather than screenshot a page that is missing its pictures or has fallen
// back to a substitute font. A finished <img> with a src but no intrinsic width did not load;
// Bloom then renders its alt text ("This image, X, is missing or was loading too slowly."), which
// is exactly the wrong render we must never accept. (Note that alt text is on EVERY image, loaded
// or not, so its presence proves nothing — only naturalWidth does.)
//
// `rootSelector` scopes which images matter: the whole document for the book preview, or just the
// active slide for a bloom-player capture. Fonts are always document-wide. Returns a list of
// problems; empty means the page is good to capture.
//
// This is handed to page.evaluate() and therefore runs INSIDE the browser, so it must be entirely
// self-contained: it cannot call anything declared outside itself.
async function settleAndReportProblems(
    rootSelector: string | null,
): Promise<string[]> {
    const g = globalThis as any;
    const doc = g.document;
    await doc.fonts.ready;
    const root = rootSelector ? doc.querySelector(rootSelector) : doc;
    const problems: string[] = [];
    if (rootSelector && !root) {
        problems.push(`${rootSelector} was not present`);
        return problems;
    }

    // Pictures reach the screen two different ways, and we have to handle both. The book preview
    // uses real <img> elements. bloom-player does NOT: it paints a page's illustration as a CSS
    // background-image on a div, so the active slide contains no <img> at all and an image-only
    // check would silently pass on an empty list.
    const imgs: any[] = Array.from(root.querySelectorAll("img"));
    await Promise.all(
        imgs.map((img: any) =>
            img.complete
                ? Promise.resolve()
                : new Promise((resolve) => {
                      img.addEventListener("load", resolve, { once: true });
                      img.addEventListener("error", resolve, { once: true });
                  }),
        ),
    );

    // A background image exposes no load state to the DOM, so the only way to wait for one — or to
    // notice that it failed — is to ask for the same URL ourselves and watch that. The browser has
    // already requested it, so this normally resolves straight from the cache. Only look at
    // elements that actually occupy space: a hidden element's background is never fetched for the
    // render, so probing it could invent a failure that does not affect the screenshot.
    const backgroundUrls = new Set<string>();
    for (const el of Array.from(root.querySelectorAll("*")) as any[]) {
        const box = el.getBoundingClientRect();
        if (box.width === 0 || box.height === 0) continue;
        const background = g.getComputedStyle(el).backgroundImage;
        if (!background || background === "none") continue;
        for (const match of background.matchAll(/url\((["']?)(.*?)\1\)/g)) {
            const url = match[2];
            if (url && !url.startsWith("data:")) backgroundUrls.add(url);
        }
    }
    const failedBackgrounds: string[] = [];
    await Promise.all(
        Array.from(backgroundUrls).map(
            (url) =>
                new Promise((resolve) => {
                    const probe = new g.Image();
                    probe.addEventListener("load", resolve, { once: true });
                    probe.addEventListener(
                        "error",
                        () => {
                            failedBackgrounds.push(url);
                            resolve(null);
                        },
                        { once: true },
                    );
                    probe.src = url;
                }),
        ),
    );

    // Fonts/images are in; let layout settle for two frames before we capture.
    await new Promise((resolve) =>
        g.requestAnimationFrame(() => g.requestAnimationFrame(resolve)),
    );

    for (const img of imgs) {
        const src = img.getAttribute("src");
        if (src && img.naturalWidth === 0)
            problems.push(`image did not load: ${src}`);
    }
    for (const url of failedBackgrounds)
        problems.push(`background image did not load: ${url}`);
    for (const font of Array.from(doc.fonts) as any[]) {
        // Include the weight/style: a family is several FontFaces, and knowing which one failed is
        // the difference between "the font server is down" and "we asked for a bold that does not
        // exist".
        if (font.status === "error")
            problems.push(
                `font did not load: ${font.family} ${font.style} ${font.weight}`,
            );
    }
    return problems;
}

describe("All books", () => {
    let page: Page;
    // A second page dedicated to bloom-player captures, with its own fixed viewport, so that
    // navigating/resizing it never disturbs the book-preview screenshots taken on `page`.
    let playerPage: Page;
    let browser: Browser;
    let context: BrowserContext;
    // Every image comparison the CURRENT case has failed. A case compares one book-preview image
    // and one image per bloom-player page, and a stale baseline usually affects several of them.
    // Throwing on the first mismatch meant the later comparisons never even captured their
    // images, so accepting a real layout change took one ~3-minute run per image (BL-16638 took
    // three rounds). So collect them all and fail once, at the end of the case.
    let comparisonFailures: string[] = [];

    beforeAll(async () => {
        await launchDedicatedBloom();
        // Text must rasterize the same way on every machine, or the reference images are only
        // valid on whichever machine made them. By default Chrome anti-aliases text with LCD
        // sub-pixel rendering, using the host's DirectWrite gamma/contrast settings, so the same
        // glyphs come out with different colored fringes on a developer machine than on the CI
        // runner. --disable-lcd-text forces grayscale anti-aliasing instead;
        // --font-render-hinting=none removes the other host-dependent step; --force-color-profile
        // pins the color space the result is converted through.
        browser = await chromium.launch({
            args: [
                "--disable-lcd-text",
                "--font-render-hinting=none",
                "--force-color-profile=srgb",
            ],
        });
        context = await browser.newContext();
        andikaIsInstalled = await isFontInstalled(context, "Andika");
        if (andikaIsInstalled) {
            console.warn(chalk.black.bgYellow(ANDIKA_INSTALLED_WARNING));
        }
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

    // NB: currently, we don't have a way of making Bloom change collections, or re-running it with
    // a different collection, so we render only the books of TESTED_COLLECTION -- the one the Bloom
    // we launch has open. (We used to take every collection in the inputs repository, which was
    // fine while it held only one; when the page-copy collection was added for the BloomE2E
    // copy-page test, its books became tests that could never pass, because selecting a book that
    // is not in the open collection fails.)
    const collectionFolder = Path.join(
        sourceCollectionsRoot,
        TESTED_COLLECTION,
    );
    const bookFolders = fs
        .readdirSync(collectionFolder)
        .filter(
            (f) =>
                fs.statSync(Path.join(collectionFolder, f)).isDirectory() &&
                !f.startsWith("Sample Texts"),
        )
        .map((f) => Path.join(collectionFolder, f));
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
        comparisonFailures = [];
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
        // Bloom operates on the temp copy; screenshots (below) go to the source book folder.
        await selectBook(toTempBookFolder(testCase.bookFolder));
        await setBranding(testCase.branding);
        await setTheme(testCase.theme);
        // Each of the calls above brings the book up to date, which rewrites the book's support
        // files (basePage.css, previewMode.css, etc.) and triggers an async re-render. We used to
        // sleep a fixed second here to let that settle; saveScreenshot now waits for the preview to
        // actually BE ready (and reloads it if it is not), which is both safer and no slower.
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

        // One failure for the whole case, listing every image that did not match, so a single run
        // shows all the baselines that need looking at.
        if (comparisonFailures.length > 0)
            throw new Error(
                `${comparisonFailures.length} of this case's images did not match their ` +
                    `reference:\n  ${comparisonFailures.join("\n  ")}`,
            );
    });

    // Create the reference image if it does not exist yet; otherwise capture a current image and
    // compare it to the reference. `shoot(path)` writes a screenshot to the given path.
    // `screenshotsDir` is always in the SOURCE inputs tree (output/testing-inputs, or whatever
    // BLOOM_TESTING_INPUTS_DIR names), never in the temp copy, so that an accepted new baseline is
    // a file a developer can commit to the inputs repository. See readme.md.
    // `isPlayerCapture` marks a bloom-player screenshot, whose text comes from whatever Andika the
    // machine has (see andikaIsInstalled), as opposed to a book-preview one, which does not.
    async function captureOrCompare(
        label: string,
        screenshotsDir: string,
        shoot: (imagePath: string) => Promise<void>,
        isPlayerCapture = false,
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
            isPlayerCapture && andikaIsInstalled
                ? ANDIKA_INSTALLED_WARNING
                : undefined,
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

    // Load the book preview and wait until it is genuinely ready to screenshot — not merely
    // present. Returns a list of problems with this render; empty means it is good to capture.
    async function loadPreviewAndWaitUntilReady(): Promise<string[]> {
        await page.goto(`${bloomOrigin}/bloom/book-preview/index.htm`, {
            waitUntil: "networkidle",
        });
        // Waiting for .bloom-page (not just body) is the real "content is in the DOM" signal: the
        // first preview load right after a book is brought up to date can come back before Bloom
        // has put the book in it.
        const havePage = await page
            .waitForSelector(".bloom-page", { timeout: 15000 })
            .then(() => true)
            .catch(() => false);
        if (!havePage) return ["no .bloom-page ever appeared"];
        // The preview is one page containing the whole book, so the whole document is what we
        // are about to screenshot.
        return await page.evaluate(settleAndReportProblems, null);
    }

    async function saveScreenshot(imagePath: string) {
        // A preview requested while Bloom is still finishing with the book (it has just been
        // brought up to date, which rewrites its support files) can come back without the book in
        // the DOM, or with its images unserved. Both are transient, and both produce a wrong
        // screenshot rather than merely a late one, so reload until we get a good render. Failing
        // after a bounded number of tries keeps a genuinely broken book from passing quietly.
        for (let attempt = 1; ; attempt++) {
            const problems = await loadPreviewAndWaitUntilReady();
            if (problems.length === 0) break;
            if (attempt >= MAX_CAPTURE_ATTEMPTS)
                throw new Error(
                    `The book preview never rendered correctly (${MAX_CAPTURE_ATTEMPTS} attempts). ` +
                        `The last attempt had these problems:\n  ${problems.join("\n  ")}`,
                );
            console.log(
                chalk.yellow(
                    `book-preview attempt ${attempt} was not usable (${problems.join("; ")}); reloading.`,
                ),
            );
        }

        // Capture the body element rather than the whole page. A full-page capture's height comes
        // from documentElement.scrollHeight, which includes the last .bloom-page's escaping 15px
        // bottom margin (previewMode.css); that trailing background is not part of what we are
        // testing, and a change in it makes every case fail on "Image sizes do not match" -- which
        // aborts before any diff image is written. body's box stops at the content.
        await argosScreenshot(page, imagePath.replace(".png", ""), {
            scale: "device",
            element: "body",
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

    // The bloom-player captures must not depend on which fonts this machine has installed. When Bloom
    // stages a BloomPUB it strips the book's own @font-face rules (BL-12594) and leaves the font to
    // bloom-player, which declares Andika as `local("Andika")` first and the Bloom-served woff2 only
    // as a fallback. So a machine with Andika installed renders every player page with its own copy of
    // the font, and its glyphs differ from the reference images (which come from a machine without it,
    // like the CI runner) by tens to hundreds of pixels per page. That has twice led someone to "fix"
    // the suite by regenerating the baselines on their own machine, which just moved the failure to
    // CI. The book-preview captures are unaffected: the staged book's own @font-face points at the
    // served copy. So we don't refuse to run; we warn once up front, and when a player page then
    // mismatches we say in the failure itself that the font is the likely cause, so nobody reads it
    // as a regression or accepts it as a new baseline.
    let andikaIsInstalled = false;
    const ANDIKA_INSTALLED_WARNING =
        `Andika is installed on this machine. bloom-player prefers an installed Andika over the copy ` +
        `Bloom serves, so the bloom-player screenshots will be rendered with this machine's Andika and ` +
        `will probably differ from the reference images (which are rendered without it, as on CI). ` +
        `Player-page differences on this machine are probably that, not a regression, and must not be ` +
        `accepted as new baselines. Uninstall Andika to make these comparisons meaningful; Bloom ` +
        `does not need it installed.`;

    // The probe is a @font-face whose only source is local(<family>): it loads if and only if the
    // font is installed. Chrome may either resolve with no faces or reject when it is not.
    async function isFontInstalled(
        context: BrowserContext,
        family: string,
    ): Promise<boolean> {
        const probe = await context.newPage();
        try {
            await probe.setContent(
                `<style>@font-face { font-family: "bloom-vr-probe"; src: local("${family}"); }</style>`,
            );
            return await probe.evaluate(async () => {
                try {
                    const faces = await (globalThis as any).document.fonts.load(
                        '16px "bloom-vr-probe"',
                    );
                    return faces.some((f: any) => f.status === "loaded");
                } catch {
                    return false;
                }
            });
        } finally {
            await probe.close();
        }
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

    // Load player page `n` of the staged book and wait until its active page is genuinely ready to
    // screenshot, reloading if it is not. The player counterpart of loadPreviewAndWaitUntilReady()
    // plus saveScreenshot()'s retry loop, and deliberately held to the same contract: we never
    // screenshot a page whose images or fonts did not arrive. Returns the .bloom-page element to
    // shoot.
    async function loadPlayerPageAndWaitUntilReady(
        stagedUrl: string,
        n: number,
    ) {
        for (let attempt = 1; ; attempt++) {
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
            const active = await playerPage.waitForSelector(
                ACTIVE_PLAYER_PAGE,
                { timeout: 30000 },
            );
            // Only the active slide's images matter: bloom-player keeps the other slides in the
            // DOM, and lazy-renders them, so a not-yet-loaded image on a slide we are not
            // photographing is normal rather than a problem.
            const problems = await playerPage.evaluate(
                settleAndReportProblems,
                ACTIVE_PLAYER_PAGE,
            );
            if (problems.length === 0) return active;
            if (attempt >= MAX_CAPTURE_ATTEMPTS)
                throw new Error(
                    `bloom-player page ${n} never rendered correctly (${MAX_CAPTURE_ATTEMPTS} attempts). ` +
                        `The last attempt had these problems:\n  ${problems.join("\n  ")}`,
                );
            console.log(
                chalk.yellow(
                    `bloom-player page ${n} attempt ${attempt} was not usable (${problems.join("; ")}); reloading.`,
                ),
            );
        }
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
            // Screenshot just the active page element, so the image is the book page itself with no
            // player chrome or letterbox around it — once fonts/images/layout have settled.
            const pageElement = await loadPlayerPageAndWaitUntilReady(
                stagedUrl,
                n,
            );
            await captureOrCompare(
                `${labelBase}-player-p${n}`,
                screenshotsDir,
                async (imagePath) => {
                    await pageElement.screenshot({ path: imagePath });
                },
                true,
            );
        }
    }

    // Compare one captured image against its reference. This does NOT throw on a mismatch: it
    // appends a description to comparisonFailures, and the test body fails the case once it has
    // compared every image. Anything thrown while comparing (notably Pixelmatch's "Image sizes do
    // not match", which is itself a real failure) is recorded the same way.
    //
    // `likelyCause`, when given, is an explanation to attach to a mismatch (see andikaIsInstalled):
    // something we know about this machine that makes the difference expected rather than a regression.
    async function comparePreviewImage(
        referencePath: string,
        testPath: string,
        diffPath: string,
        likelyCause?: string,
    ) {
        try {
            await compareOrThrow(
                referencePath,
                testPath,
                diffPath,
                likelyCause,
            );
        } catch (error) {
            comparisonFailures.push(
                `${testPath}: ${error instanceof Error ? error.message : String(error)}`,
            );
        }
    }

    // The comparison itself: write a diff image and throw when the two images differ at all.
    async function compareOrThrow(
        referencePath: string,
        testPath: string,
        diffPath: string,
        likelyCause?: string,
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
            // A thrown Error rather than expect(...).toBe(0), so the failure itself carries the
            // diff path and, when we know one, the likely cause; the console lines above are lost in
            // a long run's output. comparePreviewImage catches this and records it.
            throw new Error(
                `${testPath} differed from ${referencePath} by ${numberOfDifferentPixels} pixels. ` +
                    `The diff image is at ${diffPath}.` +
                    (likelyCause ? `\n\n${likelyCause}` : ""),
            );
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

// Map a source book folder to the corresponding folder in the temp copy that Bloom actually has
// open. selectBook must point Bloom at the temp copy; screenshots stay under the source book folder.
function toTempBookFolder(sourceBookFolder: string): string {
    if (!tempCollectionsRoot)
        throw new Error("Temp collection copy has not been created yet");
    return Path.join(
        tempCollectionsRoot,
        Path.relative(sourceCollectionsRoot, sourceBookFolder),
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

// Populate the throwaway temp collection that Bloom will open, by copying TESTED_COLLECTION. The
// inputs repository's other collections belong to other suites and we never open them, so we do
// not copy them. A plain copy is enough for determinism now: output/testing-inputs is one exact
// commit of the inputs repository (build/testing-inputs.pin), so there is no working tree to
// differ from it. The screenshots/ folders are left out — they are baselines and outputs, read and
// written in the source tree (see captureOrCompare), and copying 48 reference PNGs per book would
// only slow the run down.
function populateTempCollections(dest: string) {
    const source = Path.join(sourceCollectionsRoot, TESTED_COLLECTION);
    fs.cpSync(source, Path.join(dest, TESTED_COLLECTION), {
        recursive: true,
        filter: (path) => Path.basename(path) !== "screenshots",
    });
    console.log(`Rendering the test collection from ${source}`);
}

// Populate a throwaway temp collection (see populateTempCollections) and launch a dedicated Bloom on
// it, then wait until that instance is serving it. We always launch our own (rather than reusing a
// developer's Bloom) so the run is deterministic and never touches the source collections.
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
        TESTED_COLLECTION,
        `${TESTED_COLLECTION}.bloomCollection`,
    );
    // The exe lands in a config/platform-specific folder depending on the build; try the known
    // locations. Release is included because CI runs the suite against Release builds. Debug is
    // listed first for the common local (go.sh) case; a clean CI checkout only has the config it built.
    const exeCandidates = [
        "Debug/x64",
        "Debug/AnyCPU",
        "Debug",
        "Release/x64",
        "Release/AnyCPU",
        "Release",
    ].map((sub) => Path.join(repoRoot, "output", sub, "Bloom.exe"));
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
    // Capture Bloom's output and watch for an early exit. Without this a launch failure (crash on
    // startup, missing WebView2 runtime, first-run dialog) is invisible: the poll below just runs
    // out the full 90s and reports "seen: none" with no clue why. Echo to our own stderr too so the
    // CI step log shows Bloom's startup output inline. execFile (no stdio:'ignore') gives us pipes.
    bloomProcess.stdout?.on("data", (d) => {
        const s = d.toString();
        recordBloomOutput(s);
        process.stderr.write(`[bloom stdout] ${s}`);
    });
    bloomProcess.stderr?.on("data", (d) => {
        const s = d.toString();
        recordBloomOutput(s);
        process.stderr.write(`[bloom stderr] ${s}`);
    });
    // 'error' fires when the exe can't even be spawned (e.g. not found / not executable).
    bloomProcess.on("error", (err) =>
        recordBloomOutput(`\n[spawn error] ${err.message}\n`),
    );
    bloomProcess.on("exit", (code, signal) => {
        bloomExit = { code, signal };
    });

    // Discover which port our instance opened on by matching the collection folder it has open.
    const wantFolder = Path.join(tempCollectionsRoot, TESTED_COLLECTION);
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
        // Fail fast if Bloom died on startup instead of waiting out the full timeout: the exit code
        // plus captured output tells us it crashed rather than that discovery merely couldn't see it.
        if (bloomExit) {
            throw new Error(
                `The dedicated Bloom exited before serving the temp collection ` +
                    `(code ${bloomExit.code}, signal ${bloomExit.signal}).\n` +
                    `  exe: ${exe}\n` +
                    `  wanted: ${wantFolder}\n` +
                    formatBloomOutput(),
            );
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
            `  exe: ${exe}\n` +
            `  wanted: ${wantFolder}\n` +
            `  still running: ${bloomExit ? "no (already exited)" : "yes"}\n` +
            `  Bloom instances seen: ${seen.length ? seen.join("; ") : "none"}\n` +
            formatBloomOutput(),
    );
}

// Render the tail of Bloom's captured stdout/stderr for inclusion in a launch-failure error, so the
// CI log shows what Bloom actually said. Kept separate so both failure paths format it identically.
function formatBloomOutput(): string {
    const trimmed = bloomOutput.trim();
    if (!trimmed) return `  Bloom output: (none captured)`;
    return `  Bloom output (last ${MAX_BLOOM_OUTPUT} chars):\n${trimmed}`;
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
