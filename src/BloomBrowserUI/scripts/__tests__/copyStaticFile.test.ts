import * as fs from "node:fs";
import * as os from "node:os";
import * as path from "node:path";
import { describe, it, expect, beforeEach, afterEach } from "vitest";
import { copyStaticFile } from "../copyStaticFile.mjs";

let tempDir: string;
let browserUIRoot: string;
let outputBase: string;

function makeDir(dirPath: string) {
    fs.mkdirSync(dirPath, { recursive: true });
}

function writeFile(filePath: string, contents: string) {
    makeDir(path.dirname(filePath));
    fs.writeFileSync(filePath, contents);
}

beforeEach(() => {
    tempDir = fs.mkdtempSync(path.join(os.tmpdir(), "copy-static-"));
    browserUIRoot = path.join(tempDir, "browserUI");
    outputBase = path.join(tempDir, "out");
    makeDir(browserUIRoot);
});

afterEach(() => {
    fs.rmSync(tempDir, { recursive: true, force: true });
});

describe("copyStaticFile", () => {
    it("ignores tsconfig.json files in nested folders", () => {
        const nestedTsConfig = path.join(
            browserUIRoot,
            "sub",
            "folder",
            "tsconfig.json",
        );
        writeFile(nestedTsConfig, '{"compilerOptions":{}}\n');

        const copied = copyStaticFile(nestedTsConfig, {
            browserUIRoot,
            outputBase,
            quiet: true,
        });

        expect(copied).toBe(false);
        expect(
            fs.existsSync(
                path.join(outputBase, "sub", "folder", "tsconfig.json"),
            ),
        ).toBe(false);
    });
});
