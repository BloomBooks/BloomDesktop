#!/usr/bin/env node
// Type-check gate.
//
// Runs the TypeScript native compiler (tsgo, from @typescript/native-preview) in
// --noEmit mode and fails ONLY on a curated set of "this can't be intentional"
// error codes: grammar/syntax mistakes, missing names/modules, and argument-count
// mismatches. These are the blunders that ship real runtime bugs. BL-16448 is the
// motivating example: a required parameter followed an optional one (TS1016) and
// the call site silently bound arguments to the wrong parameters (TS2554),
// producing an "x is not a function" toast at runtime.
//
// We use tsgo rather than stock tsc because it runs this project in ~3s with no
// incremental cache (vs ~13-17s cold for tsc), which keeps the pre-commit hook
// cheap. tsgo is a preview and applies some strict checks this tsconfig doesn't
// enable (extra implicit-any/strict-property diagnostics), but that doesn't
// affect us: the gate keys on the blunder-class codes below, which tsgo and tsc
// report identically (same codes, same message format).
//
// We deliberately IGNORE the strictness/assignability codes (TS2339 property
// access, TS2532/TS18048 possibly-undefined, TS2345/TS2322 not-assignable, ...).
// The codebase has ~60 pre-existing errors of those kinds, mostly obscure
// jQuery / React-generics / never[] typing issues. We don't want to force devs
// to fix those, nor to inherit them merely for editing a nearby line. So there
// is intentionally NO baseline file to maintain: the gate is defined entirely by
// the small, stable code list below, and it has zero pre-existing matches.
//
// Why this exists at all: the Vite/esbuild build strips types without checking
// them, and ESLint runs lint rules rather than the compiler, so nothing else in
// our pipeline catches these.

const { spawnSync } = require("child_process");
const path = require("path");

const browserUIRoot = path.resolve(__dirname, "..");

// The tsgo binary, as a path relative to browserUIRoot (kept relative so a space
// in the absolute repo path can't break the Windows shell invocation below).
const tsgoBin =
    process.platform === "win32"
        ? "node_modules\\.bin\\tsgo.cmd"
        : "node_modules/.bin/tsgo";

// Non-grammar error codes the gate fails on. Each is a structural mistake that
// is virtually never a deliberate or hard-to-fix "typing" issue:
//   TS2304          cannot find name
//   TS2305          module has no exported member
//   TS2307          cannot find module
//   TS2552          cannot find name; did you mean ...
//   TS2554/5/6      wrong number of arguments
//   TS2724          module has no exported member named ...; did you mean ...
// All TS1### codes (grammar/syntax, incl. TS1016) also fail; see isGrammarCode.
// To catch another structural code, add it here and fix anything it surfaces.
const FAIL_CODES = new Set([
    "TS2304",
    "TS2305",
    "TS2307",
    "TS2552",
    "TS2554",
    "TS2555",
    "TS2556",
    "TS2724",
]);

// TS1016 etc. — the grammar/syntax family is exactly TS1 followed by 3 digits.
// (Note this excludes 5-digit strictness codes like TS18048.)
const isGrammarCode = (code) => /^TS1\d{3}$/.test(code);

// Run tsgo. `--pretty false` emits one stable line per diagnostic for easy parsing.
function runTypeChecker() {
    const result = spawnSync(
        tsgoBin,
        ["--noEmit", "--pretty", "false", "-p", "tsconfig.json"],
        {
            cwd: browserUIRoot,
            encoding: "utf8",
            maxBuffer: 64 * 1024 * 1024,
            // node_modules/.bin/tsgo.cmd is a batch shim, which needs a shell on
            // Windows. The command path is relative (no spaces), so this is safe.
            shell: process.platform === "win32",
        },
    );
    if (result.error) {
        throw result.error;
    }
    return (result.stdout || "") + (result.stderr || "");
}

// Matches "path/to/file.ts(12,34): error TS1016: message".
const DIAGNOSTIC = /^(.+?)\((\d+),(\d+)\): error (TS\d+): (.+)$/;

function main() {
    const output = runTypeChecker();
    const failures = [];
    for (const rawLine of output.split(/\r?\n/)) {
        const match = DIAGNOSTIC.exec(rawLine.trim());
        if (!match) {
            continue;
        }
        const file = match[1].replace(/\\/g, "/");
        if (file.includes("node_modules/")) {
            continue;
        }
        const code = match[4];
        if (!FAIL_CODES.has(code) && !isGrammarCode(code)) {
            continue;
        }
        failures.push({
            file,
            line: match[2],
            col: match[3],
            code,
            message: match[5],
        });
    }

    if (failures.length === 0) {
        console.log("Type check passed: no blunder-class type errors.");
        return;
    }

    console.error(
        "Type errors that are almost certainly real bugs (not the tolerated\n" +
            "strictness/assignability kind) — these must be fixed:\n",
    );
    for (const failure of failures) {
        console.error(
            `  ${failure.file}:${failure.line}:${failure.col}\n    ${failure.code}: ${failure.message}\n`,
        );
    }
    console.error(
        "These are grammar / missing-name / argument-count errors. Fix them.\n" +
            "(The gate intentionally ignores assignability/strictness errors; see\n" +
            "the comment at the top of scripts/typecheck.js for the rationale.)\n",
    );
    process.exit(1);
}

main();
