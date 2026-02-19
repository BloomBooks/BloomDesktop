import * as fs from "node:fs";
import * as os from "node:os";
import * as path from "node:path";
import { describe, it, expect, beforeEach, afterEach } from "vitest";
import { LessWatchManager } from "../watchLess.mjs";
import type { LessWatchTarget } from "../watchLess.mjs";

const silentLogger = {
    log: () => {},
    warn: () => {},
    error: () => {},
};

let tempDir: string;
let sourceRoot: string;
let outputRoot: string;
let metadataPath: string;

function makeDir(dirPath: string) {
    fs.mkdirSync(dirPath, { recursive: true });
}

function writeFile(filePath: string, contents: string) {
    makeDir(path.dirname(filePath));
    fs.writeFileSync(filePath, contents);
}

function makeManager(overrides: Partial<{ targets: LessWatchTarget[] }> = {}) {
    const defaultTarget: LessWatchTarget = {
        name: "test",
        root: sourceRoot,
        outputBase: outputRoot,
    };

    return new LessWatchManager({
        repoRoot: tempDir,
        metadataPath,
        logger: silentLogger,
        targets: overrides.targets ?? [defaultTarget],
    });
}

function getEntryId(manager: LessWatchManager, filePath: string) {
    return path
        .relative(manager.repoRoot, path.resolve(filePath))
        .replace(/\\/g, "/");
}

beforeEach(() => {
    tempDir = fs.mkdtempSync(path.join(os.tmpdir(), "watch-less-"));
    sourceRoot = path.join(tempDir, "src");
    outputRoot = path.join(tempDir, "out");
    metadataPath = path.join(outputRoot, ".state.json");
    makeDir(sourceRoot);
});

afterEach(() => {
    fs.rmSync(tempDir, { recursive: true, force: true });
});

