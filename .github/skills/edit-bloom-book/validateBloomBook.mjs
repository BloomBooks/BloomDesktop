/* eslint-env node */
/* global console, process */
import fs from "node:fs";
import path from "node:path";
import { createRequire } from "node:module";
import { fileURLToPath } from "node:url";

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);
const repoRoot = path.resolve(__dirname, "..", "..", "..");
const browserUIPackageJson = path.join(
    repoRoot,
    "src",
    "BloomBrowserUI",
    "package.json",
);

const loadJSDOM = () => {
    try {
        const browserUIRequire = createRequire(browserUIPackageJson);
        return browserUIRequire("jsdom").JSDOM;
    } catch (error) {
        const details = error instanceof Error ? error.message : String(error);
        console.error(
            "Failed to load jsdom from src/BloomBrowserUI. Run yarn in src/BloomBrowserUI if dependencies are missing.",
        );
        console.error(details);
        process.exitCode = 2;
        return undefined;
    }
};

const JSDOM = loadJSDOM();

const usage = () => {
    console.log(
        "Usage: node .github/skills/edit-bloom-book/validateBloomBook.mjs <Bloom.html-or-.htm> [more files]",
    );
};

const voidElements = new Set([
    "area",
    "base",
    "br",
    "col",
    "embed",
    "hr",
    "img",
    "input",
    "link",
    "meta",
    "param",
    "source",
    "track",
    "wbr",
]);

const getAttributeValue = (tagText, name) => {
    const match = tagText.match(
        new RegExp(`${name}\\s*=\\s*(?:"([^"]*)"|'([^']*)'|([^\\s>]+))`, "i"),
    );

    return match?.[1] ?? match?.[2] ?? match?.[3] ?? "";
};

const getBloomPagePlacementErrors = (source) => {
    const errors = [];
    const stack = [];
    const tagPattern =
        /<!--([\s\S]*?)-->|<!DOCTYPE[^>]*>|<\/?[A-Za-z][^<>]*?>/gi;
    let match;

    while ((match = tagPattern.exec(source)) !== null) {
        const tagText = match[0];
        if (
            tagText.startsWith("<!--") ||
            tagText.startsWith("<!DOCTYPE") ||
            tagText.startsWith("<!doctype")
        ) {
            continue;
        }

        const closingMatch = tagText.match(/^<\s*\/\s*([A-Za-z0-9:-]+)/);
        if (closingMatch) {
            const name = closingMatch[1].toLowerCase();
            const existingIndex = stack
                .map((item) => item.name)
                .lastIndexOf(name);
            if (existingIndex >= 0) {
                stack.length = existingIndex;
            }
            continue;
        }

        const openingMatch = tagText.match(/^<\s*([A-Za-z0-9:-]+)/);
        if (!openingMatch) {
            continue;
        }

        const name = openingMatch[1].toLowerCase();
        const classValue = getAttributeValue(tagText, "class");
        const isBloomPage = /(^|\s)bloom-page(\s|$)/.test(classValue);

        if (isBloomPage) {
            const parentName = stack.at(-1)?.name;
            if (parentName !== "body") {
                const pageId =
                    getAttributeValue(tagText, "id") || "(missing id)";
                errors.push(
                    `Bloom-page element not found at root level: ${pageId}`,
                );
            }
        }

        const selfClosing = /\/\s*>$/.test(tagText) || voidElements.has(name);
        if (!selfClosing) {
            stack.push({ name });
        }
    }

    return errors;
};

const normalizeElementName = (element) => {
    const id = element.getAttribute("id");
    return id
        ? `${element.tagName.toLowerCase()}#${id}`
        : element.tagName.toLowerCase();
};

const hasClass = (element, className) => element.classList.contains(className);

const getClassList = (element) =>
    [...element.classList].length > 0
        ? `.${[...element.classList].join(".")}`
        : "";

const describeElement = (element) => {
    const id = element.getAttribute("id")?.trim();
    return `${element.tagName.toLowerCase()}${id ? `#${id}` : ""}${getClassList(element)}`;
};

const directElementChildren = (element) =>
    [...element.children].filter((child) => child.nodeType === 1);

const closestWithClass = (element, className) =>
    element.closest(`.${className}`);

const recordDuplicateIds = (document, selector, errors) => {
    const seen = new Map();
    for (const element of document.querySelectorAll(selector)) {
        const id = element.getAttribute("id")?.trim() ?? "";
        if (!id) {
            continue;
        }

        if (seen.has(id)) {
            errors.push(
                `Duplicate id '${id}' found on ${normalizeElementName(seen.get(id))} and ${normalizeElementName(element)}.`,
            );
            continue;
        }

        seen.set(id, element);
    }
};

