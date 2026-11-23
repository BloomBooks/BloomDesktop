// Changing which languages a translation group shows after the page has loaded.
//
// When a page loads, C# gives each translation group a box (bloom-editable) for each of the book's
// languages, and SetupElements sets each box up. A control that later changes the languages a group
// shows (the Language submenu of a canvas text box, the Languages submenu of a table's text cell)
// may find a box missing; showLanguagesInTranslationGroup adds it, and setUpBoxesBuiltAfterLoad
// gives it what page load gives every box. They are exported from the page bundle, because the
// canvas submenu runs in the toolbox frame.
//
// A box added or shown here should look as it will once the page is next loaded, when C#
// (TranslationGroupManager) works out every group again: languagesShownBySetting and
// languageClassesOf give C#'s rules for which languages a group shows and which classes a box in
// each language gets, and they must change when those rules do. They cover the groups whose
// languages are added to after load (text in tables and on canvases), not the ones whose
// visibility the Appearance system controls (data-visibility-variable, on cover fields) or whose
// setting names a branding preset ("[CoverCreditsLanguage] DEFAULT:V,N1"); C# alone handles those.

import $ from "jquery";
import BloomField from "../bloomField/BloomField";
import {
    activateLongPressFor,
    addEditKeyHandlersToBoxes,
    addLanguageTagsToBoxes,
    addPlaceholdersFromLabels,
    addPlainTextPasteHandler,
    attachToCkEditor,
    configureElementsForTools,
    keepDataTextUpToDate,
    matchGroupFontSizesToLanguage1,
} from "./bloomEditing";
import { setupInlineImages } from "./inlineImages";

/**
 * The roles (or language tags) a value of data-default-languages lists, in the order written, with
 * CL2 and CL3 given as N1 and N2, the names they stand for. As ProcessLanguageList in C# does, it
 * splits at commas and spaces only.
 */
export function rolesInSetting(setting: string | null): string[] {
    return (setting ?? "")
        .split(/[, ]+/)
        .filter((token) => token.length > 0)
        .map((token) => kSynonymsOfRoles[token] ?? token);
}

// Roles that are other names for older ones: CL2 and CL3 name the collection's second and third
// languages, as N1 and N2 do.
const kSynonymsOfRoles: Record<string, string> = { CL2: "N1", CL3: "N2" };

// The languages C# works out a page's groups by, from the settings it injects (GetSettings). (The
// names of GetSettings's fields are older: currentCollectionLanguage2 and 3 are the toolbar's.)
function pageLanguages(): {
    toolbar1: string;
    toolbar2?: string;
    toolbar3?: string;
    collection1: string;
    metadata1?: string;
    metadata2?: string;
} {
    const settings = GetSettings();
    return {
        toolbar1: settings.languageForNewTextBoxes,
        toolbar2: settings.currentCollectionLanguage2,
        toolbar3: settings.currentCollectionLanguage3,
        collection1: settings.collectionLanguage1,
        metadata1: settings.metadataLanguage1,
        metadata2: settings.metadataLanguage2,
    };
}

/**
 * The collection's languages, first to third (those it has), by tag: the languages CL1, N1 and N2
 * name. Bloom's settings for the page hold them, so they are known as soon as it loads.
 */
export function collectionLanguageTags(): string[] {
    return languagesShownBySetting("CL1,N1,N2");
}

/**
 * The languages a translation group with `setting` as its data-default-languages shows, as C#
 * decides at page load (TranslationGroupManager.ShouldNormallyShowEditable, after
 * ProcessLanguageList): no setting or "auto" shows the toolbar's languages; V and L1 its first,
 * L2 and L3 its second and third; CL1 the collection's first; N1 (or CL2) and N2 (or CL3) the
 * collection's second and third; and anything else is taken as a language tag.
 */
export function languagesShownBySetting(setting: string | null): string[] {
    const languages = pageLanguages();
    const tokens = rolesInSetting(setting);
    if (tokens.length === 0 || tokens[0].toLowerCase() === "auto") {
        return [
            languages.toolbar1,
            languages.toolbar2,
            languages.toolbar3,
        ].filter((tag): tag is string => !!tag);
    }
    const tagOfRole: Record<string, string | undefined> = {
        V: languages.toolbar1,
        L1: languages.toolbar1,
        L2: languages.toolbar2,
        L3: languages.toolbar3,
        CL1: languages.collection1,
        N1: languages.metadata1,
        N2: languages.metadata2,
    };
    return tokens
        .map((token) =>
            Object.hasOwn(tagOfRole, token) ? tagOfRole[token] : token,
        )
        .filter((tag): tag is string => !!tag);
}

