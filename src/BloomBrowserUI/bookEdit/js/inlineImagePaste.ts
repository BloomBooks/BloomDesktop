// Pasting a picture into a text block makes it an inline image in that block (BL-17014). Without
// this, the browser pastes whatever text came with the picture, which is often its path: Bloom's
// own Copy Image, for one, puts the file's path on the clipboard beside the picture.
//
// The picture goes through the same C# endpoint as a picture pasted onto a canvas
// (editView/pasteImage), which copies it into the book's folder and reports it back by calling
// changeImage() in bloomEditing.ts with the imageId it was given. An id that starts with
// kMakeNewInlineImage tells changeImage that the picture is for a new inline image, much as
// kMakeNewCanvasElement does for a new canvas element. The rest of the id says which paste it
// answers, so that two pastes made before C# answers the first each find their own block.
// changeImage then calls finishPasteImageAsInlineImage, which adds the image to the block the
// paste was for. The image is added only once the picture has arrived, so a paste that fails
// leaves nothing behind, and one Undo takes back the whole paste. Until then the paste holds a
// page-content delay, so that a save made in the meantime waits for the picture (see
// startImagePaste in bloomImages.ts).
//
// When an inline image is selected, a pasted picture replaces that image's picture instead, as
// the image's own "Paste image" command does (its menu shows Ctrl+V as the shortcut), and as a
// paste onto a selected picture on a canvas does.
//
// A clipboard that holds formatted text as well as a picture, as a copy from Word or Excel
// usually does, is pasted as text, as before. The picture there is usually just a rendering of
// that same text.
import { postJson } from "../../utils/bloomApi";
import {
    doImageCommand,
    endImagePaste,
    getOwningPageBackgroundColor,
    handlePasteImageApiError,
    startImagePaste,
} from "./bloomImages";
import { EditableDivUtils } from "./editableDivUtils";
import { kInlineImageSelectedClass } from "./inlineImages";
import {
    addInlineImage,
    getInlineImageActionTarget,
} from "./inlineImageInteractions";

/**
 * How the imageId that editView/pasteImage hands back to changeImage() starts, when the pasted
 * picture is for a new inline image. See the comment at the top of this file.
 */
export const kMakeNewInlineImage = "makeNewInlineImage";

// The pastes of new inline images that are waiting for C#, by the imageId each one sent, with
// the translation group each will add its image to. Every paste has its own id, so an entry is
// never used for any paste but its own. One that C# gives up on (removeImageId) just stays; no
// answer will ever come with its id.
const pendingPastes = new Map<string, HTMLElement>();

/**
 * Where a picture pasted now should go, if it is for inline images at all: "replace" the picture
 * of the selected inline image, or "add" a new inline image to a translation group.
 */
export type InlineImagePasteTarget =
    | { kind: "replace"; img: HTMLElement }
    | { kind: "add"; translationGroup: HTMLElement };

/**
 * Where a picture pasted now should go (see InlineImagePasteTarget), or undefined when the paste
 * is not for inline images, and is then handled as it always was.
 *
 * A selected inline image comes first, wherever the focus is: clicking an image selects it
 * without moving the focus. Bloom also selects an image it has just added or changed (Insert
 * Image, a paste, an undo), and typing does not deselect it; any press elsewhere on the page
 * does. So Insert Image followed by a paste fills the new image, and a second paste right after
 * a first replaces the picture just pasted. That is what a paste onto a canvas does too: the
 * pasted picture is left selected, and the next paste replaces it.
 *
 * Otherwise it is the text block that has the focus, provided that block can take inline images
 * (see getInlineImageActionTarget: not a canvas element, an xmatter or data-book field, or an
 * image description). This goes by the focus, not by where the text selection is. Selecting a
 * picture on a canvas takes the focus out of the text but leaves the text selection where it
 * was, and a paste then belongs to that picture (see CanvasElementClipboard.pasteImageFromClipboard).
 * The page keeps its focused element while the top bar's Paste button is being clicked, so that
 * route is decided the same way.
 */
export function getInlineImagePasteTarget():
    | InlineImagePasteTarget
    | undefined {
    const selected = document.querySelector("." + kInlineImageSelectedClass);
    if (selected) {
        return {
            kind: "replace",
            img: selected.querySelector("img") as HTMLElement,
        };
    }
    const target = getInlineImageActionTarget(
        document.activeElement as HTMLElement | null,
    );
    // The focus can't be in an existing inline image, which is contenteditable=false.
    return target.kind === "add"
        ? { kind: "add", translationGroup: target.translationGroup }
        : undefined;
}

