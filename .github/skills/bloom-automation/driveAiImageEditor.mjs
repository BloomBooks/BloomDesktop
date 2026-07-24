// Drive the "Edit with AI…" image-editor flow inside a running Bloom.exe over CDP.
//
// This is the AI-Image-Editor-specific companion to the generic helpers in this folder
// (bloomProcessStatus/switchWorkspaceTab/…). It reaches ACROSS three frames — the shell,
// the "page" content iframe, and the editor overlay iframe (the bloom-ai-image-tools app) —
// which a normal single-frame driver cannot. See ai-image-editor-driving.md for the writeup.
//
// Prereqs: Bloom launched from this worktree with the editor linked and dev tools on, e.g.
//   ./go.sh --with bloom-ai-image-tools=D:/bloom-ai-image-tools
// A book must be selected in the Edit tab, on a page that has an AI-editable raster image.
//
// Usage (cdp port defaults to httpPort+2, matching Bloom's port blocks):
//   node driveAiImageEditor.mjs --http-port 8092 images       # dump current-page images
//   node driveAiImageEditor.mjs --http-port 8092 frames        # list all frames
//   node driveAiImageEditor.mjs --http-port 8092 credits       # per-book-image credits (reads FILE metadata)
//   node driveAiImageEditor.mjs --http-port 8092 dummy-edit    # full free edit+commit via the Local Dummy model
//   node driveAiImageEditor.mjs --http-port 8092 dummy-edit --match ai-image1.png --shot out.png
//
// The "Local Dummy (No AI)" model runs entirely in-browser (no OpenRouter, no key, no cost);
// Bloom only offers it when it sends showDeveloperTools:true, which AiImageEditorApi.HandleLaunch
// does via ApplicationUpdateSupport.IsDevOrAlpha (developer + alpha/unstable builds).
import { createRequire } from "node:module";
import path from "node:path";
import { fileURLToPath } from "node:url";

const here = path.dirname(fileURLToPath(import.meta.url));
// repo root is four levels up from .github/skills/bloom-automation/
const repoRoot = path.resolve(here, "..", "..", "..");
const componentTester = path.join(
    repoRoot,
    "src/BloomBrowserUI/react_components/component-tester",
);
const { chromium } = createRequire(path.join(componentTester, "package.json"))(
    "playwright",
);

const args = process.argv.slice(2);
const opt = (name, def) => {
    const i = args.indexOf(name);
    return i >= 0 && args[i + 1] ? args[i + 1] : def;
};
// A positional token is the command; every "--flag value" pair is skipped.
const valueFlags = new Set(["--http-port", "--cdp-port", "--match", "--shot"]);
const positional = [];
for (let i = 0; i < args.length; i++) {
    if (args[i].startsWith("--")) {
        if (valueFlags.has(args[i])) i++; // skip its value
        continue;
    }
    positional.push(args[i]);
}
const command = positional[0] || "frames";
const httpPort = opt("--http-port", "8092");
const cdpPort = opt("--cdp-port", String(Number(httpPort) + 2));
const matchName = opt("--match", "ai-image"); // substring of the target image's src
const shot = opt(
    "--shot",
    path.join(repoRoot, "output/screenshots/ai-editor.png"),
);

const log = (...a) => console.log(...a);
const bloomPage = (browser) =>
    browser
        .contexts()
        .flatMap((c) => c.pages())
        .find(
            (p) =>
                p.url().includes("/bloom/") &&
                !p.url().startsWith("devtools://"),
        );
const contentFrame = (page) => page.frames().find((f) => f.name() === "page");
const editorFrame = (page) =>
    page.frames().find((f) => f.url().includes("localhost:3000"));