const validatePageShells = (pages, errors) => {
    for (const page of pages) {
        const pageName = describeElement(page);
        const marginBoxes = [...page.querySelectorAll(".marginBox")].filter(
            (marginBox) => closestWithClass(marginBox, "bloom-page") === page,
        );
        if (marginBoxes.length === 0) {
            errors.push(
                `${pageName} must contain a .marginBox page content wrapper.`,
            );
        }

        for (const child of directElementChildren(page)) {
            const isExpectedPageChild =
                hasClass(child, "pageLabel") ||
                hasClass(child, "pageDescription") ||
                hasClass(child, "marginBox") ||
                hasClass(child, "bloom-pageCoverColor") ||
                hasClass(child, "bloom-translationGroup");
            if (
                !isExpectedPageChild &&
                !child.tagName.match(/^(SCRIPT|STYLE)$/i)
            ) {
                errors.push(
                    `${describeElement(child)} is an unexpected direct child of ${pageName}; page content should normally be inside .marginBox.`,
                );
            }
        }
    }
};

const validateTranslationGroups = (document, errors) => {
    for (const group of document.querySelectorAll(
        "div.bloom-translationGroup",
    )) {
        const groupName = describeElement(group);
        if (closestWithClass(group.parentElement, "bloom-translationGroup")) {
            errors.push(
                `${groupName} must not be nested inside another .bloom-translationGroup.`,
            );
        }

        const directEditables = directElementChildren(group).filter(
            (child) =>
                (child.tagName.toLowerCase() === "div" &&
                    hasClass(child, "bloom-editable")) ||
                child.tagName.toLowerCase() === "textarea",
        );
        if (directEditables.length === 0) {
            errors.push(
                `${groupName} must contain at least one direct .bloom-editable or textarea child.`,
            );
        }

        const languages = new Map();
        for (const editable of directEditables) {
            const lang = editable.getAttribute("lang")?.trim() ?? "";
            if (!lang) {
                continue;
            }

            if (languages.has(lang)) {
                errors.push(
                    `${groupName} has duplicate direct editable children for lang='${lang}'.`,
                );
                continue;
            }

            languages.set(lang, editable);
        }
    }

    for (const editable of document.querySelectorAll(".bloom-editable")) {
        if (editable.tagName.toLowerCase() !== "div") {
            errors.push(
                `${describeElement(editable)} must be a div when using .bloom-editable.`,
            );
            continue;
        }

        const owningPage = closestWithClass(editable, "bloom-page");
        const parentGroup = editable.parentElement;
        if (
            owningPage &&
            (!parentGroup || !hasClass(parentGroup, "bloom-translationGroup"))
        ) {
            errors.push(
                `${describeElement(editable)} must be a direct child of a .bloom-translationGroup.`,
            );
        }

        // contenteditable="true" is common for editable text and required for
        // CKEditor attachment, but some valid generated/xMatter fields carry
        // bloom-editable/content classes while intentionally being non-editable.
    }
};

