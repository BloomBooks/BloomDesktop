import {
    IImageInfo,
    changeImageInfo,
    notifyToolOfChangedImage,
} from "./bloomEditing";
import { normalizeCoverImageDesignation } from "./bloomImages";

interface IImageCropInfo {
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
      }
    | {
          kind: "removeElement";
          element: HTMLElement;
      };

export interface ImageUndoManagerHost {
    getCurrentPage(): HTMLElement | undefined;
    updateCanvasElementForChangedImage(imgOrImageContainer: HTMLElement): void;
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
        };
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

    /** Record a new canvas element that can be removed by Undo. */
    public pushUndoForNewPastedImage(newElement: HTMLElement): void {
        this.clearImageOperationUndoOnPageChange();
        this.imageOperationUndoStack.push({
            kind: "removeElement",
            element: newElement,
        });
    }

    /** Tell the root undo command whether a pasted or deleted image can be undone. */
    public canUndoImageOperation(): boolean {
        this.clearImageOperationUndoOnPageChange();
        const activeElement = this.host.getActiveElement();
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
                this.host.updateCanvasElementForChangedImage(undoItem.element);
                this.restoreImageCropInfo(undoItem.element, undoItem.cropInfo);
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
            case "removeElement": {
                const parent = undoItem.element.parentElement;
                const activeWasRemoved =
                    this.host.getActiveElement() === undoItem.element ||
                    !!this.host.getActiveElement()?.contains(undoItem.element);
                undoItem.element.remove();
                this.host.removeDetachedTargets();
                if (parent) {
                    this.host.updateCanvasElementClass(parent);
                    const page = parent.closest(
                        ".bloom-page",
                    ) as HTMLElement | null;
                    if (page) {
                        normalizeCoverImageDesignation(page);
                    }
                }
                if (activeWasRemoved) {
                    this.host.setActiveElement(undefined);
                }
                notifyToolOfChangedImage();
                return true;
            }
        }

        return false;
    }

    private clearImageOperationUndoOnPageChange(): void {
        const currentPage = this.host.getCurrentPage();
        let currentPageId =
            currentPage?.getAttribute("data-page-id") ?? undefined;
        if (currentPageId === null) currentPageId = undefined;
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
            width: image?.style.width ?? "",
            height: image?.style.height ?? "",
            left: image?.style.left ?? "",
            top: image?.style.top ?? "",
        };
    }

    private restoreImageCropInfo(
        imageOrContainer: HTMLElement,
        cropInfo: IImageCropInfo,
    ): void {
        const image = this.getImageElement(imageOrContainer);
        if (!image) {
            return;
        }

        image.style.width = cropInfo.width;
        image.style.height = cropInfo.height;
        image.style.left = cropInfo.left;
        image.style.top = cropInfo.top;
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

export function clearImageOperationUndoState(): void {
    getImageUndoManager().clearImageOperationUndoState();
}

export function commitPendingImageOperationUndo(
    imageOrContainer: HTMLElement,
): void {
    getImageUndoManager().commitPendingImageOperationUndo(imageOrContainer);
}

export function pushUndoForNewPastedImage(newElement: HTMLElement): void {
    getImageUndoManager().pushUndoForNewPastedImage(newElement);
}

export function canUndoImageOperation(): boolean {
    return getImageUndoManager().canUndoImageOperation();
}

export function undoImageOperation(): boolean {
    return getImageUndoManager().undoImageOperation();
}
