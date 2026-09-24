import {
    IImageInfo,
    changeImageInfo,
    notifyToolOfChangedImage,
} from "./bloomEditing";
import { normalizeCoverImageDesignation } from "./bloomImages";
import {
    getCanvasElementRotation,
    setCanvasElementRotation,
} from "./canvasElementManager/canvasElementRotation";
import { kCanvasElementSelector } from "../toolbox/canvas/canvasElementConstants";

export interface IImageCropInfo {
    width: string;
    height: string;
    left: string;
    top: string;
}

// The inline size and place of a canvas element box, as written, so that an undo can put back
// exactly what was there, including nothing at all.
export interface IElementGeometry {
    width: string;
    height: string;
    left: string;
    top: string;
}

type ImageOperationUndoItem =
    | {
          kind: "restoreImage";
          element: HTMLElement;
          imageInfo: IImageInfo;
          cropInfo: IImageCropInfo;
          // The img's inline transform: the rotation and mirror of the picture being replaced.
          imageTransform: string;
          // The size and place of the canvas element that holds the picture. A page
          // background element takes the shape of its picture, crop and rotation, and the
          // size code keeps a cropped picture's element shape, so Undo has to put this back
          // before it runs. Undefined when the picture is in no canvas element.
          elementGeometry: IElementGeometry | undefined;
      }
    // Rotate right, Flip, and a drag of the rotation handle. The picture keeps its file and
    // its metadata, so the things to put back are the rotation of the canvas element box, the
    // rotation and mirror of the picture, the crop, and the size and place of the box itself: a
    // rotation of a page background picture changes the shape of its box, because the box takes the
    // shape of the picture. There is no img when the rotation handle rotates a text box or a
    // video.
    | {
          kind: "restoreImageTransform";
          canvasElement: HTMLElement;
          img: HTMLImageElement | undefined;
          elementRotation: number;
          imageTransform: string;
          cropInfo: IImageCropInfo;
          elementGeometry: IElementGeometry;
          // The contents of the element's text boxes, formatting included, when the step was
          // recorded. Typing or formatting afterwards is a newer step, which the text editor's
          // own undo takes back first; see canUndoImageOperation.
          textWhenRecorded: string;
      };
// | {
//       kind: "removeElement";
//       element: HTMLElement;
//   };
export interface ImageUndoManagerHost {
    getCurrentPage(): HTMLElement | undefined;
    updateCanvasElementForChangedImage(
        imgOrImageContainer: HTMLElement,
        cropInfo?: IImageCropInfo,
        imageTransform?: string,
    ): void;
    // Put the frame, the game target and the tool panel back in step after an undo that only
    // changed a rotation, a mirror or a crop.
    updateCanvasElementAfterTransformChange(
        canvasElement: HTMLElement,
        img: HTMLImageElement | undefined,
    ): void;
    getActiveElement(): HTMLElement | undefined;
    setActiveElement(element: HTMLElement | undefined): void;
    removeDetachedTargets(): void;
    updateCanvasElementClass(bloomCanvas: HTMLElement): void;
}

export class ImageUndoManager {
    private imageOperationUndoStack: ImageOperationUndoItem[] = [];
    private pendingImageOperationUndo: ImageOperationUndoItem | undefined;
    private pageIdForImageOperationUndo: string | undefined;

    public constructor(private host: ImageUndoManagerHost) {}

    // Image undo is intentionally two-phase:
    // 1) prepare captures the pre-change state while the original image is intact.
    // 2) commit pushes that state only after the intended replacement actually happened on that element.
    // This avoids adding undo records for canceled/failed/mismatched operations.
    public prepareUndoForImageOperation(imageOrContainer: HTMLElement): void {
        this.clearImageOperationUndoOnPageChange();
        this.pendingImageOperationUndo = {
            kind: "restoreImage",
            element: imageOrContainer,
            imageInfo: this.getCurrentImageInfo(imageOrContainer),
            cropInfo: this.getCurrentImageCropInfo(imageOrContainer),
            imageTransform:
                this.getImageElement(imageOrContainer)?.style.transform ?? "",
            elementGeometry: this.getElementGeometry(
                this.getCanvasElement(imageOrContainer),
            ),
        };
    }