const validateSplitPanes = (document, errors) => {
    for (const splitPane of document.querySelectorAll("div.split-pane")) {
        const splitName = describeElement(splitPane);
        const isHorizontal = hasClass(splitPane, "horizontal-percent");
        const isVertical = hasClass(splitPane, "vertical-percent");
        if (isHorizontal === isVertical) {
            errors.push(
                `${splitName} must have exactly one orientation class: .horizontal-percent or .vertical-percent.`,
            );
        }

        const children = directElementChildren(splitPane);
        const components = children.filter((child) =>
            hasClass(child, "split-pane-component"),
        );
        const dividers = children.filter((child) =>
            hasClass(child, "split-pane-divider"),
        );
        const shims = children.filter((child) =>
            hasClass(child, "split-pane-resize-shim"),
        );

        if (components.length !== 2) {
            errors.push(
                `${splitName} must have exactly two direct .split-pane-component children.`,
            );
        }
        if (dividers.length !== 1) {
            errors.push(
                `${splitName} must have exactly one direct .split-pane-divider child.`,
            );
        }
        if (shims.length > 1) {
            errors.push(
                `${splitName} must not have more than one .split-pane-resize-shim child.`,
            );
        }

        for (const child of children) {
            const recognized =
                hasClass(child, "split-pane-component") ||
                hasClass(child, "split-pane-divider") ||
                hasClass(child, "split-pane-resize-shim");
            if (!recognized) {
                errors.push(
                    `${describeElement(child)} is an unexpected direct child of ${splitName}.`,
                );
            }
        }

        const expectedPositions = isHorizontal
            ? ["position-top", "position-bottom"]
            : ["position-left", "position-right"];
        for (const position of expectedPositions) {
            if (
                components.length > 0 &&
                components.filter((component) => hasClass(component, position))
                    .length !== 1
            ) {
                errors.push(
                    `${splitName} must have one .split-pane-component.${position}.`,
                );
            }
        }

        if (dividers.length === 1) {
            const expectedDivider = isHorizontal
                ? "horizontal-divider"
                : "vertical-divider";
            if (!hasClass(dividers[0], expectedDivider)) {
                errors.push(
                    `${describeElement(dividers[0])} must have .${expectedDivider}.`,
                );
            }
        }
    }

    for (const component of document.querySelectorAll(
        "div.split-pane-component",
    )) {
        if (
            hasClass(component, "marginBox") &&
            closestWithClass(component.parentElement, "bloom-page")
        ) {
            continue;
        }

        const directInners = directElementChildren(component).filter((child) =>
            hasClass(child, "split-pane-component-inner"),
        );
        const directNestedSplitPanes = directElementChildren(component).filter(
            (child) => hasClass(child, "split-pane"),
        );
        if (
            directInners.length + directNestedSplitPanes.length !== 1 ||
            directInners.length > 1 ||
            directNestedSplitPanes.length > 1
        ) {
            errors.push(
                `${describeElement(component)} must have exactly one direct .split-pane-component-inner child or one direct nested .split-pane child.`,
            );
        }

        if (
            !component.parentElement ||
            !hasClass(component.parentElement, "split-pane")
        ) {
            errors.push(
                `${describeElement(component)} must be a direct child of .split-pane.`,
            );
        }
    }
};

const validateCanvasAndImages = (document, errors) => {
    for (const canvas of document.querySelectorAll("div.bloom-canvas")) {
        const canvasName = describeElement(canvas);
        const owningPage = closestWithClass(canvas, "bloom-page");
        if (!owningPage) {
            errors.push(`${canvasName} must be inside a .bloom-page.`);
        }

        const directImages = directElementChildren(canvas).filter(
            (child) => child.tagName.toLowerCase() === "img",
        );
        const directCanvasElements = directElementChildren(canvas).filter(
            (child) => hasClass(child, "bloom-canvas-element"),
        );
        const hasLegacyImage = directImages.length > 0;
        const hasCanvasElementImage = directCanvasElements.some((element) =>
            element.querySelector(":scope > .bloom-imageContainer > img"),
        );

        if (
            !hasLegacyImage &&
            !hasCanvasElementImage &&
            directCanvasElements.length === 0
        ) {
            errors.push(
                `${canvasName} must contain either legacy direct image content or .bloom-canvas-element children.`,
            );
        }

        if (
            hasClass(canvas, "bloom-has-canvas-element") &&
            directCanvasElements.length === 0
        ) {
            errors.push(
                `${canvasName} has .bloom-has-canvas-element but no direct .bloom-canvas-element child.`,
            );
        }
    }

    for (const canvasElement of document.querySelectorAll(
        "div.bloom-canvas-element",
    )) {
        if (
            !canvasElement.parentElement ||
            !hasClass(canvasElement.parentElement, "bloom-canvas")
        ) {
            errors.push(
                `${describeElement(canvasElement)} must be a direct child of .bloom-canvas.`,
            );
        }

        const directImageContainers = directElementChildren(
            canvasElement,
        ).filter((child) => hasClass(child, "bloom-imageContainer"));
        const hasText = !!canvasElement.querySelector(
            ":scope > .bloom-translationGroup, :scope > .bloom-editable",
        );
        const hasVideo = !!canvasElement.querySelector(
            ":scope > .bloom-videoContainer",
        );
        if (directImageContainers.length === 0 && !hasText && !hasVideo) {
            errors.push(
                `${describeElement(canvasElement)} must directly contain image, text, or video content.`,
            );
        }

        for (const imageContainer of directImageContainers) {
            const directImages = directElementChildren(imageContainer).filter(
                (child) => child.tagName.toLowerCase() === "img",
            );
            if (directImages.length !== 1) {
                errors.push(
                    `${describeElement(imageContainer)} must have exactly one direct img child.`,
                );
            }
        }
    }

    for (const imageContainer of document.querySelectorAll(
        "div.bloom-imageContainer",
    )) {
        const parent = imageContainer.parentElement;
        if (!parent || !hasClass(parent, "bloom-canvas-element")) {
            errors.push(
                `${describeElement(imageContainer)} must be a direct child of .bloom-canvas-element.`,
            );
        }
    }
};

