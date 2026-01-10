import {
    getWithConfig,
    getWithConfigAsync,
    postJson,
} from "../../utils/bloomApi";

// Enhance: this could be turned into a Typescript Module with only two public methods

import theOneLocalizationManager from "../../lib/localizationManager/localizationManager";

import {
    kBackgroundImageClass,
    theOneCanvasElementManager,
    updateCanvasElementClass,
} from "./CanvasElementManager";
import {
    kCanvasElementSelector,
    kBloomCanvasClass,
    kBloomCanvasSelector,
} from "../toolbox/canvas/canvasElementUtils";

import { farthest } from "../../utils/elementUtils";
import { EditableDivUtils } from "./editableDivUtils";
import { playingBloomGame } from "../toolbox/games/DragActivityTabControl";
import { kPlaybackOrderContainerClass } from "../toolbox/talkingBook/audioRecording";
import { showCopyrightAndLicenseDialog } from "../editViewFrame";
import { getCanvasElementManager } from "../toolbox/canvas/canvasElementUtils";
import $ from "jquery";

// This appears to be constant even on higher dpi screens.
// (See http://www.w3.org/TR/css3-values/#absolute-lengths)
const kBrowserDpi = 96;
export const kImageContainerClass = "bloom-imageContainer";
export const kImageContainerSelector = `.${kImageContainerClass}`;

// We don't use actual image-placeholder.png files anymore, but we do continue to use
// src="image-placeholder.png" to mark placeholders
// Versions before 6.3 used the older filename "placeHolder.png".
export function isPlaceHolderImage(url: string | null | undefined): boolean {
    if (!url) {
        return false;
    }
    const normalizedUrl = url.toLowerCase();
    return (
        normalizedUrl.includes("image-placeholder.png") ||
        normalizedUrl.includes("placeholder.png")
    );
}

export function cleanupImages() {
    $(".bloom-imageContainer").css("opacity", ""); //comes in on img containers from an old version of myimgscale, and is a major problem if the image is missing
    $(".bloom-imageContainer").css("overflow", ""); //review: also comes form myimgscale; is it a problem?
    // I'm not clear about the source of the problem we're trying to fix here, so it MIGHT happen
    // on bloom-canvas elements (could they get migrated before some bloom did the fix above?).  So let's keep the old code above and
    // also fix if we see these on bloom-canvas.
    $(kBloomCanvasSelector).css("opacity", "");
    $(kBloomCanvasSelector).css("overflow", "");
}

export function SetupImagesInContainer(container) {
    // Prevent problems in case an ancestor has rtl, e.g. on xmatter pages of RTL language books (BL-15653)
    $(container)
        .find(".bloom-imageContainer, .bloom-videoContainer")
        .css("direction", "ltr");

    $(container)
        .find(".bloom-imageContainer > img") // the ">" here prevents finding img's of ui affordances deep in comics
        .each(function () {
            SetupImage(this);
        });
    // I think this is redundant, but it might be important for a bloom-canvas
    // where the background img has not yet been converted to a background canvas element.
    $(container)
        .find(".bloom-canvas > img") // the ">" here prevents finding img's of ui affordances deep in comics
        .each(function () {
            SetupImage(this);
        });

    $(container)
        .find(kBloomCanvasSelector)
        .each((index, element) => {
            // For now, bookButtons aren't editable
            if (!element.closest(".bloom-bookButton")) {
                SetupBloomCanvas(element as HTMLHtmlElement);
            }
        });

    //todo: this had problems. Check out the later approach, seen in draggableLabel (e.g. move handle on the inside, using a background image on a div)
    $(container)
        .find(".bloom-draggable")
        .mouseenter(function () {
            $(this).prepend(
                "<button class='moveButton' title='Move'></button>",
            );
            $(this)
                .find(".moveButton")
                .mousedown(function (e) {
                    //reviewSlog added the <any>
                    $(this)
                        .parent()
                        .trigger(<any>e);
                });
        });
    $(container)
        .find(".bloom-draggable")
        .mouseleave(function () {
            $(this)
                .find(".moveButton")
                .each(function () {
                    $(this).remove();
                });
        });

    $(container)
        .find("img")
        .each(function () {
            SetAlternateTextOnImages(this);
        });
}

