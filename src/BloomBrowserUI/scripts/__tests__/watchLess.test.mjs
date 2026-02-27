import fs from "fs";
import os from "os";
import path from "path";
import { describe, it, expect, beforeEach, afterEach } from "vitest";
import { LessWatchManager } from "../watchLess.mjs";

const silentLogger = {
    log: () => {},
    warn: () => {},
    error: () => {},
};

let tempDir;
let sourceRoot;
let outputRoot;
let metadataPath;

function makeDir(dirPath) {
    fs.mkdirSync(dirPath, { recursive: true });
}

function writeFile(filePath, contents) {
    makeDir(path.dirname(filePath));
    fs.writeFileSync(filePath, contents);
}

function makeManager(overrides = {}) {
    return new LessWatchManager({
        repoRoot: tempDir,
        metadataPath,
        logger: silentLogger,
        targets: [
            {
                name: "test",
                root: sourceRoot,
                outputBase: outputRoot,
            },
        ],
        ...overrides,
    });
}

function getEntryId(manager, filePath) {
    const key = path
        .relative(manager.repoRoot, path.resolve(filePath))
        .replace(/\\/g, "/");
    return key;
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
        const baselineMTime = fs.statSync(cssPath).mtimeMs;

        await new Promise((resolve) => setTimeout(resolve, 30));
        writeFile(entryPath, "body { color: black; }\n");
        await manager.handleFileChanged(entryPath, "entry updated");
        const deps = manager.entryDependencies.get(entryId) ?? [];
        expect(deps.length).toBe(1);

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
});