const validatePagePlacement = (document, source, errors) => {
    const allPages = [...document.querySelectorAll("div.bloom-page")];
    if (allPages.length === 0) {
        errors.push("Must have at least one .bloom-page.");
        return allPages;
    }

    errors.push(...getBloomPagePlacementErrors(source));

    for (const page of allPages) {
        if (page.parentElement?.tagName !== "BODY") {
            const pageId = page.getAttribute("id")?.trim() || "(missing id)";
            errors.push(
                `Bloom-page element not found at root level: ${pageId}`,
            );
        }
    }

    return allPages;
};

const validatePageIds = (pages, errors) => {
    const seen = new Map();
    for (const page of pages) {
        const id = page.getAttribute("id")?.trim() ?? "";
        if (!id) {
            errors.push("Each .bloom-page must have a non-empty id.");
            continue;
        }

        if (/[\s"'=<>]/.test(id)) {
            errors.push(
                `.bloom-page id '${id}' contains invalid id characters.`,
            );
        }

        if (seen.has(id)) {
            errors.push(`Duplicate .bloom-page id '${id}'.`);
            continue;
        }

        seen.set(id, page);
    }
};

const validateEditables = (document, errors) => {
    for (const editable of document.querySelectorAll("div.bloom-editable")) {
        const lang = editable.getAttribute("lang")?.trim() ?? "";
        if (!lang) {
            errors.push(
                `${normalizeElementName(editable)} is missing a non-empty lang attribute.`,
            );
        }
    }
};

const validateBook = (filePath) => {
    const absolutePath = path.resolve(filePath);
    const errors = [];

    if (!fs.existsSync(absolutePath)) {
        return {
            filePath: absolutePath,
            errors: [`File not found: ${absolutePath}`],
        };
    }

    const source = fs.readFileSync(absolutePath, "utf8");
    let document;
    try {
        document = new JSDOM(source).window.document;
    } catch (error) {
        const details = error instanceof Error ? error.message : String(error);
        return {
            filePath: absolutePath,
            errors: [`HTML parse failure: ${details}`],
        };
    }

    const html = document.documentElement;
    if (!html || html.tagName?.toLowerCase() !== "html") {
        errors.push("Document root must be <html>.");
    }

    const body = document.querySelector("body");
    if (!body) {
        errors.push("Document must contain a <body>.");
    }

    const bloomFormatVersion = document.querySelector(
        'meta[name="BloomFormatVersion"]',
    );
    if (!bloomFormatVersion) {
        errors.push('Missing <meta name="BloomFormatVersion">.');
    }

    const bloomDataDiv = document.getElementById("bloomDataDiv");
    if (!bloomDataDiv) {
        errors.push("Missing #bloomDataDiv.");
    }

    const pages = validatePagePlacement(document, source, errors);
    validatePageIds(pages, errors);
    validatePageShells(pages, errors);
    validateEditables(document, errors);
    validateTranslationGroups(document, errors);
    validateSplitPanes(document, errors);
    validateCanvasAndImages(document, errors);
    recordDuplicateIds(document, "textarea[id]", errors);
    recordDuplicateIds(document, "p[id]", errors);
    recordDuplicateIds(document, "img[id]", errors);

    return { filePath: absolutePath, errors };
};

const main = (argv) => {
    if (!JSDOM) {
        return 2;
    }

    const args = argv.slice(2);
    if (args.length === 0 || args.includes("--help") || args.includes("-h")) {
        usage();
        return args.length === 0 ? 1 : 0;
    }

    let hasErrors = false;
    for (const filePath of args) {
        const result = validateBook(filePath);
        if (result.errors.length === 0) {
            console.log(`OK  ${result.filePath}`);
            continue;
        }

        hasErrors = true;
        console.log(`FAIL ${result.filePath}`);
        for (const error of result.errors) {
            console.log(`  - ${error}`);
        }
    }

    return hasErrors ? 1 : 0;
};

const invokedDirectly =
    typeof process !== "undefined" &&
    typeof process.argv?.[1] === "string" &&
    path.resolve(process.argv[1]) === __filename;

if (invokedDirectly) {
    process.exitCode = main(process.argv);
}

export { validateBook, main };
