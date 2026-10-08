import { describe, it, expect, beforeEach, afterEach } from "vitest";
import { getTextContextMenuContent } from "./textContextMenuContent";
import {
    getInlineImageInEditable,
    insertInlineImage,
    kInlineImageSelectedClass,
} from "../js/inlineImages";

// These tests check which commands a right-click puts on the text context menu. The commands
// come from two places, the paragraph command ("No Indent") and the inline image commands, so
// many of the tests check that a command is left out where it does not apply.

// An ordinary text box with an English editable of two paragraphs and a French editable, and a
// canvas element that also contains text, so we can check which right-clicks open the menu.
// This is the same page as in noIndentSpec.ts, plus the data-page-id that the inline image
// undo code uses to tell pages apart.
function setupPage(): void {
    document.body.innerHTML = `
        <div class="bloom-page" data-page-id="text-context-menu-test">
            <div class="bloom-translationGroup normal-style" id="plainGroup">
                <div class="bloom-editable normal-style bloom-content1 bloom-visibility-code-on" lang="en" id="ordinaryText">
                    <p id="first">First paragraph <em id="emphasis">with emphasis</em></p>
                    <p id="second">Second paragraph</p>
                </div>
                <div class="bloom-editable normal-style" lang="fr" id="frenchText">
                    <p id="frenchFirst">Premier paragraphe</p>
                </div>
            </div>
            <div class="bloom-canvas">
                <div class="bloom-canvas-element">
                    <div class="bloom-translationGroup">
                        <div class="bloom-editable">
                            <p id="canvasParagraph">Canvas text</p>
                        </div>
                    </div>
                </div>
            </div>
            <div id="notText">Not in a text box at all</div>
        </div>`;
}

function element(id: string): HTMLElement {
    const result = document.getElementById(id);
    if (!result)
        throw new Error(`test setup is broken: no element with id ${id}`);
    return result;
}

const l10nIdsOf = (
    content: ReturnType<typeof getTextContextMenuContent>,
): string[] => (content?.inlineImageItems ?? []).map((item) => item.l10nId!);

describe("getTextContextMenuContent", () => {
    beforeEach(setupPage);
    afterEach(() => (document.body.innerHTML = ""));

    it("offers the paragraph command and Insert Image for a plain paragraph", () => {
        const content = getTextContextMenuContent(element("first"));

        expect(content, "expected the menu to open").toBeTruthy();
        // "No Indent" needs the paragraph, and is not offered without one.
        expect(content!.paragraph).toBe(element("first"));
        expect(l10nIdsOf(content)).toEqual(["EditTab.InlineImage.InsertImage"]);
    });

    it("finds the enclosing paragraph when the click is on something inside it", () => {
        const content = getTextContextMenuContent(element("emphasis"));
        expect(content!.paragraph).toBe(element("first"));
    });

    it("offers the paragraph command and Insert Image once the text box has an image", () => {
        insertInlineImage(element("plainGroup"));

        const content = getTextContextMenuContent(element("first"));

        // The menu still opens, with No Indent.
        expect(content!.paragraph).toBe(element("first"));
        // A text box can hold any number of inline images, so Insert Image is still offered.
        // The commands for an existing image come up when that image is clicked.
        expect(l10nIdsOf(content)).toEqual(["EditTab.InlineImage.InsertImage"]);
    });

    it("offers the image's own commands for a click on the image", () => {
        const wrapper = insertInlineImage(element("plainGroup"));

        const content = getTextContextMenuContent(
            wrapper.querySelector("img") as HTMLElement,
        );

        expect(content, "expected the menu to open on the image").toBeTruthy();
        // The image sits between the paragraphs and is not inside one, so there is no
        // paragraph and "No Indent" is not offered.
        expect(content!.paragraph).toBeUndefined();
        // This is the standard image menu, built from the same registry as the canvas element
        // menu and filtered by the usual rules for which commands are available. Rotate is
        // left out, and a divider and Delete come at the end.
        expect(l10nIdsOf(content)).toEqual([
            "EditTab.Image.EditMetadataOverlay",
            "EditTab.Image.ChooseImage",
            "EditTab.Image.CopyImage",
            "EditTab.Image.PasteImage",
            "-",
            "EditTab.Image.Flip",
            "EditTab.Image.Transparency",
            "EditTab.Image.Reset",
            "-",
            "Common.Delete",
        ]);
        // Working out the commands also selects the image they act on. inlineImageCanUndo
        // looks for the selected image when deciding whether ctrl+z is for an inline image.
        expect(wrapper.classList.contains(kInlineImageSelectedClass)).toBe(
            true,
        );
    });

    it("offers nothing for a paragraph inside a canvas element", () => {
        // Sanity check that this is a paragraph in a bloom-editable, so the only reason to leave
        // it out is that it is in a canvas element.
        expect(
            element("canvasParagraph").closest(".bloom-editable"),
        ).toBeTruthy();
        expect(
            getTextContextMenuContent(element("canvasParagraph")),
        ).toBeUndefined();
    });

    it("offers nothing for an inline image inside a canvas element", () => {
        const canvasGroup = element("canvasParagraph").closest(
            ".bloom-translationGroup",
        ) as HTMLElement;
        const wrapper = insertInlineImage(canvasGroup);
        // Canvas elements have their own context menu, which knows about their images.
        expect(
            getTextContextMenuContent(wrapper.querySelector("img")),
        ).toBeUndefined();
    });

    it("offers nothing outside a text box", () => {
        expect(getTextContextMenuContent(element("notText"))).toBeUndefined();
    });

    it("offers Insert Image in the empty space of a text box, where there is no paragraph", () => {
        // Insert Image acts on the whole text box, so it is offered anywhere in the box,
        // including the space below the last line, which is often most of the box.
        const content = getTextContextMenuContent(element("ordinaryText"));

        expect(content, "expected the menu to open").toBeTruthy();
        // Nothing for "No Indent" to act on, so it is not offered.
        expect(content!.paragraph).toBeUndefined();
        expect(l10nIdsOf(content)).toEqual(["EditTab.InlineImage.InsertImage"]);
    });

    it("offers Insert Image in the empty space of a text box that already has an image", () => {
        insertInlineImage(element("plainGroup"));
        // A text box can hold any number of inline images, so Insert Image is still offered.
        // The commands for an existing image come up when that image is clicked.
        const content = getTextContextMenuContent(element("ordinaryText"));
        expect(content, "expected the menu to open").toBeTruthy();
        expect(content!.paragraph).toBeUndefined();
        expect(l10nIdsOf(content)).toEqual(["EditTab.InlineImage.InsertImage"]);
    });

    it("offers nothing for a non-element target", () => {
        expect(getTextContextMenuContent(null)).toBeUndefined();
        expect(getTextContextMenuContent(document)).toBeUndefined();
    });

    it("offers Insert Image in a hidden language's block too", () => {
        // Every language's editable has its own copy of the image, so the command is offered in
        // any of the text box's editables, including ones that are not visible.
        const content = getTextContextMenuContent(element("frenchFirst"));
        expect(content!.paragraph).toBe(element("frenchFirst"));
        expect(l10nIdsOf(content)).toEqual(["EditTab.InlineImage.InsertImage"]);
    });

    it("leaves the image unselected when it offers nothing", () => {
        insertInlineImage(element("plainGroup"));
        getTextContextMenuContent(element("notText"));
        expect(
            getInlineImageInEditable(
                element("ordinaryText"),
            )?.classList.contains(kInlineImageSelectedClass),
        ).toBe(false);
    });
});