/**
 * The classes C# gives a box in language `tag` at page load
 * (TranslationGroupManager.UpdateContentLanguageClassesOnElement): bloom-content1, 2 or 3 for the
 * toolbar's languages, which also set the order the boxes read down the group; and
 * bloom-contentNational1 and 2 and bloom-contentCL1 for the collection's languages. The
 * appearance and custom-layout classes (bloom-contentFirst and the like) are not among them.
 */
export function languageClassesOf(tag: string): string[] {
    const languages = pageLanguages();
    const classes: string[] = [];
    if (tag === languages.toolbar1) classes.push("bloom-content1");
    else if (tag === languages.toolbar2) classes.push("bloom-content2");
    else if (tag === languages.toolbar3) classes.push("bloom-content3");
    if (tag === languages.metadata1) classes.push("bloom-contentNational1");
    if (tag === languages.toolbar3 || tag === languages.metadata2)
        classes.push("bloom-contentNational2");
    if (tag === languages.collection1) classes.push("bloom-contentCL1");
    return classes;
}

/**
 * Add to `box` the language classes C# would give it (see languageClassesOf). Its callers have
 * already taken every bloom-content class off the box.
 */
export function addLanguageClassesToBox(box: HTMLElement): void {
    languageClassesOf(box.getAttribute("lang") ?? "").forEach((name) =>
        box.classList.add(name),
    );
}

/**
 * Make `group` show the boxes of the languages `tags` and hide its others, adding a box for any of
 * those languages it has none for. A box is added among the others in the order of `order` (tags
 * in the order the languages should read down the group); a language not in `order` goes last.
 * Returns the boxes added and the boxes that were there but hidden and are now shown; both go to
 * setUpBoxesBuiltAfterLoad once the boxes are where they belong.
 *
 * Nothing is measured here, so that a caller changing many groups (a table menu changes every
 * cell) can pass the boxes of all of them to setUpBoxesBuiltAfterLoad at once: setting up or
 * tagging a box measures it, and measuring after every group's change would make the browser lay
 * out the page once per group.
 */
export function showLanguagesInTranslationGroup(
    group: HTMLElement,
    tags: string[],
    order: string[] = [],
): { added: HTMLElement[]; shown: HTMLElement[] } {
    const added = [...new Set(tags)]
        .filter((tag) => !boxInLanguage(group, tag))
        .map((tag) => addBox(group, tag, order));
    const shown: HTMLElement[] = [];
    boxesOf(group).forEach((box) => {
        const wanted = tags.includes(box.getAttribute("lang") ?? "")
            ? "bloom-visibility-code-on"
            : "bloom-visibility-code-off";
        const visibilityClasses = Array.from(box.classList).filter((name) =>
            name.startsWith("bloom-visibility-code-"),
        );
        // Classes are changed only when they are wrong: a table menu calls this for every cell,
        // and each change makes the browser work out the page's styles again.
        if (visibilityClasses.length === 1 && visibilityClasses[0] === wanted)
            return;
        if (wanted === "bloom-visibility-code-on" && !added.includes(box))
            shown.push(box);
        visibilityClasses.forEach((name) => box.classList.remove(name));
        box.classList.add(wanted);
    });
    return { added, shown };
}

/**
 * Give boxes built since the page loaded what page load (SetupElements and bootstrap in
 * bloomEditing.ts) gives every box of a translation group, through the same helpers and in the
 * same order. A step added to one belongs in the other.
 *
 * Two things differ from page load. dir="rtl" is set here from the collection's stylesheet, which
 * C# makes from the same setting it uses to set dir at load; the Format dialog's alignment and the
 * paragraph marks of editMode.less go by that attribute. And CKEditor is attached to every box
 * given, without page load's ckeditableSelector, which a box in a language that is none of the
 * collection's or the toolbar's does not match; the canvas does the same for its new elements
 * (CanvasElementManager.addEventsToFocusableElements).
 *
 * Not done here: overflow checking. Page load gives a page's boxes overflow handlers only when the
 * page has at most 30 of them (OverflowChecker.AddOverflowHandlers), and a box added later cannot
 * join that without counting again.
 *
 * `boxesToTagAgain` are boxes already set up that need only their language tags worked out again:
 * those showLanguagesInTranslationGroup has shown again (a hidden box gets no tag, since
 * AddLanguageTags skips a box too narrow to hold one), or boxes whose width may have changed. They
 * are tagged with the new boxes, so the page is laid out once to measure them all.
 */