export function SetupImage(image) {
    // Caution! image may or may not be a jQuery object.

    // Remove any obsolete explicit image size and position left over from earlier versions of Bloom, before we had object-fit:contain.
    // Note: any changes to this should probably also be made to (C#) Book.RemoveObsoleteImageAttributes(), if it still exists.
    if (image.style) {
        // Note, in BL-9460 we had to return to having width and height in some cases.
        // As of 6.1, canvas element images make use of explicit width, left, and top in styles for cropping.
        // Since canvas element images were added (2022) long after we switched to object-fit:contain (2018),
        // we can safely suppress removing width and height for those as well as for ones explicitly
        // marked to fix BL-9460 (as of August 2024, the latter is just one cover image in Kyrg2020).
        // Ones in targetWrapper divs are copied from canvas elements and may also have cropping.
        if (
            !$(image.parent).hasClass("bloom-scale-with-code") &&
            !image.closest(kCanvasElementSelector) &&
            !image.closest(".bloom-targetWrapper")
        ) {
            image.style.width = "";
            image.style.height = "";
        }
        image.style.marginLeft = "";
        image.style.marginTop = "";
    }
    if (image.removeAttribute) {
        if (image.getAttribute && image.getAttribute("style") === "") {
            image.removeAttribute("style");
        }
        image.removeAttribute("width");
        image.removeAttribute("height");
    }
    if (image.getAttribute) {
        const hndlr = image.getAttribute("onerror");
        if (!hndlr) {
            // Cover images get a handler assigned in C# code because assigning it here
            // doesn't work for the initial load on cover pages.  (The image resource load
            // fails before this bootstrap code is called in document.ready(), but only
            // for the cover image for some reason.)  The C# code also doesn't preserve
            // the bloom-imageLoadError class from earlier editing sessons.
            // The onerror handler can be null if this is not a cover image and the error
            // handler has not yet been assigned.  In that case, we want to initialize the
            // error handler and class.  See BL-14241.
            // Note that the error handler assigned this way will not be persisted as an
            // attribute in the image element in the HTML.
            image.classList.remove("bloom-imageLoadError");
            image.onerror = HandleImageError;
        }
    }
}

// This handler behaves exactly the same as the one assigned in C# code to the
// cover image elementin BookData.cs/UpdateImageFromDataSet().  Any change here
// should be reflected there as well.  This might be difficult since the C# code
// adds the very simple "this.classList.add('bloom-imageLoadError');" as the content
// of the onerror attribute.  Anything much more complicated would probably require
// more javascript work to bundle up the error handler appropriately for access.
export function HandleImageError(event: Event) {
    const target = event.target as HTMLImageElement;
    target.classList.add("bloom-imageLoadError");
    // console.error("Image failed to load:", target.src);
}

export function doImageCommand(
    img: HTMLElement | undefined,
    command: "copy" | "paste" | "change",
) {
    if (!img) {
        return;
    }
    // get the image id attribute. If it doesn't have one, add it before calling
    // the server api. This is needed to properly identify the image later on in the
    // changeImage method.  The copy command doesn't need to identify the image later,
    // as the source url is enough for that command.
    // (An image usually shouldn't have an id to begin with.)
    let imageId = img.getAttribute("id");
    const imageSrc = GetRawImageUrl(img);
    if (command !== "copy") {
        if (!imageId) {
            imageId = EditableDivUtils.createUuid();
            img.setAttribute("id", imageId);
        }
        // Note that the changeImage method (called from the C# code) will remove the id
        // attribute after using it to find the correct image.  The C# code will call the
        // removeImageId method if it doesn't call the changeImage method.  See BL-13619.
    }

    const topDiv = img.closest(kCanvasElementSelector);
    // Currently Gifs can only be added using the Games tool.
    // A gif is always an img in a canvas element div, and we put a special class
    // on the canvas element
    // and use it in various ways where GIFs need to behave differently from
    // other imgs. (For example, currently, they can only be cut/copied as file
    // paths, we don't support metadata, they can't be cropped,...)
    const imageIsGif = topDiv?.classList.contains("bloom-gif") ?? false;

    postJson("editView/" + command + "Image", {
        imageId,
        imageSrc,
        imageIsGif,
    });
}

