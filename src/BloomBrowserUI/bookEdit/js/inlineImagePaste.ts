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
// requestPageContent).
//
// A clipboard that holds formatted text as well as a picture, as a copy from Word or Excel
// usually does, is pasted as text, as before. The picture there is usually just a rendering of
// that same text.
import { postJson } from "../../utils/bloomApi";
import {
    getOwningPageBackgroundColor,
    handlePasteImageApiError,
} from "./bloomImages";
import {
    addRequestPageContentDelay,
    removeRequestPageContentDelay,
} from "./bloomEditing";
import { EditableDivUtils } from "./editableDivUtils";
import {
    addInlineImage,
    getInlineImageActionTarget,
} from "./inlineImageInteractions";

/**
 * How the imageId that editView/pasteImage hands back to changeImage() starts, when the pasted
 * picture is for a new inline image. See the comment at the top of this file.
 */
export const kMakeNewInlineImage = "makeNewInlineImage";

// The pastes that are waiting for C#, by the imageId each one sent, with the translation group
// each will add its image to. C# forgets a paste that fails or that the user cancels (by
// declining the JPEG warning, for one) by calling removeImageId with its id, which calls
// forgetInlineImagePaste. So an entry is never used for any paste but its own. Each one holds a
// page-content delay with its imageId as the delay's id, released by endPendingPaste. If C# ends a
// paste without either answer (ChangePicture reports its own errors), the delay stays, and saves of
// this page wait out requestPageContent's limit; going to another page starts afresh.
const pendingPastes = new Map<string, HTMLElement>();

// Forgets a pending paste and releases its page-content delay. Does nothing for an imageId that is
// not pending, so it is safe to call from every way a paste can end.
function endPendingPaste(imageId: string): void {
    if (pendingPastes.delete(imageId)) removeRequestPageContentDelay(imageId);
}

// Extensions of image files Bloom can paste. A file copied in Windows Explorer is usually
// offered with a MIME type, but this catches one that is not.
const kImageFileExtensions = /\.(png|jpe?g|gif|bmp|tiff?)$/i;

/**
 * The translation group that a picture pasted now should go into, which is the text block that
 * has the focus, provided that block can take inline images (see getInlineImageActionTarget: not
 * a canvas element, an xmatter or data-book field, or an image description). Returns undefined
 * anywhere else, and then the paste is handled as it always was.
 *
 * This goes by the focus, not by where the text selection is. Selecting a picture on a canvas
 * takes the focus out of the text but leaves the text selection where it was, and a paste then
 * belongs to that picture (see CanvasElementClipboard.pasteImageFromClipboard). The page keeps
 * its focused element while the top bar's Paste button is being clicked, so that route is
 * decided the same way.
 */
export function getInlineImagePasteTarget(): HTMLElement | undefined {
    const target = getInlineImageActionTarget(
        document.activeElement as HTMLElement | null,
    );
    // The focus can't be in an existing inline image, which is contenteditable=false.
    return target.kind === "add" ? target.translationGroup : undefined;
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
        (item) =>
            item.kind === "file" &&
            (item.type.startsWith("image/") ||
                kImageFileExtensions.test(item.getAsFile()?.name ?? "")),
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
    addRequestPageContentDelay(imageId);
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
            endPendingPaste(imageId);
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
    try {
        addInlineImage(translationGroup, setUpPicture);
    } finally {
        // Only now, with the picture on the page, may a waiting save go ahead.
        endPendingPaste(imageId);
    }
}

/** Forgets a paste that C# has given up on. See pendingPastes. */
export function forgetInlineImagePaste(imageId: string): void {
    endPendingPaste(imageId);
}

/**
 * The document's paste listener, in the capture phase so that it runs before CKEditor's. If the
 * caret is in a text block that can take inline images and the clipboard holds just a picture,
 * the paste becomes an inline image and nobody else sees the event. Otherwise it does nothing,
 * and the paste goes on as it always has.
 */
export function pasteImageIntoTextAsInlineImage(e: ClipboardEvent): void {
    const translationGroup = getInlineImagePasteTarget();
    if (!translationGroup || !dataTransferHoldsJustAPicture(e.clipboardData!))
        return;
    e.preventDefault();
    e.stopImmediatePropagation();
    pasteClipboardImageAsInlineImage(translationGroup);
}
