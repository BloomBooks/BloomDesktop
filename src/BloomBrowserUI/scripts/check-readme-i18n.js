#!/usr/bin/env node
/**
 * Checks that the readmes of the templates we ship are fully internationalized.
 *
 * Template readmes are localized as whole files: each `ReadMe-en.htm` is turned into a
 * `ReadMe-en.xlf` by `HtmlXliff.exe --extract` (see l10n-build.js), that XLIFF goes to Crowdin,
 * and the translations come back and are injected to produce `ReadMe-<lang>.htm`.
 *
 * The weak point is that the extractor only picks up blocks that carry an `i18n` attribute,
 * which comes from an `{i18n="some.id"}` annotation on the block in the markdown source. Forget
 * that annotation and the text is silently skipped at every stage: it never reaches the XLIFF,
 * never reaches Crowdin, never gets translated, and regenerating the XLIFF produces no diff --
 * so nothing reports it. It just stays English forever, in every language. This script is what
 * reports it.
 *
 * Four checks, all mechanical:
 *   1. Text in ReadMe-en.htm that no `i18n` attribute covers -> it will never be translated.
 *   2. A block with an `i18n` id and real text, but no matching trans-unit in ReadMe-en.xlf
 *      -> extraction dropped it.
 *   3. One id on two blocks whose text differs -> one of them will show the other's
 *      translation (sharing an id for identical text is fine, and is used deliberately).
 *   4. A trans-unit in ReadMe-en.xlf with no matching block in the HTML, and not marked obsolete
 *      -> a stale unit still being offered to translators.
 *
 * Usage: node scripts/check-readme-i18n.js [--strict]
 *   --strict  exit non-zero if anything is reported (for CI); otherwise warn only.
 */

const { glob } = require("glob");
const fs = require("fs");
const path = require("path");
const { JSDOM } = require("jsdom");

// Elements whose text is not prose for a translator. CODE/PRE/KBD/SAMP/VAR matter here
// because the readmes deliberately show literal markdown and file names in backticks.
const kNonProseTags = new Set([
    "SCRIPT",
    "STYLE",
    "HEAD",
    "TITLE",
    "CODE",
    "PRE",
    "KBD",
    "SAMP",
    "VAR",
]);

/**
 * Whether a piece of text is something a translator would actually translate. Numbers, symbols
 * and lone punctuation are not -- the Leveled Reader readme, for instance, has a table of stage
 * and level numbers, and flagging every cell of it would bury the real findings.
 */
function isTranslatableText(text) {
    return /\p{L}/u.test(text);
}

/**
 * The English XLIFF that l10n-build.js generates for a given readme.
 */
function xliffPathForReadme(htmPath) {
    const templateName = path.basename(path.dirname(htmPath));
    return path.join(
        "../../DistFiles/localization",
        templateName,
        "ReadMe-en.xlf",
    );
}

/**
 * Ids of the trans-units in an English readme XLIFF, split by whether the unit is marked
 * obsolete. We deliberately keep obsolete units rather than deleting them (see
 * DistFiles/localization/README.md), so they must not be reported as stale.
 */
function readXliffUnitIds(xliffPath) {
    const live = new Set();
    const obsolete = new Set();
    if (!fs.existsSync(xliffPath)) return { live, obsolete, missing: true };
    const xml = fs.readFileSync(xliffPath, "utf8");
    const unitRegex = /<trans-unit\b[^>]*\bid="([^"]*)"[\s\S]*?<\/trans-unit>/g;
    let match;
    while ((match = unitRegex.exec(xml)) !== null) {
        if (/<note>\s*Obsolete\b/i.test(match[0])) obsolete.add(match[1]);
        else live.add(match[1]);
    }
    return { live, obsolete, missing: false };
}

/**
 * Every text node in the body that a translator would need, paired with the nearest ancestor
 * carrying an `i18n` attribute (null when nothing covers it).
 */
function collectTextNodes(document) {
    const results = [];
    const walk = (node) => {
        for (const child of node.childNodes) {
            if (child.nodeType === 3) {
                const text = child.textContent.trim();
                if (text && isTranslatableText(text))
                    results.push({ text, node });
                continue;
            }
            if (child.nodeType !== 1) continue;
            if (kNonProseTags.has(child.tagName)) continue;
            walk(child);
        }
    };
    walk(document.body);
    return results.map((r) => {
        let el = r.node;
        while (el && el.nodeType === 1) {
            if (el.hasAttribute && el.hasAttribute("i18n"))
                return { text: r.text, id: el.getAttribute("i18n") };
            el = el.parentNode;
        }
        return { text: r.text, id: null };
    });
}