describe("LessWatchManager", () => {
    it("compiles missing outputs and records metadata", async () => {
        const entryPath = path.join(sourceRoot, "pages", "main.less");
        const partialPath = path.join(sourceRoot, "partials", "colors.less");
        writeFile(partialPath, "@primary: #ff0000;\n");
        writeFile(
            entryPath,
            '@import "../partials/colors.less";\nbody { color: @primary; }\n',
        );

        const manager = makeManager();
        await manager.initialize();

        const cssPath = path.join(outputRoot, "pages", "main.css");
        expect(fs.existsSync(cssPath)).toBe(true);
        const css = fs.readFileSync(cssPath, "utf8");
        expect(css).toContain("body");
        expect(fs.existsSync(`${cssPath}.map`)).toBe(true);

        const state = JSON.parse(fs.readFileSync(metadataPath, "utf8"));
        const entryId = getEntryId(manager, entryPath);
        expect(state.entries[entryId]).toContain(
            path.relative(tempDir, partialPath).replace(/\\/g, "/"),
        );
    });

    it("rebuilds when dependency is newer on startup", async () => {
        const entryPath = path.join(sourceRoot, "main.less");
        const partialPath = path.join(sourceRoot, "dep.less");
        writeFile(partialPath, "@val: blue;\n");
        writeFile(entryPath, '@import "dep.less";\nbody { color: @val; }\n');

        const firstManager = makeManager();
        await firstManager.initialize();
        const cssPath = path.join(outputRoot, "main.css");
        const initialMTime = fs.statSync(cssPath).mtimeMs;

        await new Promise((resolve) => setTimeout(resolve, 30));
        writeFile(partialPath, "@val: green;\n");

        const secondManager = makeManager();
        await secondManager.initialize();
        const rebuiltMTime = fs.statSync(cssPath).mtimeMs;
        expect(rebuiltMTime).toBeGreaterThan(initialMTime);
    });

    it("updates dependency graph when imports change", async () => {
        const entryPath = path.join(sourceRoot, "main.less");
        const depPath = path.join(sourceRoot, "dep.less");
        writeFile(depPath, "@val: blue;\n");
        writeFile(entryPath, '@import "dep.less";\nbody { color: @val; }\n');

        const manager = makeManager();
        await manager.initialize();
        const entryId = getEntryId(manager, entryPath);
        const cssPath = path.join(outputRoot, "main.css");

        await new Promise((resolve) => setTimeout(resolve, 30));
        writeFile(entryPath, "body { color: black; }\n");
        await manager.handleFileChanged(entryPath, "entry updated");
        const deps = manager.entryDependencies.get(entryId) ?? [];
        expect(deps.length).toBe(1);
        const baselineMTime = fs.statSync(cssPath).mtimeMs;

        await new Promise((resolve) => setTimeout(resolve, 30));
        writeFile(depPath, "@val: red;\n");
        await manager.handleFileChanged(depPath, "dep changed");
        const afterMTime = fs.statSync(cssPath).mtimeMs;
        expect(afterMTime).toBe(baselineMTime);
    });

    it("adds new dependencies and rebuilds when partial changes", async () => {
        const entryPath = path.join(sourceRoot, "main.less");
        const depA = path.join(sourceRoot, "depA.less");
        const depB = path.join(sourceRoot, "depB.less");
        writeFile(depA, "@val: blue;\n");
        writeFile(depB, "@alt: red;\n");
        writeFile(entryPath, '@import "depA.less";\nbody { color: @val; }\n');

        const manager = makeManager();
        await manager.initialize();
        const cssPath = path.join(outputRoot, "main.css");

        await new Promise((resolve) => setTimeout(resolve, 30));
        writeFile(
            entryPath,
            '@import "depA.less";\n@import "depB.less";\nbody { color: @alt; }\n',
        );
        await manager.handleFileChanged(entryPath, "entry changed");
        const entryId = getEntryId(manager, entryPath);
        const deps = manager.entryDependencies.get(entryId) ?? [];
        expect(deps.some((dep) => dep.endsWith("depB.less"))).toBe(true);

        await new Promise((resolve) => setTimeout(resolve, 30));
        writeFile(depB, "@alt: purple;\n");
        const before = fs.statSync(cssPath).mtimeMs;
        await manager.handleFileChanged(depB, "depB updated");
        const after = fs.statSync(cssPath).mtimeMs;
        expect(after).toBeGreaterThan(before);
    });

    it("removes outputs when an entry is deleted", async () => {
        const entryPath = path.join(sourceRoot, "main.less");
        writeFile(entryPath, "body { color: blue; }\n");
        const manager = makeManager();
        await manager.initialize();
        const cssPath = path.join(outputRoot, "main.css");
        expect(fs.existsSync(cssPath)).toBe(true);

        fs.unlinkSync(entryPath);
        await manager.handleFileRemoved(entryPath);
        expect(fs.existsSync(cssPath)).toBe(false);
        expect(fs.existsSync(`${cssPath}.map`)).toBe(false);
    });

    it("builds new entries on the fly", async () => {
        const manager = makeManager();
        await manager.initialize();

        const entryPath = path.join(sourceRoot, "new.less");
        writeFile(entryPath, "body { color: orange; }\n");
        await manager.handleFileAdded(manager.targets[0], entryPath);

        const cssPath = path.join(outputRoot, "new.css");
        expect(fs.existsSync(cssPath)).toBe(true);
    });

    it("rebuilds direct + transitive dependents when a dependency changes, even without prior metadata", async () => {
        const fontsPath = path.join(sourceRoot, "bloomWebFonts.less");
        const uiPath = path.join(sourceRoot, "bloomUI.less");
        const editModePath = path.join(
            sourceRoot,
            "bookEdit",
            "css",
            "editMode.less",
        );

        writeFile(fontsPath, "@UIFontStack: Arial;\n");
        writeFile(
            uiPath,
            '@import "./bloomWebFonts.less";\nbody { font-family: @UIFontStack; }\n',
        );
        writeFile(
            editModePath,
            '@import "../../bloomUI.less";\n.editMode { color: black; }\n',
        );

        // Simulate pre-existing CSS outputs (e.g. built by some other pipeline) so the manager
        // won't compile anything on startup unless it can still determine dependencies.
        await new Promise((resolve) => setTimeout(resolve, 30));
        const fontsCssPath = path.join(outputRoot, "bloomWebFonts.css");
        const uiCssPath = path.join(outputRoot, "bloomUI.css");
        const editModeCssPath = path.join(
            outputRoot,
            "bookEdit",
            "css",
            "editMode.css",
        );
        writeFile(fontsCssPath, "/* prebuilt */\n");
        writeFile(uiCssPath, "/* prebuilt */\n");
        writeFile(editModeCssPath, "/* prebuilt */\n");

        const manager = makeManager();
        await manager.initialize();

        const fontsBaseline = fs.statSync(fontsCssPath).mtimeMs;
        const uiBaseline = fs.statSync(uiCssPath).mtimeMs;
        const editModeBaseline = fs.statSync(editModeCssPath).mtimeMs;

        await new Promise((resolve) => setTimeout(resolve, 30));
        writeFile(fontsPath, "@UIFontStack: Verdana;\n");
        await manager.handleFileChanged(fontsPath, "fonts changed");

        const fontsAfter = fs.statSync(fontsCssPath).mtimeMs;
        const uiAfter = fs.statSync(uiCssPath).mtimeMs;
        const editModeAfter = fs.statSync(editModeCssPath).mtimeMs;

        expect(fontsAfter).toBeGreaterThan(fontsBaseline);
        expect(uiAfter).toBeGreaterThan(uiBaseline);
        expect(editModeAfter).toBeGreaterThan(editModeBaseline);
    });

    it("preserves metadata entries from other scopes", async () => {
        const uiRoot = path.join(tempDir, "ui");
        const contentRoot = path.join(tempDir, "content");
        makeDir(uiRoot);
        makeDir(contentRoot);

        const uiEntryPath = path.join(uiRoot, "ui.less");
        const contentEntryPath = path.join(contentRoot, "content.less");
        writeFile(uiEntryPath, "body { color: blue; }\n");
        writeFile(contentEntryPath, "body { color: green; }\n");

        const uiTarget: LessWatchTarget = {
            name: "ui",
            root: uiRoot,
            outputBase: path.join(outputRoot, "ui"),
        };
        const contentTarget: LessWatchTarget = {
            name: "content",
            root: contentRoot,
            outputBase: path.join(outputRoot, "content"),
        };

        const allTargetsManager = makeManager({
            targets: [uiTarget, contentTarget],
        });
        await allTargetsManager.initialize();

        const uiEntryId = getEntryId(allTargetsManager, uiEntryPath);
        const contentEntryId = getEntryId(allTargetsManager, contentEntryPath);

        const stateAfterAllTargets = JSON.parse(
            fs.readFileSync(metadataPath, "utf8"),
        );
        expect(stateAfterAllTargets.entries[uiEntryId]).toBeDefined();
        expect(stateAfterAllTargets.entries[contentEntryId]).toBeDefined();

        const contentOnlyManager = makeManager({ targets: [contentTarget] });
        await contentOnlyManager.initialize();

        const stateAfterContentOnly = JSON.parse(
            fs.readFileSync(metadataPath, "utf8"),
        );
        expect(stateAfterContentOnly.entries[uiEntryId]).toBeDefined();
        expect(stateAfterContentOnly.entries[contentEntryId]).toBeDefined();
    });
});
