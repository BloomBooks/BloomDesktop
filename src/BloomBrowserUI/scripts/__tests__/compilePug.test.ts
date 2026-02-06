import * as fs from "node:fs";
import * as os from "node:os";
import * as path from "node:path";
import { describe, it, expect, beforeEach, afterEach } from "vitest";
import { compilePugFiles } from "../compilePug.mjs";

let tempDir: string;
let browserUIRoot: string;
let contentRoot: string;
let outputBase: string;

function makeDir(dirPath: string) {
    fs.mkdirSync(dirPath, { recursive: true });
}

function writeFile(filePath: string, contents: string) {
    makeDir(path.dirname(filePath));
    fs.writeFileSync(filePath, contents);
}

beforeEach(() => {
    tempDir = fs.mkdtempSync(path.join(os.tmpdir(), "compile-pug-"));
    browserUIRoot = path.join(tempDir, "browserUI");
    contentRoot = path.join(tempDir, "content");
    outputBase = path.join(tempDir, "out");
    makeDir(browserUIRoot);
    makeDir(contentRoot);
});

afterEach(() => {
    fs.rmSync(tempDir, { recursive: true, force: true });
});

describe("compilePugFiles", () => {
    it("recompiles dependents when an included pug file changes", async () => {
        const partialPath = path.join(browserUIRoot, "partials", "partial.pug");
        const mainPath = path.join(browserUIRoot, "pages", "main.pug");

        writeFile(partialPath, "p Partial A\n");

        writeFile(
            mainPath,
            [
                "doctype html",
                "html",
                "  body",
                "    include ../partials/partial.pug",
                "",
            ].join("\n"),
        );

        await compilePugFiles({ browserUIRoot, contentRoot, outputBase });

        const outPath = path.join(outputBase, "pages", "main.html");
        expect(fs.existsSync(outPath)).toBe(true);
        const firstHtml = fs.readFileSync(outPath, "utf8");
        expect(firstHtml).toContain("Partial A");

        await new Promise((resolve) => setTimeout(resolve, 30));
        writeFile(partialPath, "p Partial B\n");

        await compilePugFiles({ browserUIRoot, contentRoot, outputBase });
        const secondHtml = fs.readFileSync(outPath, "utf8");
        expect(secondHtml).toContain("Partial B");
        expect(secondHtml).not.toContain("Partial A");
    });
});
