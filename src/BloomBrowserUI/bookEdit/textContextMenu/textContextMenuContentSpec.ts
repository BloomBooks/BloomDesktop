import { describe, it, expect, beforeEach, afterEach } from "vitest";
import { getTextContextMenuContent } from "./textContextMenuContent";
import {
    getInlineImageInEditable,
    insertInlineImage,
    kInlineImageSelectedClass,
} from "../js/inlineImages";

// Which commands a right-click puts on the text context menu. Two sources feed it -- the
// paragraph command ("No Indent") and whatever the text box's inline image has to offer -- so
// these tests are as much about what is NOT offered where.

// An ordinary text box with two paragraphs, a second text box to add an inline image to, and a
// canvas element that also contains text, so we can check which right-clicks the menu claims.
// Same shape as noIndentSpec.ts, with the data-page-id the inline-image undo layer keys on.
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

    it("offers the paragraph command and Add Image for a plain paragraph", () => {
        const content = getTextContextMenuContent(element("first"));

        expect(content, "expected the menu to open").toBeTruthy();
        // The paragraph is what "No Indent" needs; without it that command is not offered.
        expect(content!.paragraph).toBe(element("first"));
        expect(l10nIdsOf(content)).toEqual(["EditTab.InlineImage.AddImage"]);
    });

    it("finds the enclosing paragraph when the click is on something inside it", () => {
        const content = getTextContextMenuContent(element("emphasis"));
        expect(content!.paragraph).toBe(element("first"));
    });

    it("offers the paragraph command and Add Image once the text box has an image", () => {
        insertInlineImage(element("plainGroup"));

        const content = getTextContextMenuContent(element("first"));

        // Still a menu, and still No Indent...
        expect(content!.paragraph).toBe(element("first"));
        // ...and since there is no limit on inline images per text box, adding another is
        // still offered; the commands for an existing image belong to a click on that image.
        expect(l10nIdsOf(content)).toEqual(["EditTab.InlineImage.AddImage"]);
    });

    it("offers the image's own commands for a click on the image", () => {
        const wrapper = insertInlineImage(element("plainGroup"));

        const content = getTextContextMenuContent(
            wrapper.querySelector("img") as HTMLElement,
        );

        expect(content, "expected the menu to open on the image").toBeTruthy();
        // No paragraph: the image sits among the paragraphs, not inside one. So "No Indent",
        // which has nothing to act on, is not offered.
        expect(content!.paragraph).toBeUndefined();
        // The standard image menu (same registry as the canvas element menu, filtered by
        // the normal availability rules), then a divider and Delete.
        expect(l10nIdsOf(content)).toEqual([
            "EditTab.Image.EditMetadataOverlay",
            "EditTab.Image.ChooseImage",
            "EditTab.Image.CopyImage",
            "EditTab.Image.PasteImage",
            "EditTab.Image.Reset",
            "EditTab.Image.Transparency",
            "-",
            "Common.Delete",
        ]);
        // Deciding the commands also selects the image they act on, which is what the
        // inline-image undo layer gates ctrl+z on.
        expect(wrapper.classList.contains(kInlineImageSelectedClass)).toBe(
            true,
        );
    });

    it("offers nothing for a paragraph inside a canvas element", () => {
        // Sanity check: it really is a paragraph of a bloom-editable, so only the
        // canvas-element test can be what rejects it.
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

    it("offers Add Image in the empty space of a text box, where there is no paragraph", () => {
        // Adding an image is a command on the whole text box, not on a paragraph, so it is
        // offered anywhere in the box -- including the space below the last line, which is
        // often most of a box. This is the one place the menu now opens where the
        // paragraph-only rule would not have opened it.
        const content = getTextContextMenuContent(element("ordinaryText"));

        expect(content, "expected the menu to open").toBeTruthy();
        // Nothing for "No Indent" to act on, so it is not offered.
        expect(content!.paragraph).toBeUndefined();
        expect(l10nIdsOf(content)).toEqual(["EditTab.InlineImage.AddImage"]);
    });

    it("offers Add Image in the empty space of a text box that already has an image", () => {
        insertInlineImage(element("plainGroup"));
        // There is no limit on inline images per box, so there is still something to add;
        // an existing image's own commands are reached by clicking that image.
        const content = getTextContextMenuContent(element("ordinaryText"));
        expect(content, "expected the menu to open").toBeTruthy();
        expect(content!.paragraph).toBeUndefined();
        expect(l10nIdsOf(content)).toEqual(["EditTab.InlineImage.AddImage"]);
    });

    it("offers nothing for a non-element target", () => {
        expect(getTextContextMenuContent(null)).toBeUndefined();
        expect(getTextContextMenuContent(document)).toBeUndefined();
    });

    it("offers Add Image in a hidden language's block too", () => {
        // Every language's editable carries its own copy of the image, so the command belongs
        // on any of the text box's blocks, not only the visible one.
        const content = getTextContextMenuContent(element("frenchFirst"));
        expect(content!.paragraph).toBe(element("frenchFirst"));
        expect(l10nIdsOf(content)).toEqual(["EditTab.InlineImage.AddImage"]);
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
