import { Bubble, Comical } from "comicaljs";
import { renderCanvasElementContextControls } from "./CanvasElementContextControls";
import {
    getBackgroundImageFromBloomCanvas,
    getImageFromCanvasElement,
    getImageFromContainer,
    HandleImageError,
    isPlaceHolderImage,
    SetupMetadataButton,
} from "../bloomImages";
import { wrapWithRequestPageContentDelay } from "../bloomEditing";
import { getExactClientSize } from "../../../utils/elementUtils";
import {
    kBackgroundImageClass,
    kBloomCanvasClass,
    kCanvasElementClass,
    kHasCanvasElementClass,
} from "../../toolbox/canvas/canvasElementConstants";

export interface ICanvasElementBackgroundImageManagerHost {
    getAllBloomCanvasesOnPage: () => HTMLElement[];
    adjustChildrenIfSizeChanged: (bloomCanvas: HTMLElement) => void;
    getActiveElement: () => HTMLElement | undefined;
    alignControlFrameWithActiveElement: () => void;
    pxToNumber: (px: string, fallback?: number) => number;
}

export class CanvasElementBackgroundImageManager {
    private host: ICanvasElementBackgroundImageManagerHost;
    private pageContentDelayRequestId = "adjustBackgroundImageSize";

    // Track background image load listener to prevent duplicates.
    // Even if adjustBackgroundImageSize is somehow running simultaneously on different images and they race
    // on these, currently nothing bad can happen (worst case we leave around an event listener
    // that does nothing when triggered).
    private bgImageLoadListener: ((event: Event) => void) | undefined;

    public constructor(host: ICanvasElementBackgroundImageManagerHost) {
        this.host = host;
    }

    private clearImageLoadListener(img: HTMLImageElement) {
        if (this.bgImageLoadListener) {
            img.removeEventListener("load", this.bgImageLoadListener);
            this.bgImageLoadListener = undefined;
        }
    }

    // This should not be needed, ideally there should be no old-style bg image,
    // and if there is one, we should not touch it if there is a bg canvas element.
    // But in BL-14788 this had become true by mistake, and could have happened in
    // books where users did things with image overlays resulting in old and new image
    // representations in the same image container. We no longer save this state,
    // and no longer reproduce all of it in old books, because of changes in
    // convertLegacyFixedPagesToImageOverlays in bloomEditing.ts, but this cleanup
    // seems to deal with any case I can think up where old and new both exist.
    // Once we are sure all old books are converted by the new code, this probably
    // can be deleted.
    // But I'm leaving the code for now, because last I heard, we want to use this (or some variation of it)
    // at publish time to set the image containers back to the original, more simple state.
    public revertBackgroundCanvasElements = (): void => {
        for (const bgo of Array.from(
            document.getElementsByClassName(kBackgroundImageClass),
        )) {
            const bgImage = getImageFromCanvasElement(bgo as HTMLElement);
            const mainImage = getImageFromContainer(
                bgo.parentElement as HTMLElement,
            );
            if (bgImage && mainImage) {
                // Note that we must use get/setAttribute here rather than e.g. mainImage.src (a property
                // of HTMLImageElement) because the src property is a full URL, and we want to preserve
                // what is actually stored in the src attribute, the path relative to the book file.
                mainImage.setAttribute(
                    "src",
                    bgImage.getAttribute("src") || "",
                );
                // maintain the intellectual properties of the image (BL-14511)
                const copyright = bgImage.getAttribute("data-copyright");
                if (copyright) {
                    mainImage.setAttribute("data-copyright", copyright);
                } else {
                    mainImage.removeAttribute("data-copyright");
                }
                const creator = bgImage.getAttribute("data-creator");
                if (creator) {
                    mainImage.setAttribute("data-creator", creator);
                } else {
                    mainImage.removeAttribute("data-creator");
                }
                const license = bgImage.getAttribute("data-license");
                if (license) {
                    mainImage.setAttribute("data-license", license);
                } else {
                    mainImage.removeAttribute("data-license");
                }
                bgo.remove();
            }
        }
    };

