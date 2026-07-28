import * as fs from "node:fs";
import * as path from "node:path";
import { describe, it, expect } from "vitest";

// Guards the invariant behind the BL-15300 flicker fix: the @font-face declarations for
// Bloom's UI fonts must exist in exactly one source file (bloomUIFontFaces.css), and only an
// approved set of files may include it. If a duplicate declaration sneaks into a stylesheet
// that is compiled into a bundle or component styles, it gets injected into documents AFTER
// their text has rendered; the rendered text then switches to the new, not-yet-loaded copy of
// the font and goes blank for several frames. See bloomUIFontFaces.css for the full story.

const browserUiRoot = path.resolve(__dirname, "../..");

// Directory names we skip anywhere in the tree (dependencies, build output).
const excludedDirNames = new Set(["node_modules", "dist", ".vite", ".git"]);
// Specific third-party directories we don't own.
const excludedPaths = [
    path.join("bookEdit", "html", "font-awesome"), // third-party; linked only in the toolbox doc
    "Readium", // third-party epub reader assets
];

// The single permitted source of UI @font-face declarations.
const fontFacesFile = "bloomUIFontFaces.css";

// The only files allowed to include bloomUIFontFaces.css. Each is the one source of the
// declarations for a whole document: the three .less are compiled to css that is linked in a
// document's <head> at parse time, and uiFontFaces.ts injects them at runtime into documents
// that lack such a link (guarded so it never adds a duplicate).
const allowedIncluders = [
    path.join("bookEdit", "css", "editMode.less"),
    path.join("bookEdit", "toolbox", "toolbox.less"),
    path.join(
        "bookEdit",
        "toolbox",
        "readers",
        "readerSetup",
        "readerSetup.less",
    ),
    path.join("utils", "uiFontFaces.ts"),
];

// Files allowed to import utils/uiFontFaces (i.e. to install the faces at runtime). Every
// Bloom-managed document must end up with the UI faces; documents that neither link one of the
// stylesheets above nor mount a React root have to ask for them explicitly, and each such
// document's bundle root is listed here so that a new one is a deliberate decision rather than
// an oversight (BL-15300).
const allowedInstallCallers = [
    path.join("utils", "reactRender.tsx"), // covers every React-rooted document
    path.join("collectionsTab", "collectionsTabBookPane", "bookPreview.ts"), // the preview/thumbnail documents: no static link, no React root
];

function walk(dir: string, results: string[] = []): string[] {
    for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
        const fullPath = path.join(dir, entry.name);
        const relative = path.relative(browserUiRoot, fullPath);
        if (excludedPaths.some((ex) => relative.startsWith(ex))) continue;
        if (entry.isDirectory()) {
            if (excludedDirNames.has(entry.name)) continue;
            walk(fullPath, results);
        } else {
            results.push(fullPath);
        }
    }
    return results;
}

const isStyleFile = (file: string) =>
    file.endsWith(".less") || file.endsWith(".css");
const isScriptFile = (file: string) =>
    /\.(ts|tsx|js|jsx)$/.test(file) && !file.endsWith(".d.ts");

describe("UI @font-face single-source invariant (BL-15300)", () => {
    const allFiles = walk(browserUiRoot);

    it("finds the expected files (sanity check that the scan works)", () => {
        const relative = allFiles.map((f) => path.relative(browserUiRoot, f));
        expect(relative).toContain(fontFacesFile);
        for (const includer of allowedIncluders) {
            expect(relative).toContain(includer);
        }
    });

    it("declares UI @font-face rules only in bloomUIFontFaces.css", () => {
        // Look for actual declarations, not prose mentions of "@font-face" in comments.
        const declaration = /@font-face\s*\{/;
        const offenders = allFiles
            .filter(isStyleFile)
            .filter(
                (file) =>
                    path.relative(browserUiRoot, file) !== fontFacesFile &&
                    declaration.test(fs.readFileSync(file, "utf8")),
            )
            .map((file) => path.relative(browserUiRoot, file));
        expect(offenders).toEqual([]);
    });

    it("allows only the approved document roots to include bloomUIFontFaces.css", () => {
        const thisTestFile = path.relative(browserUiRoot, __filename);
        const includers = allFiles
            .filter((file) => isStyleFile(file) || isScriptFile(file))
            .filter(
                (file) =>
                    path.relative(browserUiRoot, file) !== fontFacesFile &&
                    path.relative(browserUiRoot, file) !== thisTestFile &&
                    fs.readFileSync(file, "utf8").includes("bloomUIFontFaces"),
            )
            .map((file) => path.relative(browserUiRoot, file));
        // Comments elsewhere may mention the file by name in prose; only count real
        // import/include references.
        const realIncluders = includers.filter((file) => {
            const text = fs.readFileSync(
                path.join(browserUiRoot, file),
                "utf8",
            );
            return /(@import|from\s+["']|import\s+["']).*bloomUIFontFaces/.test(
                text,
            );
        });
        expect(realIncluders.sort()).toEqual([...allowedIncluders].sort());
    });

    it("keeps the set of uiFontFaces importers to the documented list", () => {
        // The module itself, its own unit tests, and this file are not delivery points.
        const notDeliveryPoints = [
            path.join("utils", "uiFontFaces.ts"),
            path.join("utils", "uiFontFaces.spec.ts"),
            path.relative(browserUiRoot, __filename),
        ];
        // Match importing the MODULE rather than calling the function by name: that catches
        // namespace imports, `await import(...)`, and aliased named imports, any of which
        // would be a real fifth delivery point while a call-site-name scan looked right.
        // It also excludes prose: bloomMaterialUITheme.ts names the function in a comment
        // explaining why it must NOT install the faces, but imports nothing.
        const importsTheModule =
            /(?:from\s*|import\s*\(\s*)["'][^"']*\/uiFontFaces["']/;
        const importers = allFiles
            .filter(isScriptFile)
            .map((file) => path.relative(browserUiRoot, file))
            .filter(
                (file) =>
                    !notDeliveryPoints.includes(file) &&
                    importsTheModule.test(
                        fs.readFileSync(path.join(browserUiRoot, file), "utf8"),
                    ),
            );
        // Sanity check that the scan is actually finding importers, so that moving or renaming
        // the module can't turn this into a vacuously passing test.
        expect(importers.length).toBeGreaterThan(0);
        expect(importers.sort()).toEqual([...allowedInstallCallers].sort());
    });
});