/**
 * Pastes the picture on the clipboard where `target` says. Replacing goes through the same
 * command as the image's "Paste image" button, which records its own undo and copies the new
 * picture to the other languages when it arrives (changeImage).
 */
export function pasteClipboardImage(target: InlineImagePasteTarget): void {
    if (target.kind === "add")
        pasteClipboardImageAsInlineImage(target.translationGroup);
    else doImageCommand(target.img, "paste");
}

/**
 * Whether the clipboard's HTML, if it has any, is nothing more than a picture. A browser's
 * "Copy image" offers `<img src=...>` as HTML beside the picture, and that should paste as the
 * picture. HTML with any text in it, such as a copy from Word, Excel or part of a web page,
 * should paste as text.
 */
export function htmlIsJustAPicture(html: string | undefined): boolean {
    if (!html?.trim()) return true;
    const body = new DOMParser().parseFromString(html, "text/html").body;
    return (
        !body.textContent?.trim() && body.querySelectorAll("img").length <= 1
    );
}

/**
 * Whether the clipboard data of a paste event holds a picture and nothing that ought to be
 * pasted as text instead (see htmlIsJustAPicture). Plain text beside the picture does not count,
 * because it is usually the picture's path or URL.
 */
export function dataTransferHoldsJustAPicture(
    data: Pick<DataTransfer, "items" | "getData">,
): boolean {
    const hasPicture = Array.from(data.items).some(
        (item) => item.kind === "file" && item.type.startsWith("image/"),
    );
    return hasPicture && htmlIsJustAPicture(data.getData("text/html"));
}

/**
 * Whether the clipboard, which C# has already found holds a picture, holds nothing that ought to
 * be pasted as text instead. This is for the top bar's Paste button, which has no paste event
 * to look at.
 */
export async function clipboardHoldsJustAPicture(): Promise<boolean> {
    const items = await navigator.clipboard.read();
    for (const item of items) {
        if (!item.types.includes("text/html")) continue;
        const html = await (await item.getType("text/html")).text();
        if (!htmlIsJustAPicture(html)) return false;
    }
    return true;
}

/**
 * Asks C# for the picture on the clipboard, to be added to this translation group as a new
 * inline image. finishPasteImageAsInlineImage completes the job when the picture arrives.
 */
export function pasteClipboardImageAsInlineImage(
    translationGroup: HTMLElement,
): void {
    const imageId = `${kMakeNewInlineImage}-${EditableDivUtils.createUuid()}`;
    pendingPastes.set(imageId, translationGroup);
    startImagePaste(imageId);
    postJson(
        "editView/pasteImage",
        {
            imageId,
            imageSrc: "",
            imageIsGif: false,
            pageBackgroundColor: getOwningPageBackgroundColor(translationGroup),
        },
        undefined,
        (error) => {
            pendingPastes.delete(imageId);
            endImagePaste(imageId);
            handlePasteImageApiError(error);
        },
    );
}

/** Whether changeImage() was given this imageId by a paste of a new inline image. */
export function isInlineImagePasteId(imageId: string): boolean {
    return imageId.startsWith(kMakeNewInlineImage);
}

/**
 * Called by changeImage() when C# has put the pasted picture in the book's folder. Adds the
 * inline image to the block the paste with this imageId was for. `setUpPicture` puts the picture
 * into the new image's img (changeImage passes changeImageInfo, which also copies it to the
 * other languages).
 */
export function finishPasteImageAsInlineImage(
    imageId: string,
    setUpPicture: (img: HTMLElement) => void,
): void {
    const translationGroup = pendingPastes.get(imageId);
    if (!translationGroup) {
        throw new Error(
            `finishPasteImageAsInlineImage: no inline image paste is waiting for ${imageId}`,
        );
    }
    pendingPastes.delete(imageId);
    try {
        addInlineImage(translationGroup, setUpPicture);
    } finally {
        // Only now, with the picture on the page, may a waiting save go ahead.
        endImagePaste(imageId);
    }
}

/**
 * The document's paste listener, in the capture phase so that it runs before CKEditor's. If an
 * inline image is selected, or the focus is in a text block that can take inline images, and the
 * clipboard holds just a picture, the paste goes to inline images (see getInlineImagePasteTarget)
 * and nobody else sees the event. Otherwise it does nothing, and the paste goes on as it always
 * has.
 */
export function pasteImageIntoTextAsInlineImage(e: ClipboardEvent): void {
    const target = getInlineImagePasteTarget();
    if (!target || !dataTransferHoldsJustAPicture(e.clipboardData!)) return;
    e.preventDefault();
    e.stopImmediatePropagation();
    pasteClipboardImage(target);
}