function shorten(text) {
    const oneLine = text.replace(/\s+/g, " ");
    return oneLine.length > 70 ? oneLine.slice(0, 70) + "..." : oneLine;
}

function checkReadme(htmPath) {
    const problems = [];
    const html = fs.readFileSync(htmPath, "utf8");
    const { document } = new JSDOM(html).window;
    const texts = collectTextNodes(document);

    const xliffPath = xliffPathForReadme(htmPath);
    const { live, obsolete, missing } = readXliffUnitIds(xliffPath);
    if (missing) {
        problems.push(
            `no English XLIFF at ${xliffPath} -- nothing in this readme is being translated`,
        );
        return problems;
    }

    // 1. Text that no i18n attribute covers.
    for (const t of texts) {
        if (t.id === null)
            problems.push(
                `text has no i18n id, so it will never be translated: "${shorten(t.text)}"\n` +
                    `      Fix: add an {i18n="some.unique.id"} annotation to that block in the ReadMe-en.md source.`,
            );
    }

    // 2. Ids present in the HTML with real text, but no trans-unit for them.
    const idsWithText = new Set(texts.filter((t) => t.id).map((t) => t.id));
    for (const id of idsWithText) {
        if (!live.has(id) && !obsolete.has(id))
            problems.push(
                `i18n id "${id}" has text in the readme but no trans-unit in ReadMe-en.xlf -- extraction dropped it`,
            );
    }

    // 3. One id used by two blocks whose text differs. Sharing an id between blocks that say
    // the same thing is deliberate and useful -- the Leveled Reader's "n/a" cells do it, so the
    // string is translated once -- but the XLIFF holds one source per id, so if the texts differ
    // one block silently ends up displaying the other's translation.
    const textsById = new Map();
    for (const el of document.querySelectorAll("[i18n]")) {
        const text = el.textContent.replace(/\s+/g, " ").trim();
        if (!text) continue;
        const id = el.getAttribute("i18n");
        if (!textsById.has(id)) textsById.set(id, new Set());
        textsById.get(id).add(text);
    }
    for (const [id, texts] of textsById) {
        if (texts.size > 1)
            problems.push(
                `i18n id "${id}" is used by ${texts.size} blocks with different text, so all but ` +
                    `one will show the wrong translation -- give them separate ids: ` +
                    Array.from(texts)
                        .map((t) => `"${shorten(t)}"`)
                        .join(" vs "),
            );
    }

    // 4. Live trans-units that no longer correspond to anything in the HTML.
    const allHtmlIds = new Set(
        Array.from(document.querySelectorAll("[i18n]")).map((e) =>
            e.getAttribute("i18n"),
        ),
    );
    for (const id of live) {
        if (!allHtmlIds.has(id))
            problems.push(
                `trans-unit "${id}" is in ReadMe-en.xlf but not in the readme -- either restore it ` +
                    `or mark it obsolete with a <note>Obsolete as of X.Y</note>`,
            );
    }

    return problems;
}

function main() {
    const strict = process.argv.includes("--strict");
    const readmes = glob
        .sync("../../output/browser/templates/**/ReadMe-en.htm")
        .sort();

    if (readmes.length === 0) {
        console.error(
            "No ReadMe-en.htm found under output/browser/templates. Run the content/markdown " +
                "build first (this checks the generated readmes, which is what the extractor sees).",
        );
        process.exit(strict ? 1 : 0);
    }

    console.log(`Checking i18n of ${readmes.length} template readmes`);
    let total = 0;
    for (const htmPath of readmes) {
        const problems = checkReadme(htmPath);
        const name = path.basename(path.dirname(htmPath));
        if (problems.length === 0) {
            console.log(`  ok  ${name}`);
            continue;
        }
        total += problems.length;
        console.log(`  !!  ${name}`);
        for (const p of problems) console.log(`      - ${p}`);
    }

    if (total === 0) {
        console.log("\nAll template readmes are fully internationalized.");
        return;
    }
    console.log(
        `\n${total} problem(s) found. These strings will never reach Crowdin, so they stay ` +
            `English in every language.`,
    );
    if (strict) process.exit(1);
}

main();
