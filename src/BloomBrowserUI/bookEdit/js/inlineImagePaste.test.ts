import {
    describe,
    it,
    expect,
    beforeEach,
    afterEach,
    afterAll,
    vi,
} from "vitest";

// Adding the image selects it, which shows its toolbar, and the toolbar asks the api whether AI
// image editing is on. There is no api here, so these answer "off"; see the same mock in
// inlineImageInteractions.test.ts.
vi.mock("../../react_components/featureStatus", () => ({
    getFeatureStatusAsync: () => Promise.resolve(undefined),
    useGetFeatureStatus: () => undefined,
    useGetFeatureAvailabilityMessage: () => "",
    openBloomSubscriptionSettings: () => {},
}));
// The paste's request to C#. The tests check what it asks for, and play C#'s part by calling
// finishPasteImageAsInlineImage themselves.
const api = vi.hoisted(() => ({
    postJson: vi.fn(),
    handlePasteImageApiError: vi.fn(),
    doImageCommand: vi.fn(),
}));
// Unit tests must not reach the network (see vitest.setup.ts). Adding the image selects it, and
// its toolbar asks Bloom for the collection's language names; see the same mock in
// inlineImageInteractions.test.ts.
vi.mock("../../utils/bloomApi", async (importOriginal) => {
    const actual =
        await importOriginal<typeof import("../../utils/bloomApi")>();
    return {
        ...actual,
        postJson: api.postJson,
        useApiObject: <T>(urlSuffix: string, defaultValue: T) =>
            urlSuffix === "settings/languageNames"
                ? defaultValue
                : actual.useApiObject(urlSuffix, defaultValue),
        trackEvent: () => {},
    };
});
// A paste holds a page-content delay until it ends, so that a save waits for the picture. The
// tests check that every way a paste ends releases it.
const delays = vi.hoisted(() => ({
    add: vi.fn(),
    remove: vi.fn(),
}));
// bloomEditing imports this module, so a mock built on importOriginal would load the real
// bloomEditing, and through it this module, before the mock existed. Nothing here needs the rest
// of bloomEditing, so the mock is only these two.
vi.mock("./bloomEditing", () => ({
    addRequestPageContentDelay: delays.add,
    removeRequestPageContentDelay: delays.remove,
}));
// Showing the failure's message needs Bloom's message box, which is not here. Replacing a
// selected image's picture goes through doImageCommand, as its Paste image button does; the
// tests check that it is asked to.
vi.mock("./bloomImages", async (importOriginal) => ({
    ...(await importOriginal<typeof import("./bloomImages")>()),
    handlePasteImageApiError: api.handlePasteImageApiError,
    doImageCommand: api.doImageCommand,
}));
import OverflowChecker from "../OverflowChecker/OverflowChecker";
import {
    getTestRoot,
    cleanTestRoot,
    removeTestRoot,
} from "../../utils/testHelper";
import {
    discardPendingInlineImageUndo,
    getInlineImageInEditable,
    getInlineImages,
    insertInlineImage,
    kInlineImageSelectedClass,
    syncInlineImagesFromEditable,
} from "./inlineImages";
import { cleanupInlineImageInteractions } from "./inlineImageInteractions";
import {
    clipboardHoldsJustAPicture,
    dataTransferHoldsJustAPicture,
    finishPasteImageAsInlineImage,
    endInlineImagePaste,
    getInlineImagePasteTarget,
    htmlIsJustAPicture,
    isInlineImagePasteId,
    kMakeNewInlineImage,
    pasteClipboardImageAsInlineImage,
    pasteImageIntoTextAsInlineImage,
} from "./inlineImagePaste";
import { theOneUndoStack } from "../undo/UndoStack";

// What a browser's "Copy image" puts on the clipboard as HTML, beside the picture itself.
const kCopyImageHtml =
    '<html><body><!--StartFragment--><img src="https://example.com/bird.png"/><!--EndFragment--></body></html>';
// A copy of a few words from Word, which offers a rendering of them as a picture too.
const kWordHtml =
    "<html><body><!--StartFragment--><p class=MsoNormal>Some <b>words</b></p><!--EndFragment--></body></html>";

// Something shaped like the clipboardData of a paste event. Each entry is one of its items:
// a MIME type for a file (a picture) or for a string.
function makeClipboardData(
    items: { kind: "file" | "string"; type: string; fileName?: string }[],
    html?: string,
): Pick<DataTransfer, "items" | "getData"> {
    return {
        items: items.map((item) => ({
            kind: item.kind,
            type: item.type,
            getAsFile: () =>
                item.fileName ? new File([], item.fileName) : null,
        })) as unknown as DataTransferItemList,
        getData: (format: string) =>
            format === "text/html" ? (html ?? "") : "",
    };
}

