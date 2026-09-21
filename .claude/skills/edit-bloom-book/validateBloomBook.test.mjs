/* eslint-env node */
import assert from "node:assert/strict";
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import test from "node:test";
import { fileURLToPath } from "node:url";

import { validateBook } from "./validateBloomBook.mjs";

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);
const repoRoot = path.resolve(__dirname, "..", "..", "..");

const makeBook = (bodyContent) => `<!DOCTYPE html>
<html>
<head><meta name="BloomFormatVersion" content="3.0"></head>
<body>
<div id="bloomDataDiv"></div>
${bodyContent}
</body>
</html>`;

const validateTemporaryBook = (testContext, bodyContent) => {
    const tempDir = fs.mkdtempSync(path.join(os.tmpdir(), "bloom-validator-"));
    testContext.after(() =>
        fs.rmSync(tempDir, { recursive: true, force: true }),
    );
    const bookPath = path.join(tempDir, "Bloom.html");
    fs.writeFileSync(bookPath, makeBook(bodyContent), "utf8");
    return validateBook(bookPath);
};

test("validator accepts known-good sample shell books", () => {
    const books = [
        path.join(
            repoRoot,
            "src",
            "content",
            "templates",
            "Sample Shells",
            "A Family Learns about Immunisations",
            "A Family Learns about Immunisations.htm",
        ),
        path.join(
            repoRoot,
            "src",
            "content",
            "templates",
            "Sample Shells",
            "The Moon and the Cap",
            "The Moon and the Cap.htm",
        ),
    ];

    for (const book of books) {
        const result = validateBook(book);
        assert.deepEqual(result.errors, [], book);
    }
});

test("validator rejects malformed translation groups", (testContext) => {
    const result = validateTemporaryBook(
        testContext,
        `<div class="bloom-page" id="page1">
            <div class="marginBox">
                <div class="bloom-translationGroup">
                    <span class="bloom-editable" lang="en"></span>
                </div>
            </div>
        </div>`,
    );

    assert.match(
        result.errors.join("\n"),
        /must contain at least one direct \.bloom-editable or textarea child/,
    );
});

test("validator rejects malformed split panes", (testContext) => {
    const result = validateTemporaryBook(
        testContext,
        `<div class="bloom-page" id="page1">
            <div class="marginBox">
                <div class="split-pane horizontal-percent">
                    <div class="split-pane-component position-top"></div>
                    <div class="split-pane-divider horizontal-divider"></div>
                    <div class="split-pane-component position-bottom">
                        <div class="split-pane-component-inner"></div>
                    </div>
                </div>
            </div>
        </div>`,
    );

    assert.match(
        result.errors.join("\n"),
        /must have exactly one direct \.split-pane-component-inner child/,
    );
});

test("validator accepts nested origami split panes", (testContext) => {
    const result = validateTemporaryBook(
        testContext,
        `<div class="bloom-page" id="page1">
            <div class="split-pane-component marginBox">
                <div class="split-pane horizontal-percent">
                    <div class="split-pane-component position-top">
                        <div class="split-pane vertical-percent">
                            <div class="split-pane-component position-left">
                                <div class="split-pane-component-inner"></div>
                            </div>
                            <div class="split-pane-divider vertical-divider"></div>
                            <div class="split-pane-component position-right">
                                <div class="split-pane-component-inner"></div>
                            </div>
                        </div>
                    </div>
                    <div class="split-pane-divider horizontal-divider"></div>
                    <div class="split-pane-component position-bottom">
                        <div class="split-pane-component-inner"></div>
                    </div>
                </div>
            </div>
        </div>`,
    );

    assert.deepEqual(result.errors, []);
});

test("validator rejects malformed canvas image containers", (testContext) => {
    const result = validateTemporaryBook(
        testContext,
        `<div class="bloom-page" id="page1">
            <div class="marginBox">
                <div class="bloom-canvas bloom-has-canvas-element">
                    <div class="bloom-canvas-element bloom-backgroundImage">
                        <div class="bloom-imageContainer"></div>
                    </div>
                </div>
            </div>
        </div>`,
    );

    assert.match(
        result.errors.join("\n"),
        /must have exactly one direct img child/,
    );
});