    /**
     * Record what Rotate right or Flip is about to change, so that Undo can put it back. This
     * is one phase, not two: the command cannot fail or be canceled, so there is nothing to
     * wait for before we keep the record.
     */
    public pushUndoForImageTransform(canvasElement: HTMLElement): void {
        this.pushTransformUndo(
            canvasElement,
            getCanvasElementRotation(canvasElement),
        );
    }

    /**
     * Record a drag of the rotation handle, so that Undo can put the angle back. The caller
     * gives the angle the element had when the drag started, because by the time the drag
     * ends the element is already rotated. It calls this only when the angle really changed,
     * so a click on the handle leaves nothing for Undo to do.
     */
    public pushUndoForCanvasElementRotation(
        canvasElement: HTMLElement,
        rotationBeforeDrag: number,
    ): void {
        this.pushTransformUndo(canvasElement, rotationBeforeDrag);
    }

    private pushTransformUndo(
        canvasElement: HTMLElement,
        elementRotation: number,
    ): void {
        this.clearImageOperationUndoOnPageChange();
        // A rotation handle rotates text boxes as well, so there is not always a
        // picture. The rotation and the crop of the picture are only in the record when there is.
        const img = this.getImageElement(canvasElement);
        this.imageOperationUndoStack.push({
            kind: "restoreImageTransform",
            canvasElement,
            img,
            elementRotation,
            imageTransform: img?.style.transform ?? "",
            cropInfo: {
                width: img?.style.width ?? "",
                height: img?.style.height ?? "",
                left: img?.style.left ?? "",
                top: img?.style.top ?? "",
            },
            elementGeometry: this.getElementGeometry(canvasElement),
            textWhenRecorded: textBoxContents(canvasElement),
        });
    }

    /** Clear all pending/recorded image operation undo state. */
    public clearImageOperationUndoState(): void {
        this.imageOperationUndoStack = [];
        this.pendingImageOperationUndo = undefined;
    }

    /** Commit the pending image operation undo after the replacement has actually happened. */
    public commitPendingImageOperationUndo(
        imageOrContainer: HTMLElement,
    ): void {
        this.clearImageOperationUndoOnPageChange();
        if (
            this.pendingImageOperationUndo &&
            this.pendingImageOperationUndo.kind === "restoreImage" &&
            this.pendingImageOperationUndo.element === imageOrContainer
        ) {
            this.imageOperationUndoStack.push(this.pendingImageOperationUndo);
            this.pendingImageOperationUndo = undefined;
        }
    }

    // /** Record a new canvas element that can be removed by Undo. */
    // public pushUndoForNewPastedImage(newElement: HTMLElement): void {
    //     this.clearImageOperationUndoOnPageChange();
    //     this.imageOperationUndoStack.push({
    //         kind: "removeElement",
    //         element: newElement,
    //     });
    // }

    /** Tell the root undo command whether a pasted or deleted image can be undone. */
    public canUndoImageOperation(): boolean {
        this.clearImageOperationUndoOnPageChange();
        const activeElement = this.host.getActiveElement();
        const topOfStack =
            this.imageOperationUndoStack[
                this.imageOperationUndoStack.length - 1
            ];
        if (topOfStack?.kind === "restoreImageTransform") {
            // The rotation handle rotates text boxes too, so this record does not
            // need a picture. We ask instead that the element it belongs to is the selected
            // one, which is the same idea as one undo stack for each text box. If its text has
            // changed since, the typing is newer than the rotation, so we leave Undo to the text
            // editor until the text is back the way it was.
            return (
                !!activeElement &&
                activeElement === topOfStack.canvasElement &&
                textBoxContents(activeElement) === topOfStack.textWhenRecorded
            );
        }
        if (topOfStack?.kind === "restoreImage") {
            // Undo puts back the picture of the element the record belongs to, so it is
            // offered only while that element is selected; otherwise Undo with picture A
            // selected would change picture B.
            return (
                !!activeElement &&
                (activeElement.contains(topOfStack.element) ||
                    topOfStack.element.contains(activeElement))
            );
        }
        let onImageContainer = false;
        if (activeElement) {
            onImageContainer =
                activeElement.getElementsByClassName("bloom-imageContainer")
                    .length > 0 ||
                activeElement.closest(".bloom-imageContainer") !== null;
        }
        return this.imageOperationUndoStack.length > 0 && onImageContainer;
    }

