// Changing which languages a translation group shows after the page has loaded.
//
// When a page loads, C# gives each translation group a box (bloom-editable) for each of the book's
// languages, and SetupElements sets each box up. A control that later changes the languages a group
// shows (the Language submenu of a canvas text box, the Languages submenu of a table's text cell)
// may find a box missing; showLanguagesInTranslationGroup adds it, and setUpBoxesBuiltAfterLoad
// gives it what page load gives every box. Both are exported from the page bundle, because the
// canvas submenu runs in the toolbox frame.

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
 * given, without page load's ckeditableSelector, which a box added after load does not match
 * until C# gives it its bloom-content class; the canvas does the same for its new elements
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