export function handleMouseEnterBloomCanvas(bloomCanvas: HTMLElement): void {
    if (!bloomCanvas) {
        return;
    }
    if (playingBloomGame(bloomCanvas)) {
        // I wish this knowledge was not here, but I don't see a better way to prevent image editing
        // and hover effects when in test mode.
        return;
    }
    const img = getBackgroundImageFromBloomCanvas(bloomCanvas);

    if (!img) {
        return;
    }
    SetImageTooltip(bloomCanvas);

    if (
        bloomCanvas.getElementsByClassName(kPlaybackOrderContainerClass)
            .length > 0
    ) {
        return; // Playback order controls are active, deactivate bloom-canvas stuff.
    }
}

function SetupChangeImageButton(bloomCanvas: HTMLElement) {
    for (const oldButton of Array.from(
        bloomCanvas.getElementsByClassName("changeImageButton"),
    )) {
        oldButton.remove();
    }
    const button = bloomCanvas.ownerDocument.createElement("button");
    button.className = `changeImageButton imageButton imageOverlayButton bloom-ui`;
    button.addEventListener("click", (e: MouseEvent) => {
        const img = getBackgroundImageFromBloomCanvas(bloomCanvas);
        if ((e as any).detail > 1 || !img) {
            return;
        }
        doImageCommand(img, "change");
    });
    const titleId = "EditTab.Image.ChangeImage";
    const titleText = "Change image";
    theOneLocalizationManager
        .asyncGetText(titleId, titleText, "tooltip text")
        .done((translation) => {
            button.title = translation;
            bloomCanvas.prepend(button);
        })
        .fail(() => {
            button.title = titleText;
            bloomCanvas.prepend(button);
        });
}

export function handleMouseLeaveBloomCanvas(containerDiv: Element): void {
    DisableImageTooltip(containerDiv as HTMLElement);
}

export function DisableImageEditing(bloomCanvas: HTMLElement) {
    bloomCanvas.classList.add("bloom-hideImageButtons");
    UpdateImageTooltipVisibility(bloomCanvas);
}

/**
 * Undo the effect of calling DisableImageEditing()
 */
export function EnableImageEditing(bloomCanvas: HTMLElement) {
    bloomCanvas.classList.remove("bloom-hideImageButtons");
    UpdateImageTooltipVisibility(bloomCanvas);
}

/**
 * Like EnableImageEditing, but for the entire document instead of a single container.
 */
export function EnableAllImageEditing() {
    // getElementsByClassName returns these in document order.
    // EnableImageEditing has side effects. Whose should win?
    // We'd rather have the first bloom-canvas win, rather than the last child container,
    // so apply these in reverse document order.
    // (This may not be strictly needed because currently, all calls to this function
    // occur when the side effects don't matter. But just in case.)
    Array.from(document.getElementsByClassName("bloom-hideImageButtons"))
        .reverse()
        .forEach(EnableImageEditing);
}

// Bloom "bloom-canvas" elements are <div>'s which wrap various kinds of canvas elements.
// In legacy books and in publication mode, they may directly wrap an img (or have a
// background image) which acts as a background for any other content.
// Precondition: bloomCanvas must be just a single HTMLElement
function SetupBloomCanvas(bloomCanvas: HTMLElement) {
    // Now that we can overlay things on top of images, we don't want to show the flower placeholder
    // if the bloom-canvas contains a canvas element.
    updateCanvasElementClass(bloomCanvas);

    // This will fix cover image on Kyrgyzstan books that we created before we switched to this
    // new border system. Going forward, say 5.1, we could remove this and just
    // rely on a call to SetImageDisplaySize() when the image is added.
    const img = $(bloomCanvas).find("img");
    SetImageDisplaySizeIfCalledFor($(bloomCanvas), img);
    SetupChangeImageButton(bloomCanvas);

    $(bloomCanvas)
        .mouseenter(function () {
            handleMouseEnterBloomCanvas(this);
        })
        .mouseleave(function (e: JQueryMouseEventObject) {
            // Page numbers displaying inside the bloom-canvas must have their
            // width and height constrained.  That way, hovering over the page number
            // triggers the mouseleave event, and the image editing buttons are hidden
            // before the mouse cursor actually leaves the bloom-canvas, but hovering
            // above or beside the page number does nothing.  See BL-13098 and BL-13221.
            handleMouseLeaveBloomCanvas(this);
        });
}

export function getImageUrlFromImageContainer(
    HTMLElement: HTMLElement,
): string {
    return GetRawImageUrl(getImageFromContainer(HTMLElement));
}