    /** Undo the most recent image operation, if there is one. */
    public undoImageOperation(): boolean {
        this.clearImageOperationUndoOnPageChange();
        const undoItem = this.imageOperationUndoStack.pop();
        if (!undoItem) {
            return false;
        }

        switch (undoItem.kind) {
            case "restoreImage": {
                changeImageInfo(undoItem.element, undoItem.imageInfo);
                const canvasElement = this.getCanvasElement(undoItem.element);
                if (canvasElement && undoItem.elementGeometry) {
                    this.setElementGeometry(
                        canvasElement,
                        undoItem.elementGeometry,
                    );
                }
                this.host.updateCanvasElementForChangedImage(
                    undoItem.element,
                    undoItem.cropInfo,
                    undoItem.imageTransform,
                );
                const page = undoItem.element.closest(
                    ".bloom-page",
                ) as HTMLElement | null;
                if (page) {
                    normalizeCoverImageDesignation(page);
                }
                const img = this.getImageElement(undoItem.element);
                notifyToolOfChangedImage(img);
                return true;
            }
            case "restoreImageTransform": {
                setCanvasElementRotation(
                    undoItem.canvasElement,
                    undoItem.elementRotation,
                );
                if (undoItem.img) {
                    undoItem.img.style.transform = undoItem.imageTransform;
                    undoItem.img.style.width = undoItem.cropInfo.width;
                    undoItem.img.style.height = undoItem.cropInfo.height;
                    undoItem.img.style.left = undoItem.cropInfo.left;
                    undoItem.img.style.top = undoItem.cropInfo.top;
                }
                this.setElementGeometry(
                    undoItem.canvasElement,
                    undoItem.elementGeometry,
                );
                // This also tells the tool panel about the change.
                this.host.updateCanvasElementAfterTransformChange(
                    undoItem.canvasElement,
                    undoItem.img,
                );
                return true;
            }
            // case "removeElement": {
            //     const parent = undoItem.element.parentElement;
            //     const activeWasRemoved =
            //         this.host.getActiveElement() === undoItem.element ||
            //         !!this.host.getActiveElement()?.contains(undoItem.element);
            //     undoItem.element.remove();
            //     this.host.removeDetachedTargets();
            //     if (parent) {
            //         this.host.updateCanvasElementClass(parent);
            //         const page = parent.closest(
            //             ".bloom-page",
            //         ) as HTMLElement | null;
            //         if (page) {
            //             normalizeCoverImageDesignation(page);
            //         }
            //     }
            //     if (activeWasRemoved) {
            //         this.host.setActiveElement(undefined);
            //     }
            //     notifyToolOfChangedImage();
            //     return true;
            // }
        }

        return false;
    }

    private clearImageOperationUndoOnPageChange(): void {
        const currentPage = this.host.getCurrentPage();
        let currentPageId =
            currentPage?.getAttribute("data-page-id") ?? undefined;
        if (this.pageIdForImageOperationUndo !== currentPageId) {
            this.clearImageOperationUndoState();
            this.pageIdForImageOperationUndo = currentPageId;
        }
    }

    private getCurrentImageInfo(imageOrContainer: HTMLElement): IImageInfo {
        const image = this.getImageElement(imageOrContainer);
        return {
            imageId: "",
            src: image?.getAttribute("src") ?? "",
            copyright: imageOrContainer.getAttribute("data-copyright") ?? "",
            creator: imageOrContainer.getAttribute("data-creator") ?? "",
            license: imageOrContainer.getAttribute("data-license") ?? "",
            undoable: "false",
        };
    }