export function setUpBoxesBuiltAfterLoad(
    boxes: HTMLElement[],
    boxesToTagAgain: HTMLElement[] = [],
): void {
    const groups = Array.from(
        new Set(
            boxes
                .map((box) =>
                    box.closest<HTMLElement>(".bloom-translationGroup"),
                )
                .filter((group): group is HTMLElement => !!group),
        ),
    );
    const $boxes = $(boxes);
    // Before ManageField and CKEditor, as at page load.
    groups.forEach((group) => setupInlineImages(group));
    // Every box's direction is read before any box is written to, so the styles are worked out
    // once.
    const rightToLeft = new Set(
        boxes.filter((box) => getComputedStyle(box).direction === "rtl"),
    );
    boxes.forEach((box) => {
        if (rightToLeft.has(box)) box.setAttribute("dir", "rtl");
        else box.removeAttribute("dir");
        BloomField.ManageField(box);
    });
    // The boxes, not their groups, whose other boxes already have the tools' handlers.
    configureElementsForTools(boxes);
    keepDataTextUpToDate(
        $boxes.filter(
            ".bloom-translationGroup.bloom-text-for-css .bloom-editable",
        ),
    );
    matchGroupFontSizesToLanguage1(groups);
    addPlainTextPasteHandler($boxes);
    addEditKeyHandlersToBoxes($boxes);
    groups.forEach((group) =>
        addPlaceholdersFromLabels($(group).children("label.placeholder")),
    );
    // Must come before attachToCkEditor: CKEditor's inline mode decides whether the editor is
    // read-only from the element's contenteditable state at the moment it attaches.
    boxes.forEach((box) => box.setAttribute("contenteditable", "true"));
    addLanguageTagsToBoxes($([...boxes, ...boxesToTagAgain]));
    boxes.forEach((box) => attachToCkEditor(box));
    // After CKEditor, which can replace the boxes' nodes, as at page load.
    activateLongPressFor($boxes);
}

function boxesOf(group: HTMLElement): HTMLElement[] {
    return Array.from(
        group.querySelectorAll<HTMLElement>(":scope > .bloom-editable"),
    );
}

function boxInLanguage(
    group: HTMLElement,
    tag: string,
): HTMLElement | undefined {
    return boxesOf(group).find((box) => box.getAttribute("lang") === tag);
}

// A new, empty box in language `tag`, made from one of the group's boxes so it keeps what every
// box of the group shares (its style, its data-book), without what belongs to that box's language
// or to its editing. The group's "z" box, the template that is never edited, is preferred.
function addBox(group: HTMLElement, tag: string, order: string[]): HTMLElement {
    const boxes = boxesOf(group);
    const source =
        boxes.find((box) => box.getAttribute("lang") === "z") ?? boxes[0];
    const box = source.cloneNode(false) as HTMLElement;
    for (const attribute of Array.from(box.attributes)) {
        if (kAttributesNotCopied.test(attribute.name))
            box.removeAttribute(attribute.name);
    }
    Array.from(box.classList)
        .filter((name) => kClassesNotCopied.test(name))
        .forEach((name) => box.classList.remove(name));
    box.setAttribute("lang", tag);
    addLanguageClassesToBox(box);
    box.innerHTML = "<p></p>";
    const later = order
        .slice(order.indexOf(tag) + 1)
        .map((other) => boxInLanguage(group, other))
        .find((other) => other !== undefined);
    // A language not in `order` gives indexOf -1, so every language is "later" than it; it goes
    // last instead.
    group.insertBefore(box, order.includes(tag) ? (later ?? null) : null);
    return box;
}

// What a box carries that belongs to its own language or to its editing session (CKEditor adds
// role, aria-*, title, spellcheck and tabindex), and so is not copied into a new box. That
// includes its Talking Book recording, which the new box does not have: the same attributes and
// classes that C# leaves out when it makes a box from another at load
// (TranslationGroupManager.MakeElementWithLanguageForOneGroup), and ui-audioCurrent, the mark an
// older Bloom left on the box being recorded.
const kAttributesNotCopied =
    /^(id|lang|dir|contenteditable|data-languagetipcontent|role|aria-.*|title|spellcheck|tabindex|data-cke-.*|data-duration|data-audiorecordingmode|data-audiorecordingendtimes)$/i;
const kClassesNotCopied =
    /^(bloom-content.*|cke_.*|audio-sentence|bloom-postAudioSplit|ui-audioCurrent)$/;