const dumpImages = async (page) => {
    const f = contentFrame(page);
    return f.evaluate(() =>
        Array.from(
            document.querySelectorAll('img, [style*="background-image"]'),
        ).map((el) => {
            const isImg = el.tagName === "IMG";
            const bg = getComputedStyle(el).backgroundImage;
            const src = isImg
                ? el.getAttribute("src")
                : bg && bg !== "none"
                  ? (bg.match(/url\(["']?([^"')]+)/)?.[1] ?? "")
                  : "";
            return {
                tag: el.tagName.toLowerCase(),
                src: src || "",
                dataCopyright: el.getAttribute("data-copyright"),
                dataCreator: el.getAttribute("data-creator"),
                dataLicense: el.getAttribute("data-license"),
                inCanvasElement: !!el.closest(
                    ".bloom-canvas-element, .bloom-imageContainer",
                ),
            };
        }),
    );
};

// Open the editor overlay the real way: right-click the canvas element to raise its
// context menu, then click "Edit with AI…". We use raw mouse coordinates because a
// Comical <canvas> overlay sits on top of the image and intercepts element-targeted clicks.
const openEditorOverlay = async (page) => {
    const f = contentFrame(page);
    const img = f.locator(`img[src*="${matchName}"]`).first();
    await img.waitFor({ state: "visible", timeout: 10000 });
    const box = await img.boundingBox();
    const cx = box.x + box.width / 2;
    const cy = box.y + box.height / 2;
    await page.mouse.click(cx, cy); // select the canvas element
    await page.waitForTimeout(400);
    await page.mouse.click(cx, cy, { button: "right" }); // open its context menu
    await page.waitForTimeout(700);
    await f
        .locator('[role="menuitem"]', { hasText: "Edit with AI" })
        .first()
        .click();
    let ef;
    for (let i = 0; i < 40 && !ef; i++) {
        await page.waitForTimeout(500);
        ef = editorFrame(page);
    }
    if (!ef)
        throw new Error("editor overlay frame (localhost:3000) never appeared");
    await ef.waitForLoadState("domcontentloaded");
    await page.waitForTimeout(1500); // let the ready/init handshake settle
    return ef;
};

// Editor-UI recipe mirrors bloom-ai-image-tools/tests/bloom-host-harness.spec.ts.
const dummyEditAndCommit = async (ef, page) => {
    await ef.getByRole("button", { name: /Enhance/i }).click();
    await ef.getByText("Custom Edit", { exact: true }).click();
    await ef.getByTestId("tool-model-picker-custom").click();
    await ef.getByText("Local Dummy (No AI)").click();
    await ef.locator("body").press("Escape");
    await ef.getByTestId("input-prompt").fill("Add a dummy banner");
    await ef.getByRole("button", { name: /Apply Changes/i }).click();
    const commit = ef.getByTestId("bloom-host-commit-current-result");
    await commit.waitFor({ state: "visible", timeout: 40000 });
    await commit.click();
    await page.waitForTimeout(2500); // commit + (current-page) save+rethink
};

const browser = await chromium.connectOverCDP(`http://localhost:${cdpPort}`);
try {
    const page = bloomPage(browser);
    if (!page) throw new Error(`No Bloom /bloom/ target on CDP ${cdpPort}`);
    await page.waitForLoadState("domcontentloaded");

    if (command === "frames") {
        log(
            JSON.stringify(
                page.frames().map((f) => ({ name: f.name(), url: f.url() })),
                null,
                2,
            ),
        );
    } else if (command === "images") {
        log(JSON.stringify(await dumpImages(page), null, 2));
    } else if (command === "credits") {
        // Reads each book image's EMBEDDED FILE metadata (host GetCreditsForImageFile),
        // so it verifies what a reload would re-derive — not just the live DOM attributes.
        const body = await page.evaluate(async () => {
            const r = await fetch("/bloom/api/aiImageEditor/launch", {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: "{}",
            });
            return r.json();
        });
        log("showDeveloperTools:", body.showDeveloperTools);
        (body.bookImages || []).forEach((b) =>
            log(" ", b.id, "->", JSON.stringify(b.credits)),
        );
    } else if (command === "dummy-edit") {
        log(
            "before:",
            JSON.stringify(
                (await dumpImages(page)).filter((i) => i.inCanvasElement),
            ),
        );
        const ef = await openEditorOverlay(page);
        log("editor opened:", ef.url());
        await dummyEditAndCommit(ef, page);
        await page.screenshot({ path: shot });
        log(
            "after :",
            JSON.stringify(
                (await dumpImages(page)).filter((i) => i.inCanvasElement),
            ),
        );
        log("screenshot ->", shot);
    } else {
        log(
            `Unknown command "${command}". Use: frames | images | credits | dummy-edit`,
        );
        process.exitCode = 1;
    }
} finally {
    await browser.close();
}