export function getImageFromContainer(
    imageContainer: HTMLElement, // in one possibly obsolete caller, might be a bloom-canvas
): HTMLImageElement | null {
    // If there is ever a case where the img we want is not a direct child of the container,
    // be careful not to fix this in a way that might accidentally return a canvas element image
    // when we want the parent image (or accidentally return all of them, especially if
    // you use jquery).
    // The bloom-ui filter prevents returning controls we add to canvas elements.
    // Note: x instanceof HTMLImageElement did not work reliably.
    return Array.from(imageContainer.children).find(
        (x) => x.nodeName === "IMG",
    ) as HTMLImageElement;
}

// Given a canvas element which may or may not be an image canvas element, if it IS an image canvas element,
// find its image. Otherwise, return null.
export function getImageFromCanvasElement(
    canvasElement: HTMLElement,
): HTMLImageElement | null {
    const imageContainer =
        canvasElement.getElementsByClassName(kImageContainerClass)[0];
    if (!imageContainer) {
        return null;
    }
    return getImageFromContainer(imageContainer as HTMLElement);
}

// The background image in a bloom-canvas doesn't behave quite like other canvas elements,
// since code keeps it centered and filling the container in at least one dimension. (Currently
// we mainly mimic object-fit:contain, but some pages really want object-fit:cover). The internal
// representation, however, is the same as for other canvas elements, so we can use many of the
// same functions. That is, the background image is an element with the bloom-canvas-element
// class (with a determined size and position in its style top, left, width, and height),
// an image-container (with 100% size) and an img (which may have top, left, and width
// attributes to crop it, as well as the src that determines the actual image).
// (It also has bloom-backgroundImage).
// There are enough common behaviors to make it useful to use the same structure, and I
// (JohnT) at least find it useful to think of it as a background canvas element.
export function getBackgroundCanvasElementFromBloomCanvas(
    bloomCanvas: HTMLElement,
): HTMLElement | null {
    return bloomCanvas.getElementsByClassName(
        kBackgroundImageClass,
    )[0] as HTMLElement;
}

// Shortcut to get the img element from the background canvas element (if any) of a bloom-canvas.
// This, rather than the obsolete img that is a direct child and is always image-placeholder.png,
// is the background image that is actually displayed.
export function getBackgroundImageFromBloomCanvas(
    bloomCanvas: HTMLElement,
): HTMLElement | null {
    const bgCanvasElement =
        getBackgroundCanvasElementFromBloomCanvas(bloomCanvas);
    if (!bgCanvasElement) {
        return null;
    }
    return getImageFromCanvasElement(bgCanvasElement as HTMLElement);
}

/**
 * Disables the current image tooltip
 */
function DisableImageTooltip(container: HTMLElement | undefined | null) {
    const canvasElementManager = getCanvasElementManager();
    if (canvasElementManager) {
        // If this is set up, since we only want this tooltip for one container that has an active
        // background image, the most complete thing is to remove them all.
        canvasElementManager
            .getAllBloomCanvasesOnPage()
            .forEach((bloomCanvas) => {
                bloomCanvas.title = "";
            });
        return;
    }
    if (!container) {
        return;
    }

    // If the canvas element manager hasn't been set up at all we can at least clear the current one.
    const bloomCanvas = container.closest(kBloomCanvasClass) as HTMLElement; // this is the one we want to clear the title on, if any

    if (bloomCanvas) {
        bloomCanvas.title = "";
    }
}

