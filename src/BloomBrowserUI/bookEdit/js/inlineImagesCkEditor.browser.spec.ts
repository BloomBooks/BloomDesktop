import { describe, it, expect, beforeAll } from "vitest";
// Runs in the browser vitest project, because it loads the real CKEditor that Bloom ships
// (lib/ckeditor), which needs a real browser to build its editors.
import { attachToCkEditor } from "./bloomEditing";
import { kInlineImageClass } from "./inlineImageConstants";

const ckeditorScriptUrl = new URL(
    "../../lib/ckeditor/ckeditor.js",
    import.meta.url,
).href;

/**
 * Loads lib/ckeditor/ckeditor.js as Bloom's edit page does, with a script tag. CKEditor then
 * loads its config.js, plugins, language and skin from the folder it was loaded from.
 */
function loadCkEditor(): Promise<void> {
    // CKEditor reads this global to find the folder its other files are in.
    (window as unknown as { CKEDITOR_BASEPATH: string }).CKEDITOR_BASEPATH =
        ckeditorScriptUrl.substring(0, ckeditorScriptUrl.lastIndexOf("/") + 1);
    return new Promise((resolve, reject) => {
        const script = document.createElement("script");
        script.src = ckeditorScriptUrl;
        script.onload = () => resolve();
        script.onerror = () =>
            reject(new Error(`Could not load ${ckeditorScriptUrl}`));
        document.head.appendChild(script);
    });
}

/** Returns the CKEditor instance that attachToCkEditor made for the editable. */
function getEditorFor(editable: HTMLElement): CKEDITOR.editor {
    const editor = Object.values(CKEDITOR.instances).find(
        (instance) => instance.element.$ === editable,
    );
    if (!editor) throw new Error("attachToCkEditor made no editor");
    return editor;
}

/** Resolves once the editor has finished loading the box (CKEditor's instanceReady). */
function waitForInstanceReady(editor: CKEDITOR.editor): Promise<void> {
    if ((editor as { instanceReady?: boolean }).instanceReady)
        return Promise.resolve();
    return new Promise((resolve) =>
        editor.on("instanceReady", () => resolve()),
    );
}

/** The src of the inline image in the HTML that the editor's getData() returns. */
function getInlineImageSrcInData(editor: CKEDITOR.editor): string | null {
    const data = document.createElement("div");
    data.innerHTML = editor.getData();
    const img = data.querySelector(`.${kInlineImageClass} img`);
    if (!img)
        throw new Error(
            `getData() returned no inline image: ${editor.getData()}`,
        );
    return img.getAttribute("src");
}

describe("an inline image in a box that CKEditor manages", () => {
    beforeAll(async () => {
        await loadCkEditor();
        // Bloom makes each editor itself, in attachToCkEditor.
        CKEDITOR.disableAutoInline = true;
        // Keeps attachToCkEditor from asking Bloom (which is not running) for the palette.
        CKEDITOR.config.colorButton_colors = "000000,FF0000";
    });

    it("is saved by getData() with the picture it was changed to after the editor loaded", async () => {
        document.body.innerHTML =
            '<div class="bloom-page"><div class="marginBox">' +
            '<div class="bloom-translationGroup">' +
            '<div class="bloom-editable bloom-content1" lang="en" contenteditable="true">' +
            `<div class="${kInlineImageClass}" contenteditable="false"><img src="old.png" alt=""></div>` +
            "<p>Some text</p>" +
            "</div></div></div></div>";
        const editable = document.querySelector(
            ".bloom-editable",
        ) as HTMLElement;

        attachToCkEditor(editable);
        const editor = getEditorFor(editable);
        await waitForInstanceReady(editor);

        // sanity check: the editor loaded the box with the old picture
        expect(getInlineImageSrcInData(editor)).toBe("old.png");

        // This is how changeImageInfo in bloomEditing.ts gives the img its new picture.
        const img = editable.querySelector(
            `.${kInlineImageClass} img`,
        ) as HTMLImageElement;
        img.setAttribute("src", "new.png");

        expect(
            getInlineImageSrcInData(editor),
            "CKEditor's getData() should write the picture's current src, not the src it had when the editor loaded the box",
        ).toBe("new.png");
    });
});