const kPictureItem = { kind: "file" as const, type: "image/png" };
const kTextItem = { kind: "string" as const, type: "text/plain" };
const kHtmlItem = { kind: "string" as const, type: "text/html" };

// Builds a page holding one text block, in English and French, inside an element with
// `wrapperClass` if given (for instance a canvas element), with the given attributes on the
// English editable. Puts the caret in the English text, giving it the focus as clicking there
// does, and returns the translation group. (In Bloom, CKEditor makes the editables focusable;
// here a tabindex does.)
function makeTextBlockWithCaret(
    options: { wrapperClass?: string; editableAttributes?: string } = {},
): HTMLElement {
    const root = getTestRoot();
    const groupHtml =
        `<div class="bloom-translationGroup" id="group">` +
        `<div class="bloom-editable bloom-content1 bloom-visibility-code-on" lang="en" tabindex="0" ${
            options.editableAttributes ?? ""
        }><p id="caretHere">English</p></div>` +
        `<div class="bloom-editable" lang="fr"><p>French</p></div>` +
        `</div>`;
    root.innerHTML =
        `<div class="bloom-page" data-page-id="test-page">` +
        (options.wrapperClass
            ? `<div class="${options.wrapperClass}">${groupHtml}</div>`
            : groupHtml) +
        `</div>`;
    const group = root.querySelector("#group") as HTMLElement;
    editableFor(group, "en").focus();
    const text = root.querySelector("#caretHere")!.firstChild!;
    document.getSelection()!.collapse(text, 3);
    return group;
}

function editableFor(group: HTMLElement, lang: string): HTMLElement {
    return group.querySelector(`[lang="${lang}"]`) as HTMLElement;
}

// The imageId that the nth paste (counting from 0) sent to C#.
function sentImageId(n: number): string {
    return (api.postJson.mock.calls[n][1] as { imageId: string }).imageId;
}

// Plays C#'s part: answers the paste that sent this imageId, as changeImage does with the
// picture, which is to set it on the img and copy it to the other languages (changeImageInfo).
function deliverPicture(imageId: string, src: string): void {
    finishPasteImageAsInlineImage(imageId, (img) => {
        img.setAttribute("src", src);
        syncInlineImagesFromEditable(
            img.closest(".bloom-editable") as HTMLElement,
        );
    });
}