// Note: since this function (obviously) updates state / has side effects,
// callers should consider the order operations are done if multiple operations happen at or near the same time
// to ensure that the final state is the one they desire.
export function UpdateImageTooltipVisibility(
    container: HTMLElement | undefined | null,
) {
    const theOneCanvasElementManager = getCanvasElementManager();
    if (
        !container ||
        container.classList.contains("bloom-hideImageButtons") ||
        playingBloomGame(container) ||
        EditableDivUtils.isInHiddenLanguageBlock(container) ||
        !theOneCanvasElementManager ||
        container.getElementsByClassName("bloom-backgroundImage")[0] !==
            theOneCanvasElementManager.getActiveElement()
    ) {
        // We don't want the tooltip unless this container's background image is active.
        DisableImageTooltip(container);
    } else {
        const dataTitle = container.getAttribute("data-title");

        // If dataTitle is null for some unexpected reason, let's just leave the title unchanged.
        if (dataTitle !== null) {
            // Set title on the main bloom-canvas
            // The intuitive thing to do would be to set each container's title individually.
            // However, that relies on each container being able to receive mouse events.
            // Canvas element images have problems receiving mouse events on the majority of their surface area
            // because the canvas is above them.
            // One could mess around with an invisible layer for each canvas element image above the canvas, and it seems to work
            // but that's a lot more complicated z-index wise and introduces other undesired side effects which need to be coded against.
            // It's less complicated to just set the title of the main container (its events trigger because the canvas is its descendant,
            // and the canvas is receiving events)
            const bloomCanvas = container.closest(
                kBloomCanvasSelector,
            ) as HTMLElement;

            if (bloomCanvas) {
                bloomCanvas.title = dataTitle;
            } else {
                console.assert(false, ".bloom-canvas expected but not found.");
            }
        }
    }
}

async function SetImageTooltip(bloomCanvas: HTMLElement) {
    const title = await DetermineImageTooltipAsync(bloomCanvas);

    // We use data-title to store what the tooltip should be, regardless of whether the tooltip should actually be currently visible
    // Use the real title attribute to show the tooltip only when desired
    // (e.g. show when no canvas element selected but hide when a canvas element is selected)
    bloomCanvas.setAttribute("data-title", title);

    UpdateImageTooltipVisibility(bloomCanvas);
}

// Corresponds with ImageApi.cs::HandleImageInfo
interface IImageInfoResponse {
    name: string;
    bytes: number;
    width: number;
    height: number;
    bitDepth: string;
}

async function DetermineImageTooltipAsync(
    bloomCanvas: HTMLElement,
): Promise<string> {
    const imgElement = getBackgroundImageFromBloomCanvas(bloomCanvas);

    if (!imgElement) {
        return "";
    }
    const url = GetRawImageUrl(imgElement);
    // Don't try to go getting image info for a built in Bloom image (like cogGrey.svg).
    // It'll just throw an exception.
    if (url.startsWith("/bloom/")) {
        return "";
    }

    const containerJQ = $(bloomCanvas);
    const targetDpiWidth = Math.ceil((300 * containerJQ.width()) / kBrowserDpi);
    const targetDpiHeight = Math.ceil(
        (300 * containerJQ.height()) / kBrowserDpi,
    );
    const isPlaceHolder = isPlaceHolderImage(url);

    const result = await getWithConfigAsync<IImageInfoResponse>("image/info", {
        params: { image: url },
    });

    if (!result) {
        return "";
    }

    const imageFileInfo = result.data;
    let linesAboutThisFile: string;
    const fileFound = imageFileInfo.bytes >= 0;
    let dpiLine = "";
    if (isPlaceHolder) {
        linesAboutThisFile = "";
    } else if (!fileFound) {
        linesAboutThisFile = `${imageFileInfo.name} not found\n`;
    } else {
        const dpi = getDpi(
            bloomCanvas,
            imageFileInfo.width,
            imageFileInfo.height,
        );
        const bulletForDpi = dpi < 300 ? "⚠" : "✓";
        // removed because only devs care! Bit Depth: ${imageFileInfo.bitDepth.toString()}
        linesAboutThisFile = `Name: ${
            imageFileInfo.name
        } Size: ${getFileLengthString(imageFileInfo.bytes)} Dots: ${
            imageFileInfo.width
        } x ${imageFileInfo.height}\n\n`;
        if (!isPlaceHolder) {
            dpiLine = `${bulletForDpi} This image would print at ${dpi} DPI.\n`;
        }
    }

    // This is really talking about the bloom-canvas, but for UI we'll stick with image container.
    const linesAboutThisContext =
        `For the current paper size:\n` +
        `  • The image container is ${containerJQ.width()} x ${containerJQ.height()} dots.\n` +
        `  • For print publications, you want between 300-600 DPI (Dots Per Inch).\n` +
        dpiLine +
        `  • An image with ${targetDpiWidth} x ${targetDpiHeight} dots would fill this container at 300 DPI.`;

    // if there is a data-href, start with that url
    let hyperlinkInfo = "";
    const hyperlink = bloomCanvas.getAttribute("data-href");
    if (hyperlink) {
        // Enhance: we should eventually give book names and page numbers, but perhaps that will
        // require clicking something to see all that instead of doing that lookup just in case
        // someone looks at the tooltip.
        hyperlinkInfo = `Hyperlink: ${hyperlink}\n`; // don't worry about localization yet
    }
    return hyperlinkInfo + linesAboutThisFile + linesAboutThisContext;
}
function getDpi(
    container: HTMLElement,
    imageWidth: number,
    imageHeight: number,
): number {
    const containerJQ = $(container);

    const containerAspectRatio =
        containerJQ.width() / Math.max(1, containerJQ.height());
    const imageAspectRatio = imageWidth / Math.max(1, imageHeight);

    // Image is skinnier than the container, so the image will be
    // stretched/squeezed to fit the height of the container.
    if (imageAspectRatio < containerAspectRatio) {
        return Math.round(imageHeight / (containerJQ.height() / kBrowserDpi));
    }
    // Image is fatter than the container, so the image will be
    // stretched/squeezed to fit the width of the container.
    else {
        return Math.round(imageWidth / (containerJQ.width() / kBrowserDpi));
    }
}