    private getCurrentImageCropInfo(
        imageOrContainer: HTMLElement,
    ): IImageCropInfo {
        const image = this.getImageElement(imageOrContainer);
        return {
            width: imageOrContainer.style.width || image?.style.width || "",
            height: imageOrContainer.style.height || image?.style.height || "",
            left: imageOrContainer.style.left || image?.style.left || "",
            top: imageOrContainer.style.top || image?.style.top || "",
        };
    }

    // The canvas element that holds this img or image container, if there is one.
    private getCanvasElement(
        imageOrContainer: HTMLElement,
    ): HTMLElement | undefined {
        return (
            (imageOrContainer.closest(
                kCanvasElementSelector,
            ) as HTMLElement | null) ?? undefined
        );
    }

    // The inline size and place of a canvas element, as written. Undefined for no element.
    private getElementGeometry(canvasElement: HTMLElement): IElementGeometry;
    private getElementGeometry(
        canvasElement: HTMLElement | undefined,
    ): IElementGeometry | undefined;
    private getElementGeometry(
        canvasElement: HTMLElement | undefined,
    ): IElementGeometry | undefined {
        if (!canvasElement) {
            return undefined;
        }
        return {
            width: canvasElement.style.width,
            height: canvasElement.style.height,
            left: canvasElement.style.left,
            top: canvasElement.style.top,
        };
    }

    // Write back a size and place that getElementGeometry recorded.
    private setElementGeometry(
        canvasElement: HTMLElement,
        geometry: IElementGeometry,
    ): void {
        canvasElement.style.width = geometry.width;
        canvasElement.style.height = geometry.height;
        canvasElement.style.left = geometry.left;
        canvasElement.style.top = geometry.top;
    }

    private getImageElement(
        imageOrContainer: HTMLElement,
    ): HTMLImageElement | undefined {
        return imageOrContainer.tagName === "IMG"
            ? (imageOrContainer as HTMLImageElement)
            : (imageOrContainer.getElementsByTagName("img")[0] ?? undefined);
    }
}

let theOneImageUndoManager: ImageUndoManager | undefined;

export function initializeImageUndoManager(host: ImageUndoManagerHost): void {
    if (theOneImageUndoManager) {
        throw new Error("Image undo manager has already been initialized");
    }
    theOneImageUndoManager = new ImageUndoManager(host);
}

function getImageUndoManager(): ImageUndoManager {
    if (!theOneImageUndoManager) {
        throw new Error("Image undo manager has not been initialized");
    }
    return theOneImageUndoManager;
}

export function prepareUndoForImageOperation(
    imageOrContainer: HTMLElement,
): void {
    getImageUndoManager().prepareUndoForImageOperation(imageOrContainer);
}

// The markup inside the element's text boxes, which changes with typing and with formatting such
// as bold. It leaves out the text boxes' own attributes, which the editor changes on focus.
function textBoxContents(canvasElement: HTMLElement): string {
    return Array.from(canvasElement.getElementsByClassName("bloom-editable"))
        .map((editable) => editable.innerHTML)
        .join("\n");
}

export function pushUndoForImageTransform(canvasElement: HTMLElement): void {
    getImageUndoManager().pushUndoForImageTransform(canvasElement);
}

export function pushUndoForCanvasElementRotation(
    canvasElement: HTMLElement,
    rotationBeforeDrag: number,
): void {
    getImageUndoManager().pushUndoForCanvasElementRotation(
        canvasElement,
        rotationBeforeDrag,
    );
}

export function clearImageOperationUndoState(): void {
    getImageUndoManager().clearImageOperationUndoState();
}

export function commitPendingImageOperationUndo(
    imageOrContainer: HTMLElement,
): void {
    getImageUndoManager().commitPendingImageOperationUndo(imageOrContainer);
}

// export function pushUndoForNewPastedImage(newElement: HTMLElement): void {
//     getImageUndoManager().pushUndoForNewPastedImage(newElement);
// }

export function canUndoImageOperation(): boolean {
    return getImageUndoManager().canUndoImageOperation();
}

export function undoImageOperation(): boolean {
    return getImageUndoManager().undoImageOperation();
}