describe("inlineImagePaste", () => {
    beforeEach(() => {
        cleanTestRoot();
        theOneUndoStack.clear();
        discardPendingInlineImageUndo();
        api.postJson.mockReset();
        api.handlePasteImageApiError.mockReset();
        api.doImageCommand.mockReset();
        delays.add.mockReset();
        delays.remove.mockReset();
        // See the same spy in inlineImages.test.ts: the overflow check needs layout jsdom lacks.
        vi.spyOn(
            OverflowChecker,
            "AdjustSizeOrMarkOverflowSoon",
        ).mockImplementation(() => {});
    });
    afterEach(() => {
        cleanupInlineImageInteractions();
        vi.restoreAllMocks();
    });
    afterAll(removeTestRoot);

    describe("htmlIsJustAPicture", () => {
        it("accepts no HTML at all", () => {
            expect(htmlIsJustAPicture(undefined)).toBe(true);
            expect(htmlIsJustAPicture("")).toBe(true);
        });
        it("accepts the HTML of a browser's Copy Image", () => {
            expect(htmlIsJustAPicture(kCopyImageHtml)).toBe(true);
        });
        it("rejects HTML with text in it", () => {
            expect(htmlIsJustAPicture(kWordHtml)).toBe(false);
        });
        it("rejects a spreadsheet's table", () => {
            expect(
                htmlIsJustAPicture(
                    "<table><tr><td>1</td><td>2</td></tr></table>",
                ),
            ).toBe(false);
        });
        it("rejects more than one picture", () => {
            expect(
                htmlIsJustAPicture('<img src="a.png"><img src="b.png">'),
            ).toBe(false);
        });
    });

    // The Paste button's check. C# has already found a picture on the clipboard; this reads the
    // clipboard's HTML to see whether it is just that picture.
    describe("clipboardHoldsJustAPicture", () => {
        // Puts a clipboard holding one item with these representations where the code reads it.
        function setClipboard(representations: Record<string, string>) {
            const item = {
                types: Object.keys(representations),
                getType: (type: string) =>
                    // What the browser gives is a Blob; jsdom's Blob has no text(), so this is
                    // just the part of one that the code reads.
                    Promise.resolve({
                        text: () => Promise.resolve(representations[type]),
                    }),
            };
            Object.defineProperty(navigator, "clipboard", {
                value: { read: () => Promise.resolve([item]) },
                configurable: true,
            });
        }

        it("accepts a clipboard with no HTML", async () => {
            setClipboard({ "image/png": "", "text/plain": "C:\pic.png" });
            expect(await clipboardHoldsJustAPicture()).toBe(true);
        });
        it("accepts HTML that is just the picture", async () => {
            setClipboard({ "image/png": "", "text/html": kCopyImageHtml });
            expect(await clipboardHoldsJustAPicture()).toBe(true);
        });
        it("rejects HTML with text in it", async () => {
            setClipboard({ "image/png": "", "text/html": kWordHtml });
            expect(await clipboardHoldsJustAPicture()).toBe(false);
        });
    });

    describe("dataTransferHoldsJustAPicture", () => {
        it("accepts a picture alone", () => {
            expect(
                dataTransferHoldsJustAPicture(
                    makeClipboardData([kPictureItem]),
                ),
            ).toBe(true);
        });
        it("accepts a picture with its path as plain text, as Bloom's Copy Image gives", () => {
            expect(
                dataTransferHoldsJustAPicture(
                    makeClipboardData([kTextItem, kPictureItem]),
                ),
            ).toBe(true);
        });
        it("accepts a picture whose HTML is just the picture", () => {
            expect(
                dataTransferHoldsJustAPicture(
                    makeClipboardData(
                        [kHtmlItem, kPictureItem],
                        kCopyImageHtml,
                    ),
                ),
            ).toBe(true);
        });
        it("accepts an image file without a MIME type, by its name", () => {
            expect(
                dataTransferHoldsJustAPicture(
                    makeClipboardData([
                        { kind: "file", type: "", fileName: "bird.JPG" },
                    ]),
                ),
            ).toBe(true);
        });
        it("rejects a picture that comes with formatted text", () => {
            expect(
                dataTransferHoldsJustAPicture(
                    makeClipboardData([kHtmlItem, kPictureItem], kWordHtml),
                ),
            ).toBe(false);
        });
        it("rejects text alone", () => {
            expect(
                dataTransferHoldsJustAPicture(makeClipboardData([kTextItem])),
            ).toBe(false);
        });
        it("rejects a file that is not a picture", () => {
            expect(
                dataTransferHoldsJustAPicture(
                    makeClipboardData([
                        {
                            kind: "file",
                            type: "application/pdf",
                            fileName: "doc.pdf",
                        },
                    ]),
                ),
            ).toBe(false);
        });
    });

    describe("getInlineImagePasteTarget", () => {
        it("gives the text block the caret is in", () => {
            const group = makeTextBlockWithCaret();
            expect(getInlineImagePasteTarget()).toEqual({
                kind: "add",
                translationGroup: group,
            });
        });
        // Clicking an image selects it without moving the focus, and any other press on the page
        // deselects it. So a selected image is what the user last clicked, wherever the focus is.
        it("gives the selected inline image, even with the focus somewhere else", () => {
            const group = makeTextBlockWithCaret();
            const wrapper = insertInlineImage(group);
            wrapper.classList.add(kInlineImageSelectedClass);
            (document.activeElement as HTMLElement).blur();
            // Sanity check: nothing has the focus.
            expect(document.activeElement).toBe(document.body);

            expect(getInlineImagePasteTarget()).toEqual({
                kind: "replace",
                img: wrapper.querySelector("img"),
            });
        });
        it("gives nothing in a canvas element's text", () => {
            makeTextBlockWithCaret({ wrapperClass: "bloom-canvas-element" });
            expect(getInlineImagePasteTarget()).toBeUndefined();
        });
        it("gives nothing in a field Bloom writes itself, such as the title", () => {
            makeTextBlockWithCaret({
                editableAttributes: 'data-book="bookTitle"',
            });
            expect(getInlineImagePasteTarget()).toBeUndefined();
        });
        // Selecting a picture on a canvas takes the focus out of the text but leaves the text
        // selection there. The paste then belongs to the picture (BL-16542), not to the text.
        it("gives nothing once the focus has left the text, though the caret stays there", () => {
            makeTextBlockWithCaret();
            (document.activeElement as HTMLElement).blur();
            // jsdom clears the selection on blur, which Chromium does not, so put it back.
            const text = document.getElementById("caretHere")!.firstChild!;
            document.getSelection()!.collapse(text, 3);
            // Sanity check: the caret is in the block, but the focus is not.
            expect(document.getSelection()!.anchorNode).toBe(text);
            expect(document.activeElement).toBe(document.body);

            expect(getInlineImagePasteTarget()).toBeUndefined();
        });
    });

    describe("pasting", () => {
        it("adds the pasted picture to every language as one undoable step", () => {
            const group = makeTextBlockWithCaret();
            // Sanity check: no images to begin with.
            expect(getInlineImages(group).length).toBe(0);

            pasteClipboardImageAsInlineImage(group);
            expect(api.postJson).toHaveBeenCalledWith(
                "editView/pasteImage",
                expect.objectContaining({
                    imageId: expect.stringMatching(
                        new RegExp(`^${kMakeNewInlineImage}-`),
                    ),
                }),
                undefined,
                expect.any(Function),
            );
            expect(isInlineImagePasteId(sentImageId(0))).toBe(true);
            // Nothing is added until the picture arrives, and until then a save has to wait.
            expect(getInlineImages(group).length).toBe(0);
            expect(delays.add).toHaveBeenCalledWith(sentImageId(0));
            expect(delays.remove).not.toHaveBeenCalled();

            deliverPicture(sentImageId(0), "pasted.png");

            expect(delays.remove).toHaveBeenCalledWith(sentImageId(0));

            ["en", "fr"].forEach((lang) => {
                const img = getInlineImageInEditable(
                    editableFor(group, lang),
                )?.querySelector("img");
                expect(img?.getAttribute("src"), lang).toBe("pasted.png");
            });
            expect(theOneUndoStack.getEntryCount()).toBe(1);

            theOneUndoStack.undo();

            expect(getInlineImages(group).length).toBe(0);
        });

        it("adds nothing when the request fails", () => {
            const group = makeTextBlockWithCaret();
            pasteClipboardImageAsInlineImage(group);
            const onError = api.postJson.mock.calls[0][3] as (
                error: unknown,
            ) => void;
            const error = { response: { data: "No image" } };

            onError(error);

            expect(api.handlePasteImageApiError).toHaveBeenCalledWith(error);
            expect(delays.remove).toHaveBeenCalledWith(sentImageId(0));
            // The paste is over, so a picture arriving now would be a mistake.
            expect(() => deliverPicture(sentImageId(0), "late.png")).toThrow(
                /no inline image paste is waiting/,
            );
            expect(getInlineImages(group).length).toBe(0);
        });

        // C# calls removeImageId with the paste's id when it gives up, for instance when the
        // user declines the JPEG warning. A later paste must not get that paste's block.
        it("forgets a paste that C# gave up on", () => {
            const group = makeTextBlockWithCaret();
            pasteClipboardImageAsInlineImage(group);

            endInlineImagePaste(sentImageId(0));
            expect(delays.remove).toHaveBeenCalledWith(sentImageId(0));
            // Forgetting it again (or forgetting an id that was never a paste) changes nothing.
            endInlineImagePaste(sentImageId(0));
            expect(delays.remove).toHaveBeenCalledTimes(1);

            expect(() => deliverPicture(sentImageId(0), "late.png")).toThrow(
                /no inline image paste is waiting/,
            );
            expect(getInlineImages(group).length).toBe(0);
        });

        it("adds both pictures when a second paste is made before the first arrives", () => {
            const group = makeTextBlockWithCaret();
            pasteClipboardImageAsInlineImage(group);
            pasteClipboardImageAsInlineImage(group);
            // Sanity check: each paste sent its own id.
            expect(sentImageId(0)).not.toBe(sentImageId(1));

            deliverPicture(sentImageId(0), "first.png");
            deliverPicture(sentImageId(1), "second.png");

            const srcs = getInlineImages(group)
                .filter((w) => w.closest('[lang="en"]'))
                .map((w) => w.querySelector("img")!.getAttribute("src"));
            expect(srcs.sort()).toEqual(["first.png", "second.png"]);
        });

        // A pasted picture is left selected, as Insert Image leaves its new image, so the next
        // paste replaces it rather than adding another; a paste onto a canvas does the same.
        it("leaves the pasted picture selected, so a second paste would replace it", () => {
            const group = makeTextBlockWithCaret();
            pasteClipboardImageAsInlineImage(group);
            deliverPicture(sentImageId(0), "first.png");
            const added = getInlineImages(group).find(
                (w) =>
                    w.classList.contains(kInlineImageSelectedClass) &&
                    w.closest('[lang="en"]'),
            );
            // Sanity check: the new picture is the selected one.
            expect(added?.querySelector("img")?.getAttribute("src")).toBe(
                "first.png",
            );

            expect(getInlineImagePasteTarget()).toEqual({
                kind: "replace",
                img: added!.querySelector("img"),
            });
        });

        it("throws if a picture arrives that no paste asked for", () => {
            expect(() =>
                deliverPicture(`${kMakeNewInlineImage}-unknown`, "stray.png"),
            ).toThrow(/no inline image paste is waiting/);
        });
    });

    describe("pasteImageIntoTextAsInlineImage", () => {
        function makePasteEvent(
            clipboardData: Pick<DataTransfer, "items" | "getData">,
        ) {
            return {
                clipboardData,
                preventDefault: vi.fn(),
                stopImmediatePropagation: vi.fn(),
            };
        }

        it("takes over a paste of a picture into text", () => {
            makeTextBlockWithCaret();
            const event = makePasteEvent(
                makeClipboardData([kTextItem, kPictureItem]),
            );

            pasteImageIntoTextAsInlineImage(event as unknown as ClipboardEvent);

            expect(event.preventDefault).toHaveBeenCalled();
            expect(event.stopImmediatePropagation).toHaveBeenCalled();
            expect(api.postJson).toHaveBeenCalledTimes(1);
        });

        it("leaves a paste of formatted text alone, even with a picture", () => {
            makeTextBlockWithCaret();
            const event = makePasteEvent(
                makeClipboardData([kHtmlItem, kPictureItem], kWordHtml),
            );

            pasteImageIntoTextAsInlineImage(event as unknown as ClipboardEvent);

            expect(event.preventDefault).not.toHaveBeenCalled();
            expect(api.postJson).not.toHaveBeenCalled();
        });

        it("replaces the picture of a selected inline image instead of adding one", () => {
            const group = makeTextBlockWithCaret();
            const wrapper = insertInlineImage(group);
            wrapper.classList.add(kInlineImageSelectedClass);
            const event = makePasteEvent(makeClipboardData([kPictureItem]));

            pasteImageIntoTextAsInlineImage(event as unknown as ClipboardEvent);

            expect(event.preventDefault).toHaveBeenCalled();
            const img = wrapper.querySelector("img")!;
            expect(api.doImageCommand).toHaveBeenCalledWith(img, "paste");
            // No new image is asked for.
            expect(api.postJson).not.toHaveBeenCalled();
            // A save waits for the new picture, until C# answers with the img's id (changeImage
            // or removeImageId, which both end the paste).
            expect(img.id).not.toBe("");
            expect(delays.add).toHaveBeenCalledWith(img.id);
            expect(delays.remove).not.toHaveBeenCalled();

            endInlineImagePaste(img.id);

            expect(delays.remove).toHaveBeenCalledWith(img.id);
        });

        // Both pastes would put the same clipboard picture there, and C# would answer the second
        // with an id the first answer has already taken off the img.
        it("ignores a second paste over a selected image whose replacement is still pending", () => {
            const group = makeTextBlockWithCaret();
            const wrapper = insertInlineImage(group);
            wrapper.classList.add(kInlineImageSelectedClass);
            const paste = () =>
                pasteImageIntoTextAsInlineImage(
                    makePasteEvent(
                        makeClipboardData([kPictureItem]),
                    ) as unknown as ClipboardEvent,
                );

            paste();
            paste();

            expect(api.doImageCommand).toHaveBeenCalledTimes(1);
            expect(delays.add).toHaveBeenCalledTimes(1);

            // Once C# has answered, a new paste goes ahead.
            const img = wrapper.querySelector("img")!;
            endInlineImagePaste(img.id);
            paste();

            expect(api.doImageCommand).toHaveBeenCalledTimes(2);
        });

        it("leaves a picture alone where inline images can't go", () => {
            makeTextBlockWithCaret({ wrapperClass: "bloom-canvas-element" });
            const event = makePasteEvent(makeClipboardData([kPictureItem]));

            pasteImageIntoTextAsInlineImage(event as unknown as ClipboardEvent);

            expect(event.preventDefault).not.toHaveBeenCalled();
            expect(api.postJson).not.toHaveBeenCalled();
        });
    });
});