    public handleResizeAdjustments = (): void => {
        const bloomCanvases = this.host.getAllBloomCanvasesOnPage();
        bloomCanvases.forEach((bloomCanvas) => {
            this.switchBackgroundToCanvasElementIfNeeded(bloomCanvas);
            this.host.adjustChildrenIfSizeChanged(bloomCanvas);
        });
    };

    // If a bloom-canvas has a non-placeholder background image, we switch the
    // background image to an image canvas element. This allows it to be manipuluated more easily.
    // More importantly, it prevents the difficult-to-account-for movement of the
    // background image when the container is resized. Once it is a canvas element,
    // we can apply our algorithm to adjust all the canvas elements together when the container
    // is resized. A further benefit is that it is somewhat backwards compatible:
    // older code will not mess with canvas element positioning like it would tend to
    // if we put position and size attributes on the background image directly.
    private switchBackgroundToCanvasElementIfNeeded(bloomCanvas: HTMLElement) {
        const bgCanvasElement = bloomCanvas.getElementsByClassName(
            kBackgroundImageClass,
        )[0] as HTMLElement;
        if (bgCanvasElement) {
            // I think this is redundant, but it got added by mistake at one point,
            // and will hide the placeholder if it's there, so make sure it's not.
            bgCanvasElement.classList.remove(kHasCanvasElementClass);
            return; // already have one.
        }
        this.switchBackgroundToCanvasElement(bloomCanvas);
    }

    private switchBackgroundToCanvasElement(bloomCanvas: HTMLElement) {
        const oldBgImage = getImageFromContainer(bloomCanvas);
        let bgCanvasElement = bloomCanvas.getElementsByClassName(
            kBackgroundImageClass,
        )[0] as HTMLElement;
        if (!bgCanvasElement) {
            // various legacy behavior, such as hiding the old-style background placeholder.
            bloomCanvas.classList.add(kHasCanvasElementClass);
            bgCanvasElement = document.createElement("div");
            bgCanvasElement.classList.add(kCanvasElementClass);
            bgCanvasElement.classList.add(kBackgroundImageClass);

            // Make a new image-container to hold just the background image, inside the new canvas element.
            // We don't want a deep clone...that will copy all the canvas elements, too.
            // I'm not sure how much good it does to clone rather than making a new one, now the classes are
            // not the same.
            const newImgContainer = bloomCanvas.cloneNode(false) as HTMLElement;
            newImgContainer.classList.add("bloom-imageContainer");
            newImgContainer.classList.remove(kBloomCanvasClass);
            newImgContainer.classList.remove(kHasCanvasElementClass);
            bgCanvasElement.appendChild(newImgContainer);
            let newImg: HTMLElement;
            if (oldBgImage) {
                // If we have an image, we want to clone it and put it in the new image-container.
                // (Could just move it, but that complicates the code for inserting the canvas element.)
                newImg = oldBgImage.cloneNode(false) as HTMLElement;
            } else {
                // Otherwise, we'll make a placeholder image. Src may get set below.
                newImg = document.createElement("img");
                newImg.setAttribute("src", "placeHolder.png");
            }
            newImg.classList.remove("bloom-imageLoadError");
            newImgContainer.appendChild(newImg);

            // Set level so Comical will consider the new canvas element to be under the existing ones.
            const canvasElementElements = Array.from(
                bloomCanvas.getElementsByClassName(kCanvasElementClass),
            ) as HTMLElement[];
            this.putBubbleBefore(bgCanvasElement, canvasElementElements, 1);
            bgCanvasElement.style.visibility = "none"; // hide it until we adjust its shape and position
            // consistent with level, we want it in front of the (new, placeholder) background image
            // and behind the other canvas elements.
            if (oldBgImage) {
                bloomCanvas.insertBefore(
                    bgCanvasElement,
                    oldBgImage.nextSibling,
                );
            } else {
                const canvas = bloomCanvas.getElementsByTagName(
                    "canvas",
                )[0] as HTMLElement;
                if (canvas) {
                    bloomCanvas.insertBefore(
                        bgCanvasElement,
                        canvas.nextSibling,
                    );
                } else {
                    // Some old books can be in this state.  See BL-15298.
                    // Put it at the start of the bloom-canvas. This is safer than appending because
                    // we want the implicit z-order of the background image to be at the back.
                    bloomCanvas.prepend(bgCanvasElement);
                }
            }
        }
        const bgImage = getBackgroundImageFromBloomCanvas(
            bloomCanvas,
        ) as HTMLElement; // must exist by now
        // Whether it's a new bgImage or not, copy its src from the old-style img
        bgImage.classList.remove("bloom-imageLoadError");
        bgImage.onerror = HandleImageError;
        bgImage.setAttribute(
            "src",
            oldBgImage?.getAttribute("src") ?? "placeHolder.png",
        );
        this.adjustBackgroundImageSize(bloomCanvas, bgCanvasElement, true);
        bgCanvasElement.style.visibility = ""; // now we can show it, if it was new and hidden
        SetupMetadataButton(bloomCanvas);
        if (oldBgImage) {
            oldBgImage.remove();
        }
    }