function SetImageDisplaySizeIfCalledFor(container: JQuery, img: JQuery) {
    //const $container = $(containerDiv);
    // For Kyrgyzstan covers, we needed a white border around images.
    // object-fit:contain would leave the border of the image around the inside
    // of the parent, instead of around the fitted image. See
    // https://issues.bloomlibrary.org/youtrack/issue/BL-9460.
    // So this allows us to do things the old fashioned way, essentially
    // implementing object-fit ourselves, so that a style can be applied to add
    // a border that will fit snugly.

    if (container.hasClass("bloom-scale-with-code")) {
        const url = GetRawImageUrl(img);
        // Don't touch images for a built in Bloom image (like cogGrey.svg).
        if (url.startsWith("/bloom/")) {
            return;
        }
        getWithConfig(
            "image/info",
            { params: { image: GetRawImageUrl(img) } },
            (result) => {
                const imageInfo: any = result.data;
                const containerAspectRatio =
                    container.width() / Math.max(1, container.height());
                const imageAspectRatio =
                    imageInfo.width / Math.max(1, imageInfo.height);

                // image is skinnier than the container
                if (imageAspectRatio < containerAspectRatio) {
                    $(img).css(
                        "width",
                        `${container.height() * imageAspectRatio}px`,
                    );
                    $(img).css("height", "100%");
                }
                // image is fatter than the container
                else {
                    $(img).css(
                        "height",
                        `${
                            imageInfo.height *
                            (container.width() / Math.max(1, imageInfo.width))
                        }px`,
                    );
                    $(img).css("width", "100%");
                }
                // This isn't actually needed once we have the height and width
                // here. However, when a new the book is previewed *before we've
                // had a chance to set this stuff*, it will look awful unless we
                // can put object-fit:contain on it. That won't make the border
                // fit tightly, but at least the image won't be distorted. So we
                // do set it that way in the stylesheet, and then overwrite that here once
                // we have the height and width set.
                $(img).css("object-fit", "cover");
            },
        );
    }
}

function getFileLengthString(bytes): string {
    const units = ["bytes", "kb", "mb"];
    for (let i = units.length; i-- > 0; ) {
        const unit = Math.pow(1024, i);
        if (bytes >= unit)
            //reviewSlog
            return (
                (Math.round((bytes / unit) * 100) / 100).toFixed(2).toString() +
                " " +
                units[i]
            );
        //return parseFloat(Math.round(bytes / unit * 100) / 100).toFixed(2) + ' ' + units[i];
    }
    return "";
}