    // Adjust the levels of all the bubbles of all the listed canvas elements so that
    // the one passed can be given the required level and all the others (keeping their
    // current order) will be perceived by ComicalJs as having a higher level
    private putBubbleBefore(
        canvasElement: HTMLElement,
        canvasElementElements: HTMLElement[],
        requiredLevel: number,
    ) {
        let minLevel = Math.min(
            ...canvasElementElements.map(
                (b) => Bubble.getBubbleSpec(b as HTMLElement).level ?? 0,
            ),
        );
        if (minLevel <= requiredLevel) {
            // bump all the others up so we can insert one at level 1 below them all
            // We don't want to use zero as a level...some Comical code complains that
            // the canvas element doesn't have a level at all. And I'm nervous about using
            // negative numbers...something that wants a level one higher might get zero.
            canvasElementElements.forEach((b) => {
                const bubble = new Bubble(b as HTMLElement);
                const spec = bubble.getBubbleSpec();
                // the one previously at minLevel will now be at requiredLevel+1, others higher in same sequence.
                spec.level += requiredLevel - minLevel + 1;
                bubble.persistBubbleSpec();
            });
            minLevel = 2;
        }
        const bubble = new Bubble(canvasElement as HTMLElement);
        bubble.getBubbleSpec().level = requiredLevel;
        bubble.persistBubbleSpec();
        Comical.update(canvasElement.parentElement as HTMLElement);
    }

    public adjustBackgroundImageSize = (
        bloomCanvas: HTMLElement,
        bgCanvasElement: HTMLElement,
        useSizeOfNewImage: boolean,
    ): void => {
        // adjustBackgroundImageSizeToFit may wait for the image to load and make modifications after,
        // and we want to make sure those modifications are included in any save that occurs in the meantime.
        // wrapWithRequestPageContentDelay will add the delay before calling the function and remove it
        // when the promise settles.
        wrapWithRequestPageContentDelay(
            () =>
                this.adjustBackgroundImageSizeToFit(
                    bloomCanvas,
                    bgCanvasElement,
                    useSizeOfNewImage,
                ),
            this.pageContentDelayRequestId,
        );
    };