// Gets the src attribute out of images, and the background-image:url() of everything else
function GetRawImageUrl(imgOrDivWithBackgroundImage): string {
    if ($(imgOrDivWithBackgroundImage).hasAttr("src")) {
        return $(imgOrDivWithBackgroundImage).attr("src");
    }
    //handle divs with background-image in an inline style attribute
    if ($(imgOrDivWithBackgroundImage).hasAttr("style")) {
        const style = $(imgOrDivWithBackgroundImage).attr("style");
        // see http://stackoverflow.com/questions/9723889/regex-to-match-urls-in-inline-styles-div-style-url
        //var result = (/url\(\s*(['"]?)(.*?)\1\s*\)/.exec(style) || [])[2];
        // Various things don't expect this to return null or undefined, which the regex just might do.
        return (/url\s*\(\s*(['"]?)(.*?)\1\s*\)/.exec(style) || [])[2] ?? "";
    }
    return "";
}

/* appears to be unused
export function SetImageElementUrl(imgOrDivWithBackgroundImage, url) {
    if (imgOrDivWithBackgroundImage.tagName.toLowerCase() === "img") {
        imgOrDivWithBackgroundImage.src = url;
    } else {
        imgOrDivWithBackgroundImage.style =
            "background-image:url('" + url + "')";
    }
}
*/

// While the actual metadata is embedded in the images (Bloom/palaso does that), Bloom sticks some metadata in data-* attributes
// so that we can easily & quickly get to it here.
// Currently this button is only shown on top of background images, which are usually large enough for it.
// This function takes a parent element which is currently the body element.
export function SetupMetadataButton(parent: HTMLElement) {
    // This method is called from bloomEditing.ts / SetupElements with the body element.
    let bgImageCanvasElements: HTMLElement[] = [];
    if (parent.classList.contains(kBackgroundImageClass)) {
        bgImageCanvasElements.push(parent);
    } else {
        bgImageCanvasElements = Array.from(
            parent.getElementsByClassName(kBackgroundImageClass),
        ) as HTMLElement[];
    }
    for (const bgImageCanvasElement of bgImageCanvasElements) {
        const bloomCanvas = bgImageCanvasElement.parentElement as HTMLElement;
        for (const oldButton of Array.from(
            bloomCanvas.getElementsByClassName("editMetadataButton"),
        )) {
            oldButton.remove();
        }
        const container = bgImageCanvasElement.getElementsByClassName(
            kImageContainerClass,
        )[0] as HTMLElement;
        if (!container) {
            continue; // pathological
        }
        const img = getImageFromContainer(container);
        if (!img || !!isPlaceHolderImage(GetRawImageUrl(img))) continue; // placeholder, doesn't get one of these buttons.

        //review: should we also require copyright, illustrator, etc? In many contexts the id of the work-for-hire illustrator isn't available
        const copyright = img.getAttribute("data-copyright");

        // With the bloom-ui class present, we don't have to worry about removing this except when
        // this function is called again.
        let buttonClasses = `editMetadataButton imageButton bloom-ui`;
        let title = "Edit image credits, copyright, & license";
        let titleId = "EditTab.Image.EditMetadata";
        if (!copyright || copyright.length === 0) {
            buttonClasses += " imgMetadataProblem";
            title = "Image is missing information on Credits, Copyright";
            titleId = "EditTab.Image.MissingInfo";
        }
        const button = container.ownerDocument.createElement("button");
        button.className = buttonClasses;
        button.addEventListener("click", () => {
            // Don't do this before it gets clicked; might not be correct at the time we set up the handler.
            const url = img.getAttribute("src");
            showCopyrightAndLicenseDialog(url ?? "");
        });
        theOneLocalizationManager
            .asyncGetText(titleId, title, "tooltip text")
            .done((translation) => {
                const title = translation.replace(/'/g, "&apos;");
                button.title = title;
                bloomCanvas.prepend(button);
            })
            .fail(() => {
                button.title = title;
                bloomCanvas.prepend(button);
            });
    }
}

// Instead of "missing", we want to show it in the right ui language. We also want the text
// to indicate that it might not be missing, just didn't load (this happens on slow machines)
function SetAlternateTextOnImages(element) {
    const rawImageUrl = GetRawImageUrl(element);
    if (rawImageUrl.length > 0) {
        if (isPlaceHolderImage(rawImageUrl)) {
            // We now use css to display the placeholder image instead of an actual image-placeholder.png file,
            // but we are continuing to set and use  src=image-placeholder.png to trigger place holder behavior. So we
            // don't expect to find a image-placeholder.png file, and we don't want to display alt text.
            return;
        }
        //don't show this on the empty license image when we don't know the license yet
        const englishText =
            "This picture, {0}, is missing or was loading too slowly."; // Also update HtmlDom.cs::IsPlaceholderImageAltText
        const nameWithoutQueryString = GetRawImageUrl(element).split("?")[0];
        const decodedName = decodeURI(nameWithoutQueryString);
        // show English to start with in case localization never returns even a failure
        $(element).attr(
            "alt",
            theOneLocalizationManager.simpleFormat(englishText, [decodedName]),
        );
        theOneLocalizationManager
            .asyncGetText(
                "EditTab.Image.AltMsg",
                englishText,
                "message displayed when the picture image cannot be displayed",
                decodedName,
            )
            .done((translation) => {
                $(element).attr("alt", translation);
            })
            .fail(() => {
                $(element).attr(
                    "alt",
                    theOneLocalizationManager.simpleFormat(englishText, [
                        decodedName,
                    ]),
                );
            });
    } else {
        $(element).attr("alt", ""); //don't be tempted to show something like a '?' unless you fix the result when you have a custom book license on top of that '?'
    }
}

// Handle elements like the wordAndPicture container in the Picture Dictionary which have the class bloom-resizable.
// This method uses jQuery UI resizable to make it so.
export function SetupResizableElement(element) {
    $(element)
        .mouseenter(function () {
            $(this).addClass("ui-mouseOver");
        })
        .mouseleave(function () {
            $(this).removeClass("ui-mouseOver");
        });
    // When the outer container is resized, the inner bloom-canvas is resized with it.
    const bloomCanvas = $(element).find(kBloomCanvasSelector);
    // A Picture Dictionary Word-And-Image
    if ($(bloomCanvas).length > 0) {
        // This method gets called on elements that have class bloom-resizable, which at the moment
        // is only the wordAndPicture container in the Picture Dictionary. It contains a bloom-canvas,
        // typically just with a picture, but nothing prevents adding canvas elements to it.
        // The idea is to use jquery resizable on the outer element (the argument to this function)
        // which is expected to contain one bloom-canvas and typically some other text.
        // The bloom-canvas is resized by the same amount as the outer element (using jquery's
        // alsoResize). As with an orgami splitter move, we need some magic to make the main
        // image inside the canvas resize in real time, and any other bloom-canvas elements
        // adjust when the drag ends.
        // A previous comment talked about the reason for this strategy being to keep the
        // caption centered, but currently we are NOT centering it. However, it makes sense
        // to resize the picture and its captions together anyway. We at least want the text
        // boxes to stay the same size as the bloom-canvas.)
        const img = $(bloomCanvas).find("img");
        $(element).resizable({
            handles: "nw, ne, sw, se",
            containment: "parent",
            alsoResize: bloomCanvas,
            start(e, ui) {
                theOneCanvasElementManager.suspendComicEditing(
                    "forJqueryResize",
                );
            },
            stop(e, ui) {
                theOneCanvasElementManager.resumeComicEditing();
            },
        });
    }
    // It actually IS a bloom-canvas. This old code expects it to directly contain
    // an img and does not account for canvas elements. With no wasy way to test, I'm not
    // going to attempt a full fix.
    else if ($(element).hasClass(kBloomCanvasClass)) {
        alert(
            "applying bloom-resizable to a bloom-canvas may not work. Code in bloomImages.SetupResizableElement needs updating",
        );
        const img = $(element).find("img");
        $(element).resizable({
            handles: "nw, ne, sw, se",
            containment: "parent",
        });
    }
    // some other kind of resizable. (JT Mar 2025: I don't think anything currently uses this,
    // so it has not been tested with any changes we've made in the last few years)
    else {
        $(element).resizable({
            handles: "nw, ne, sw, se",
            containment: "parent",
            stop: ResizeUsingPercentages,
            start: (e, ui) => {
                if (
                    $(ui.element).css("top") == "0px" &&
                    $(ui.element).css("left") == "0px"
                ) {
                    $(ui.element).data("doRestoreRelativePosition", "true");
                }
            },
        });
    }
}

//jquery resizable normally uses pixels. This makes it use percentages, which are mor robust across page size/orientation changes
function ResizeUsingPercentages(e, ui) {
    const parent = ui.element.parent();
    ui.element.css({
        width: (ui.element.width() / parent.width()) * 100 + "%",
        height: (ui.element.height() / parent.height()) * 100 + "%",
    });

    //after any resize jquery adds an absolute position, which we don't want unless the user has resized
    //so this removes it, unless we previously noted that the user had moved it
    if ($(ui.element).data("doRestoreRelativePosition")) {
        ui.element.css({
            position: "",
            top: "",
            left: "",
        });
    }
    $(ui.element).removeData("hadPreviouslyBeenRelocated");
}