    // Given a bg canvas element, which is a canvas element having the bloom-backgroundImage
    // class, and the height and width of the parent bloom-canvas, this method attempts to
    // make the bgCanvasElement the right size and position to fill as much as possible of the parent,
    // rather like object-fit:contain. It is used in two main scenarios: the user may have
    // selected a different image, which means we must adjust to suit a different image aspect
    // ratio. Or, the size of the container may have changed, e.g., using origami. We must also
    // account for the possibility that the image has been cropped, in which case, we want to
    // keep the cropped aspect ratio. (Cropping attributes will already have been removed if it
    // is a new image.)
    // Things are complicated because it's possible the image has not loaded yet, so we can't
    // get its natural dimensions to figure an aspect ratio. In this case, the method arranges
    // to be called again after the image loads or a timeout.
    // A further complication is that the image may fail to load, so we never get natural
    // dimensions. In this case, we expand the bgCanvasElement to the full size of the container so
    // all the space is available to display the error icon and message.
    private adjustBackgroundImageSizeToFit(
        bloomCanvas: HTMLElement,
        // The canvas element div that contains the background image.
        // (Since this is the background that we overlay things on, it is itself a
        // canvas element only in the sense that it has the same HTML structure in order to
        // allow many commands and functions to work on it as if it were an ordinary canvas element.)
        bgCanvasElement: HTMLElement,
        // if this is set true, we've updated the src of the background image and want to
        // ignore any cropping (assumes the img doesn't have any
        // cropping-related style settings) and just adjust the canvas element to fit the image.
        // We'll always have to wait for it to load in this case, otherwise, we may get
        // the dimensions of a previous image.
        useSizeOfNewImage: boolean,
    ): Promise<void> {
        const { width: bloomCanvasWidth, height: bloomCanvasHeight } =
            getExactClientSize(bloomCanvas);
        let imgAspectRatio =
            bgCanvasElement.clientWidth / bgCanvasElement.clientHeight;
        const img = getImageFromCanvasElement(bgCanvasElement);
        let failedImage = false;
        // We don't ever expect there not to be an img. If it happens, we'll just do nothing.
        if (!img) {
            return Promise.resolve();
        }
        // The image may not have loaded yet or may have failed to load.  If either of these
        // cases is true, then the naturalHeight and naturalWidth will be zero.  If the image
        // failed to load, a special class is added to the image to indicate this fact (if all
        // goes well).  However, we may know that this is called in response to a new image, in
        // which case the class may not have been added yet.
        // We conclude that the image has truly failed if 1) we don't have natural dimensions set
        // to something other than zero, 2) we are not waiting for new dimensions, and 3) the
        // image has the special class indicating that it failed to load.  (The class is supposed
        // to be removed when we change the src attribute, which leads to a new load attempt.)
        failedImage =
            // As of BL-15441, we use css instead of real placeHolder.png files but still set src="placeHolder.png"
            // to indicate placeholders. Treat this case as a failed image for dimensions purposes
            isPlaceHolderImage(img.getAttribute("src")) ||
            (img.naturalHeight === 0 && // not loaded successfully (yet)
                !useSizeOfNewImage && // not waiting for new dimensions
                img.classList.contains("bloom-imageLoadError")); // error occurred while trying to load
        if (failedImage) {
            // If the image failed to load, just use the container aspect ratio to fill up
            // the container with the error message (alt attribute string).
            imgAspectRatio = bloomCanvasWidth / bloomCanvasHeight;
        } else if (
            img.naturalHeight === 0 ||
            img.naturalWidth === 0 ||
            useSizeOfNewImage
        ) {
            // if we don't have a height and width, or we know the image src changed
            // and have not yet waited for new dimensions, go ahead and wait.
            // Return a promise that resolves when the image loads or after a timeout.
            return new Promise<void>((resolve) => {
                const handle = setTimeout(
                    () => {
                        this.adjustBackgroundImageSizeToFit(
                            bloomCanvas,
                            bgCanvasElement,
                            // after the timeout we don't consider that we MUST wait if we have dimensions
                            false,
                        ).then(resolve);
                    },
                    // I think this is long enough that we won't be seeing obsolete data (from a previous src).
                    // OTOH it's not hopelessly long for the user to wait when we don't get an onload.
                    // If by any chance this happens when the image really isn't loaded enough to
                    // have naturalHeight/Width, the zero checks above will force another iteration.
                    100,
                    // somehow Typescript is confused and thinks this is a NodeJS version of setTimeout.
                ) as unknown as number;
                // preferably we update when we are loaded.
                // Remove any existing listener to prevent duplicates.
                this.clearImageLoadListener(img);
                // Store the listener so the timer can remove it if it's no longer needed.
                // If this method somehow runs simultaneously on different images, the worst this should
                // cause is redundant promise resolution attempts, which are ignored.
                this.bgImageLoadListener = () => {
                    clearTimeout(handle);
                    this.adjustBackgroundImageSizeToFit(
                        bloomCanvas,
                        bgCanvasElement,
                        false, // when this call happens we have the new dimensions.
                    ).then(resolve);
                    this.bgImageLoadListener = undefined;
                };
                img.addEventListener("load", this.bgImageLoadListener, {
                    once: true,
                });
            });
        } else if (img.style.width) {
            // there is established cropping. Use the cropped size to determine the
            // aspect ratio.
            imgAspectRatio =
                this.host.pxToNumber(bgCanvasElement.style.width) /
                this.host.pxToNumber(bgCanvasElement.style.height);
        } else {
            // not cropped, so we can use the natural dimensions
            imgAspectRatio = img.naturalWidth / img.naturalHeight;
        }

        const oldCeWidth = this.host.pxToNumber(
            bgCanvasElement.style.width,
            bgCanvasElement.clientWidth,
        );
        const oldCeHeight = this.host.pxToNumber(
            bgCanvasElement.style.height,
            bgCanvasElement.clientHeight,
        );
        const containerAspectRatio = bloomCanvasWidth / bloomCanvasHeight;
        const fitCoverMode = img?.classList.contains(
            "bloom-imageObjectFit-cover",
        );
        let matchWidthOfContainer = imgAspectRatio > containerAspectRatio;
        if (fitCoverMode) {
            // In case it is NOT already cropped, its size will be 100%, so we must capture
            // this before we change the parent.
            const oldImgWidth =
                this.host.pxToNumber(img.style.width) || img.clientWidth;
            // make the canvas element fill the container
            bgCanvasElement.style.width = bloomCanvasWidth + "px";
            bgCanvasElement.style.height = bloomCanvasHeight + "px";
            bgCanvasElement.style.left = "0px";
            bgCanvasElement.style.top = "0px";
            //
            matchWidthOfContainer = !matchWidthOfContainer;
            // This is the height it would be if not cropped.
            const oldImgHeight =
                (oldImgWidth * img.naturalHeight) / img.naturalWidth;
            const oldImgLeft = this.host.pxToNumber(img.style.left) || 0;
            const oldImgTop = this.host.pxToNumber(img.style.top) || 0; // negative
            // crop the image (or adjust its cropping) to fill the container
            if (matchWidthOfContainer) {
                // image is taller than a perfect fit, so it will fill the width and be cropped
                // (more than before) in height.
                const ceScale = bgCanvasElement.clientWidth / oldCeWidth;
                const minScale = bgCanvasElement.clientWidth / oldImgWidth;
                const scale = Math.max(ceScale, minScale);
                img.style.width = oldImgWidth * scale + "px";
                img.style.left = oldImgLeft * scale + "px"; //same fraction cropped in width
                const previouslyHiddenAtTop = -oldImgTop * scale;
                const previouslyHiddenAtBottom =
                    (oldImgHeight + oldImgTop - oldCeHeight) * scale;
                // this might be negative, if the container got shorter in aspect ratio.
                // That is, possibly keeping the same top cropping would leave space at the bottom
                const excessHeight =
                    oldImgHeight * scale -
                    bloomCanvasHeight -
                    previouslyHiddenAtTop -
                    previouslyHiddenAtBottom;
                img.style.top =
                    Math.min(-previouslyHiddenAtTop - excessHeight / 2, 0) +
                    "px";
            } else {
                // image is wider than a perfect fit, so it will fill the height and be cropped
                // (more than before) in width.
                const ceScale = bgCanvasElement.clientHeight / oldCeHeight;
                // we must scale it up enough to fill the height of the container.
                const minScale = bgCanvasElement.clientHeight / oldImgHeight;
                const scale = Math.max(ceScale, minScale);
                img.style.width = oldImgWidth * scale + "px";
                img.style.top = oldImgTop * scale + "px"; //same fraction cropped in height
                const previouslyHiddenAtLeft = -oldImgLeft * scale;
                const previouslyHiddenAtRight =
                    (oldImgWidth + oldImgLeft - oldCeWidth) * scale;
                const excessWidth =
                    oldImgWidth * scale -
                    bloomCanvasWidth -
                    previouslyHiddenAtLeft -
                    previouslyHiddenAtRight;
                img.style.left =
                    Math.min(-previouslyHiddenAtLeft - excessWidth / 2, 0) +
                    "px";
            }
        } else {
            if (matchWidthOfContainer) {
                // size of image is width-limited: image is wider than a perfect fit,
                // so it will fill the width of the container and have a smaller height.
                bgCanvasElement.style.width = bloomCanvasWidth + "px";
                bgCanvasElement.style.left = "0px";
                const imgHeight = bloomCanvasWidth / imgAspectRatio;
                bgCanvasElement.style.top =
                    (bloomCanvasHeight - imgHeight) / 2 + "px";
                bgCanvasElement.style.height = imgHeight + "px";
            } else {
                const imgWidth = bloomCanvasHeight * imgAspectRatio;
                bgCanvasElement.style.width = imgWidth + "px";
                bgCanvasElement.style.top = "0px";
                bgCanvasElement.style.left =
                    (bloomCanvasWidth - imgWidth) / 2 + "px";
                bgCanvasElement.style.height = bloomCanvasHeight + "px";
            }
            // If the image was cropped, we want to adjust the cropping to the new size.
            // If it wasn't cropped, we want to leave it alone (it will default to fitting
            // within the canvas element).
            // Note that if useSizeOfNewImage is true, we assume there is no cropping yet,
            // so we don't do this adjustment.
            if (!useSizeOfNewImage && img?.style.width) {
                // need to adjust image settings to preserve cropping
                // Note that style.width can have fractional values, while clientWidth is always
                // rounded to an integer value. So we want to use style.width values (if possible)
                // for greater accuracy in scaling. (BL-15464)
                const newCeWidth = this.host.pxToNumber(
                    bgCanvasElement.style.width,
                    bgCanvasElement.clientWidth,
                );
                const scale = newCeWidth / oldCeWidth;
                img.style.width =
                    this.host.pxToNumber(img.style.width) * scale + "px";
                img.style.left =
                    this.host.pxToNumber(img.style.left) * scale + "px";
                img.style.top =
                    this.host.pxToNumber(img.style.top) * scale + "px";
            }
        }
        // Ensure that the missing image message is displayed without being cropped.
        // See BL-14241.
        if (failedImage && img && img.style && img.style.width.length > 0) {
            const imgLeft = this.host.pxToNumber(img.style.left);
            const imgTop = this.host.pxToNumber(img.style.top);
            if (imgLeft < 0 || imgTop < 0) {
                // The failed image was cropped. Remove the cropping to facilitate displaying the error state.
                img.setAttribute(
                    "data-style",
                    `left:${img.style.left}; width:${img.style.width}; top:${img.style.top};`,
                );
                const imgWidth = this.host.pxToNumber(img.style.width);
                console.warn(
                    `Missing image: resetting left from ${imgLeft} to 0, top from ${imgTop} to 0, and width from ${imgWidth} to ${
                        imgWidth + imgLeft
                    }`,
                );
                img.style.left = "0px";
                img.style.top = "0px";
                img.style.width = imgWidth + imgLeft + "px";
            }
        }
        this.host.alignControlFrameWithActiveElement();
        if (bgCanvasElement === this.host.getActiveElement()) {
            // Rerender the image's controls, since we may need to enable the Expand Image button since the size has changed.
            // (When the page is first loaded, we adjust the background image though it is NOT the active element;
            // in that case, we must not try to render the controls as if they belonged to it.)
            renderCanvasElementContextControls(bgCanvasElement, false);
        }
        this.clearImageLoadListener(img);
        return Promise.resolve();
    }
}
