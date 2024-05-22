import "../../lib/jquery.resize"; // makes jquery resize work on all elements
import {
    getWithConfig,
    getWithConfigAsync,
    postJson
} from "../../utils/bloomApi";

// Enhance: this could be turned into a Typescript Module with only two public methods

import theOneLocalizationManager from "../../lib/localizationManager/localizationManager";

import { theOneBubbleManager, updateOverlayClass } from "./bubbleManager";

import { farthest } from "../../utils/elementUtils";
import { EditableDivUtils } from "./editableDivUtils";

const kPlaybackOrderContainerSelector: string =
    ".bloom-playbackOrderControlsContainer";

// This appears to be constant even on higher dpi screens.
// (See http://www.w3.org/TR/css3-values/#absolute-lengths)
const kBrowserDpi = 96;

export function cleanupImages() {
    $(".bloom-imageContainer").css("opacity", ""); //comes in on img containers from an old version of myimgscale, and is a major problem if the image is missing
    $(".bloom-imageContainer").css("overflow", ""); //review: also comes form myimgscale; is it a problem?
}

export function SetupImagesInContainer(container) {
    $(container)
        .find(".bloom-imageContainer > img") // the ">" here prevents finding img's of ui affordances deep in comics
        .each(function() {
            SetupImage(this);
        });

    $(container)
        .find(".bloom-imageContainer")
        .each((index, element) => {
            SetupImageContainer(element as HTMLHtmlElement);
        });

    //todo: this had problems. Check out the later approach, seen in draggableLabel (e.g. move handle on the inside, using a background image on a div)
    $(container)
        .find(".bloom-draggable")
        .mouseenter(function() {
            $(this).prepend(
                "<button class='moveButton' title='Move'></button>"
            );
            $(this)
                .find(".moveButton")
                .mousedown(function(e) {
                    //reviewSlog added the <any>
                    $(this)
                        .parent()
                        .trigger(<any>e);
                });
        });
    $(container)
        .find(".bloom-draggable")
        .mouseleave(function() {
            $(this)
                .find(".moveButton")
                .each(function() {
                    $(this).remove();
                });
        });

    // Add the event listener to handle control/alt being pressed.
    // We have a pretty interesting way of listening for these keydown/keyup events.
    // I initially tried putting the event listeners on the bloom-imageContainer,
    // but that didn't work reliably.
    // Keyboard events don't fire for every element, but only certain valid ones.
    // For the short answer, divs with contenteditable=true meet the criteria.
    // (Although... I tested what happens with slapping contenteditable=true onto the bloom-imageContainer,
    // and I don't think it helped)
    // To get around this, I found the following strategy:
    // 1) place the keydown listener as broadly as we can... that's {document}.
    // 2) When a key is pressed, the listener will fire and some child element of the document will be the event target.
    // 3) Oddly enough however, the same trick doesn't work for "keyup" (if you put the listener on {document}, it doesn't fire).
    // 4) Instead, we put the "keyup" event listener on whatever element fired the "keydown" event,
    //    since whatever element that is should presumably be getting the keyup event too.
    //    This could be the main text box actually, and that's fine.
    //    It could also be the translation qtip way off screen (maybe only for pages without a text box?),
    //    but even that is sufficient for our purposes.
    document.addEventListener("keydown", ctrlAltKeyDownListener);

    $(container)
        .find("img")
        .each(function() {
            SetAlternateTextOnImages(this);
        });
}

export function SetupImage(image: JQuery) {
    // Remove any obsolete explicit image size and position left over from earlier versions of Bloom, before we had object-fit:contain.
    if (image.style) {
        // Note, in BL-9460 we had to return to having width and height in some cases.
        if (!$(image.parent).hasClass("bloom-scale-with-code")) {
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
}

/**
 * When the Ctrl or Alt key is pressed, hide the image editing buttons until the key is released.
 */
function ctrlAltKeyDownListener(event: KeyboardEvent) {
    if ((event.ctrlKey || event.altKey) && event.target) {
        event.target.addEventListener("keyup", ctrlAltKeyUpListener);

        // Note (paranoia): Add the ui-suppressImageButtons class conservatively (ensure event.target is good (non-null) first),
        // remove it liberally (regardless of event.target),
        // Since if it gets stuck on, the image editing buttons won't come back (unless SetupImageContainer is re-run)
        document
            .querySelectorAll<HTMLElement>(".bloom-imageContainer")
            .forEach(imageContainer => {
                SuppressImageEditing(imageContainer);

                if (event.ctrlKey) {
                    imageContainer.classList.add("ui-ctrlDown");
                } else if (event.altKey) {
                    theOneBubbleManager.tryApplyResizingUI(imageContainer);
                }
            });
    }
}

/**
 * When the last Ctrl or Alt key is released, show the image editing buttons again (if they still exist)
 */
function ctrlAltKeyUpListener(event: KeyboardEvent) {
    // event.ctrlKey/event.altKey are normally false when a single Control or Alt button is released
    // (unless the user pressed two ctrl/alt keys, and then released one of them).
    // We don't want to fire the event until all of the ctrl/alt keys are released.
    const isCtrlOrAltReleased = event.key === "Control" || event.key === "Alt";
    const areAnyOtherCtrlOrAltKeysDown = event.ctrlKey || event.altKey;

    if (isCtrlOrAltReleased && !areAnyOtherCtrlOrAltKeysDown) {
        document
            .querySelectorAll<HTMLElement>(
                ".bloom-imageContainer.ui-suppressImageButtons"
            )
            .forEach(imageContainer => {
                DisableSuppressImageEditingButtons(imageContainer);

                // Remember, for keyup events, you want to check event.key === "Control" instead of event.ctrlKey
                if (event.key === "Control") {
                    imageContainer.classList.remove("ui-ctrlDown");
                } else if (
                    event.key === "Alt" &&
                    !theOneBubbleManager.isResizing(imageContainer)
                ) {
                    // FYI: Check !isResizing() so if you release Alt but still keep the mouse down, resizing will continue.
                    // Unsure if this needs to be set in stone, but it's for consistency with
                    // 1) our historical practice, and 2) how Ctrl+drag currently works
                    theOneBubbleManager.turnOffResizing(imageContainer);
                }
            });

        // De-register ourself as an event handler.
        // We're no longer needed until the next time ctrl/alt is pressed,
        // and the user could type a bunch of things in a text box, which we don't need to bother listening to.
        event.target?.removeEventListener("keyup", ctrlAltKeyUpListener);
    }
}

export function GetButtonModifier(container) {
    let buttonModifier = "";
    const imageButtonWidth = 87;
    const imageButtonHeight = 52;
    const $container = $(container);
    if ($container.height() < imageButtonHeight * 2) {
        buttonModifier = "smallButtonHeight";
    }
    if ($container.width() < imageButtonWidth * 2) {
        buttonModifier += " smallButtonWidth";
    }
    if ($container.width() < imageButtonWidth) {
        buttonModifier += " verySmallButtons";
    }
    return buttonModifier;
}

export function addImageEditingButtons(containerDiv: HTMLElement): void {
    if (!containerDiv || containerDiv.classList.contains("hoverUp")) {
        return;
    }
    let img = getImgFromContainer(containerDiv);

    // Enhance: remove this unused flexibility to put images as background-images on div.bloom-imageContainers.
    // We still do it when making bloomPUBs, but that's a write-only operation (and we will retain the ability to migrate back).
    // I (JH) think it is cognitively expensive to leave legacy cruft around.
    if (img.length === 0)
        // This case is probably a left over from some previous Bloom where
        // we were using background images instead of <img>? But it does
        // no harm so I'm leaving it in.
        img = $(containerDiv); //using a backgroundImage

    const $containerDiv = $(containerDiv);
    if ($containerDiv.find(kPlaybackOrderContainerSelector).length > 0) {
        return; // Playback order controls are active, deactivate image container stuff.
    }
    if ($containerDiv.find("button.imageOverlayButton").length > 0) {
        return; // already have buttons
    }
    const buttonModifier = GetButtonModifier($containerDiv);

    const addButtonHandler = (command: string) => {
        const button = $containerDiv.get(0)?.firstElementChild;
        button?.addEventListener("click", (e: MouseEvent) => {
            // "detail >1" in chromium means this is a double click.
            // Note, if we have problems with timing, this wouldn't necessarily fix them.
            // It's only going to debounce clicks that come in close enough to count as
            // double clicks, presumably based on the OS's double click timing setting.
            // eslint-disable-next-line @typescript-eslint/no-explicit-any
            if ((e as any).detail > 1) {
                return;
            }
            // get the image id attribute. If it doesn't have one, add it first
            let imageId = img.attr("id");
            const imageSrc = GetRawImageUrl(img);
            if (!imageId) {
                imageId = EditableDivUtils.createUuid();
                img.attr("id", imageId);
            }

            postJson("editView/" + command + "Image", { imageId, imageSrc });
        });
    };

    $containerDiv.prepend(
        '<button class="miniButton cutImageButton imageOverlayButton disabled ' +
            buttonModifier +
            '" title="' +
            theOneLocalizationManager.getText("EditTab.Image.CutImage") +
            '"></button>'
    );
    addButtonHandler("cut");

    $containerDiv.prepend(
        '<button class="miniButton copyImageButton imageOverlayButton disabled ' +
            buttonModifier +
            '" title="' +
            theOneLocalizationManager.getText("EditTab.Image.CopyImage") +
            '"></button>'
    );
    addButtonHandler("copy");

    $containerDiv.prepend(
        '<button class="pasteImageButton imageButton imageOverlayButton ' +
            buttonModifier +
            '" title="' +
            theOneLocalizationManager.getText("EditTab.Image.PasteImage") +
            '"></button>'
    );
    addButtonHandler("paste");

    $containerDiv.prepend(
        '<button class="changeImageButton imageButton imageOverlayButton ' +
            buttonModifier +
            '" title="' +
            theOneLocalizationManager.getText("EditTab.Image.ChangeImage") +
            '"></button>'
    );
    addButtonHandler("change");

    // As part of BL-9976 JH decided to remove this button as users were getting confused.
    // if (
    //     // Only show this button if the toolbox is also offering it. It might not offer it
    //     // if it's experimental and that settings isn't on, or for Bloom Enterprise reasons, or whatever.
    //     getToolboxFrameExports()
    //         ?.getTheOneToolbox()
    //         .getToolIfOffered(ImageDescriptionAdapter.kToolID)
    // ) {
    //     $this.prepend(
    //         '<button class="imageDescriptionButton imageButton imageOverlayButton ' +
    //             buttonModifier +
    //             '" title="' +
    //             theOneLocalizationManager.getText(
    //                 "EditTab.Toolbox.ImageDescriptionTool" // not quite the "Show Image Description Tool", but... feeling parsimonious
    //             ) +
    //             '"></button>'
    //     );
    //     $this.find(".imageDescriptionButton").click(() => {
    //         getToolboxFrameExports()
    //             ?.getTheOneToolbox()
    //             .activateToolFromId(ImageDescriptionAdapter.kToolID);
    //     });
    // }

    SetImageTooltip(containerDiv);

    if (IsImageReal(img)) {
        const title = theOneLocalizationManager.getText(
            "EditTab.Image.EditMetadata"
        );
        const button = `<button class="editMetadataButton imageButton imageOverlayButton ${buttonModifier}" title="${title}"></button>`;
        $containerDiv.prepend(button);
        $containerDiv.find("button.editMetadataButton").attr(
            "onClick",
            // Originally, we tried to determine the imageUrl at setup time and put that string in the onClick handler string below.
            // However, it turns out that we must rely on the click handler knowing what element we clicked on ("this") at click time.
            // Otherwise, we can get an incorrect imageUrl. For example, a picture on picture might give the parent picture imageUrl.
            // We have to do this strange editTabBundle stuff because at the time of the click, we are in a context where that is all we can access.
            `(window.parent || window).editTabBundle.showCopyrightAndLicenseDialog(
                (window.parent || window).editTabBundle.getImageUrlFromImageButton(this));`
        );
        $containerDiv.find(".miniButton").each(function() {
            $(this).removeClass("disabled");
        });
    }

    $containerDiv.addClass("hoverUp");
}

/**
 * Gets a NodeListOf of the image editing button elements, which can be iterated through.
 * @param containerDiv: The .bloom-imageContainer which contains the image editing buttons.
 * @param options: An object with the following fields:
 *    skipProblemIndicator: true to omit the imgMetadataProblem element(s) from the list
 */
function getImageEditingButtons(
    containerDiv: Element,
    options: {
        skipProblemIndicator: boolean;
    }
): NodeListOf<Element> {
    // NOTE: imgMetadataProblem isn't actually an imageOverlayButton,
    // so strictly speaking, we don't need to do any special checks,
    // but let's check anyway just in case it ever receives the imageOverlayButton class in the future
    const selector =
        ".imageOverlayButton" +
        (options.skipProblemIndicator ? ":not(.imgMetadataProblem)" : "");
    return containerDiv.querySelectorAll(selector);
}

export function removeImageEditingButtons(containerDiv: Element): void {
    containerDiv.classList.remove("hoverUp");
    getImageEditingButtons(containerDiv, {
        skipProblemIndicator: true // leave the problem indicator visible
    }).forEach(button => {
        button.remove();
    });
    DisableImageTooltip(containerDiv as HTMLElement);
}

export function tryRemoveImageEditingButtons(
    containerDiv: Element | undefined
): void {
    if (containerDiv) {
        removeImageEditingButtons(containerDiv);
    }
}

export function DisableImageEditing(imageContainer: HTMLElement) {
    imageContainer.classList.add("bloom-hideImageButtons");
    UpdateImageTooltipVisibility(imageContainer);
}

/**
 * Undo the effect of calling DisableImageEditing()
 */
export function EnableImageEditing(imageContainer: HTMLElement) {
    imageContainer.classList.remove("bloom-hideImageButtons");
    UpdateImageTooltipVisibility(imageContainer);
}

/**
 * Like EnableImageEditing, but for the entire document instead of a single container.
 */
export function EnableAllImageEditing() {
    // getElementsByClassName returns these in document order.
    // EnableImageEditing has side effects. Whose should win?
    // We'd rather have the first primary image container win, rather than the last child container,
    // so apply these in reverse document order.
    // (This may not be strictly needed because currently, all calls to this function
    // occur when the side effects don't matter. But just in case.)
    Array.from(document.getElementsByClassName("bloom-hideImageButtons"))
        .reverse()
        .forEach(EnableImageEditing);
}

/**
 * Temporarily suppresses image editing
 */
function SuppressImageEditing(imageContainer: HTMLElement) {
    imageContainer.classList.add("ui-suppressImageButtons");
    UpdateImageTooltipVisibility(imageContainer);
}

/**
 * Undo the effect of calling SuppressImageEditing()
 */
function DisableSuppressImageEditingButtons(imageContainer: HTMLElement) {
    imageContainer.classList.remove("ui-suppressImageButtons");
    UpdateImageTooltipVisibility(imageContainer);
}

// Bloom "imageContainer"s are <div>'s which wrap an <img>, and automatically proportionally resize
// the img to fit the available space.
// Precondition: containerDiv must be just a single HTMLElement
function SetupImageContainer(containerDiv: HTMLElement) {
    // Initialize the value of the hoverUp class.
    // the hoverup class should be present whenever the mouse is over the containerDiv.
    // This is usually achieved by mouseenter/mouseleave event handlers,
    // but mouseenter won't trigger if the mouse starts off over the image container when the page is loaded
    // That case is extremely commonplace when adding comic bubbles, because that needs to reload the page.
    // (It is also possible to trigger even when opening up a new page, but probably less likely to happen accidentally)
    if (containerDiv.matches(":hover")) {
        containerDiv.classList.add("hoverUp");
    } else {
        containerDiv.classList.remove("hoverUp");
    }

    // Just in case, ensure prior state is cleaned up
    DisableSuppressImageEditingButtons(containerDiv);

    // Now that we can overlay things on top of images, we don't want to show the flower placeholder
    // if the image container contains an overlay.
    updateOverlayClass(containerDiv);

    // This will fix cover image on Kyrgyzstan books that we created before we switched to this
    // new border system. Going forward, say 5.1, we could remove this and just
    // rely on a call to SetImageDisplaySize() when the image is added.
    const img = $(containerDiv).find("img");
    SetImageDisplaySizeIfCalledFor($(containerDiv), img);

    $(containerDiv)
        .mouseenter(function() {
            addImageEditingButtons(this);
        })
        .mouseleave(function(e: JQueryMouseEventObject) {
            // Page numbers displaying inside the image container must have their
            // width and height constrained.  That way, hovering over the page number
            // triggers the mouseleave event, and the image editing buttons are hidden
            // before the mouse cursor actually leaves the image container, but hovering
            // above or beside the page number does nothing.  See BL-13098 and BL-13221.
            removeImageEditingButtons(this);
        });
}

export function getImageUrlFromImageButton(button: HTMLButtonElement): string {
    const imageContainer = button?.parentElement;
    if (!imageContainer) return "";
    return GetRawImageUrl(getImgFromContainer(imageContainer));
}

function getImgFromContainer(imageContainer: HTMLElement) {
    // FF60 doesn't seem to do this properly, so I'm punting and using jquery...
    // return imageContainer.querySelector(
    //     "img:not(.bloom-ui > img, .bloom-ui)"
    // );
    return $(imageContainer)
        .find("img")
        .not(".bloom-ui") // e.g. dragHandle.svg
        .not(".bloom-ui img"); // e.g. cog.svg
}

/**
 * Disables the current image tooltip
 */
function DisableImageTooltip(container: HTMLElement) {
    // The patriarch represents the main .bloom-imageContainer, which would be the earliest in the DOM hierarchy.
    // We use the patriarch's title to represent the title for itself or whichever of its child overlayImages is active
    // (to avoid complicated conflicting z-index issues)
    const patriarch = farthest<HTMLElement>(container, ".bloom-imageContainer");

    // Before clearing the patriarch's title, first check if the patriarch
    // still represents this particular container.
    // When switching between containers, it might not, because we need to both set the title for the new one
    // and disable the old title.
    // So, check first before clearing the title.
    if (patriarch?.title === container.getAttribute("data-title")) {
        patriarch.title = "";
    }
}

// Note: since this function (obviously) updates state / has side effects,
// callers should consider the order operations are done if multiple operations happen at or near the same time
// to ensure that the final state is the one they desire.
function UpdateImageTooltipVisibility(container: HTMLElement) {
    if (
        container.classList.contains("bloom-hideImageButtons") ||
        container.classList.contains("ui-suppressImageButtons")
    ) {
        // Since the image buttons aren't visible, hide the image tooltip too
        DisableImageTooltip(container);
    } else {
        // Image editing buttons are allowed to be shown, so allow the image tooltip to be shown too
        const dataTitle = container.getAttribute("data-title");

        // If dataTitle is null for some unexpected reason, let's just leave the title unchanged.
        if (dataTitle !== null) {
            //
            // Set title on the main image container
            // The intuitive thing to do would be to set each container's title individually.
            // However, that relies on each container being able to receive mouse events.
            // Overlay images have problems receiving mouse events on the majority of their surface area
            // because the canvas is above them.
            // One could mess around with an invisible layer for each overlay image above the canvas, and it seems to work
            // but that's a lot more complicated z-index wise and introduces other undesired side effects which need to be coded against.
            // It's less complicated to just set the title of the main container (its events trigger because the canvas is its descendant,
            // and the canvas is receiving events)
            //
            const patriarch = farthest<HTMLElement>(
                container,
                ".bloom-imageContainer"
            );

            if (patriarch) {
                patriarch.title = dataTitle;
            } else {
                console.assert(
                    false,
                    ".bloom-imageContainer expected but not found."
                );
            }
        }
    }
}

async function SetImageTooltip(container: HTMLElement) {
    const title = await DetermineImageTooltipAsync(container);

    // We use data-title to store what the tooltip should be, regardless of whether the tooltip should actually be currently visible
    // Use the real title attribute to show the tooltip only when desired
    // (e.g. show when no bubble selected but hide when a bubble is selected)
    container.setAttribute("data-title", title);

    UpdateImageTooltipVisibility(container);
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
    container: HTMLElement
): Promise<string> {
    const containerJQ = $(container);
    const imgElement = $(container).find("img");

    if (!imgElement) {
        return "";
    }
    const url = GetRawImageUrl(imgElement);
    // Don't try to go getting image info for a built in Bloom image (like cogGrey.svg).
    // It'll just throw an exception.
    if (url.startsWith("/bloom/")) {
        return "";
    }

    const targetDpiWidth = Math.ceil((300 * containerJQ.width()) / kBrowserDpi);
    const targetDpiHeight = Math.ceil(
        (300 * containerJQ.height()) / kBrowserDpi
    );
    const isPlaceHolder = url.indexOf("placeHolder.png") > -1;

    const result = await getWithConfigAsync<IImageInfoResponse>("image/info", {
        params: { image: url }
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
            container,
            imageFileInfo.width,
            imageFileInfo.height
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

    const linesAboutThisContext =
        `For the current paper size:\n` +
        `  • The image container is ${containerJQ.width()} x ${containerJQ.height()} dots.\n` +
        `  • For print publications, you want between 300-600 DPI (Dots Per Inch).\n` +
        dpiLine +
        `  • An image with ${targetDpiWidth} x ${targetDpiHeight} dots would fill this container at 300 DPI.`;

    return linesAboutThisFile + linesAboutThisContext;
}
function getDpi(
    container: HTMLElement,
    imageWidth: number,
    imageHeight: number
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
            result => {
                const imageInfo: any = result.data;
                const containerAspectRatio =
                    container.width() / Math.max(1, container.height());
                const imageAspectRatio =
                    imageInfo.width / Math.max(1, imageInfo.height);

                // image is skinnier than the container
                if (imageAspectRatio < containerAspectRatio) {
                    $(img).css(
                        "width",
                        `${container.height() * imageAspectRatio}px`
                    );
                    $(img).css("height", "100%");
                }
                // image is fatter than the container
                else {
                    $(img).css(
                        "height",
                        `${imageInfo.height *
                            (container.width() /
                                Math.max(1, imageInfo.width))}px`
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
            }
        );
    }
}

function getFileLengthString(bytes): String {
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

// IsImageReal returns true if the img tag refers to a non-placeholder image
// If the image is a placeholder:
// - we don't want to offer to edit placeholder credits
// - we don't want to activate the minibuttons for cut/copy
function IsImageReal(img) {
    return (
        GetRawImageUrl(img)
            .toLowerCase()
            .indexOf("placeholder") == -1
    ); //don't offer to edit placeholder credits
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

//While the actual metadata is embedded in the images (Bloom/palaso does that), Bloom sticks some metadata in data-* attributes
// so that we can easily & quickly get to the here.
export function SetOverlayForImagesWithoutMetadata(container) {
    $(container)
        .find("*[style*='background-image']")
        .each(function() {
            SetOverlayForImagesWithoutMetadataInner(this, this);
        });

    //Do the same for any img elements inside
    $(container)
        .find(".bloom-imageContainer")
        .each(function() {
            // BL-9976: now that we can have images on images, only look one level down from the container.
            const img = $(this).find("> img");
            SetOverlayForImagesWithoutMetadataInner($(img).parent(), img);
        });
}

function SetOverlayForImagesWithoutMetadataInner(container, img) {
    if (!IsImageReal(img)) {
        return;
    }

    UpdateOverlay(container, img);

    //and if the bloom program changes these values (i.e. the user changes them using bloom), I
    //haven't figured out a way (apart from polling) to know that. So for now I'm using a hack
    //where Bloom calls click() on the image when it wants an update, and we detect that here.
    $(img).click(() => {
        UpdateOverlay(container, img);
    });
}

function UpdateOverlay(container, img) {
    $(container)
        .find("button.imgMetadataProblem")
        .each(function() {
            $(this).remove();
        });

    //review: should we also require copyright, illustrator, etc? In many contexts the id of the work-for-hire illustrator isn't available
    const copyright = $(img).attr("data-copyright");
    if (!copyright || copyright.length === 0) {
        const buttonClasses = `editMetadataButton imageButton imgMetadataProblem ${GetButtonModifier(
            container
        )}`;
        const englishText =
            "Image is missing information on Credits, Copyright, or License";
        theOneLocalizationManager
            .asyncGetText(
                "EditTab.Image.MissingInfo",
                englishText,
                "tooltip text"
            )
            .done(translation => {
                const title = translation.replace(/'/g, "&apos;");
                $(container).prepend(
                    `<button class='${buttonClasses}' title='${title}'></button>`
                );
            })
            .fail(() => {
                $(container).prepend(
                    `<button class='${buttonClasses}' title='${englishText}'></button>`
                );
            });
    }
}

// Instead of "missing", we want to show it in the right ui language. We also want the text
// to indicate that it might not be missing, just didn't load (this happens on slow machines)
function SetAlternateTextOnImages(element) {
    if (GetRawImageUrl(element).length > 0) {
        //don't show this on the empty license image when we don't know the license yet
        const englishText =
            "This picture, {0}, is missing or was loading too slowly."; // Also update HtmlDom.cs::IsPlaceholderImageAltText
        const nameWithoutQueryString = GetRawImageUrl(element).split("?")[0];
        const decodedName = decodeURI(nameWithoutQueryString);
        theOneLocalizationManager
            .asyncGetText(
                "EditTab.Image.AltMsg",
                englishText,
                "message displayed when the picture image cannot be displayed",
                decodedName
            )
            .done(translation => {
                $(element).attr("alt", translation);
            })
            .fail(() => {
                $(element).attr(
                    "alt",
                    theOneLocalizationManager.simpleFormat(englishText, [
                        decodedName
                    ])
                );
            });
    } else {
        $(element).attr("alt", ""); //don't be tempted to show something like a '?' unless you fix the result when you have a custom book license on top of that '?'
    }
}

export function SetupResizableElement(element) {
    $(element)
        .mouseenter(function() {
            $(this).addClass("ui-mouseOver");
        })
        .mouseleave(function() {
            $(this).removeClass("ui-mouseOver");
        });
    const childImgContainer = $(element).find(".bloom-imageContainer");
    // A Picture Dictionary Word-And-Image
    if ($(childImgContainer).length > 0) {
        /* The case here is that the thing with this class actually has an
         inner image, as is the case for the Picture Dictionary.
         The key, non-obvious, difficult requirement is keeping the text below
         a picture dictionary item centered underneath the image.  I'd be
         surprised if this wasn't possible in CSS, but I'm not expert enough.
         So, I switched from having the image container be resizable, to having the
         whole div (image+headwords) be resizable, then use the "alsoResize"
         parameter to make the imageContainer resize.  Then, in order to make
         the image resize in real-time as you're dragging, I use the "resize"
         event to scale the image up proportionally (and centered) inside the
         newly resized container.
         */
        const img = $(childImgContainer).find("img");
        $(element).resizable({
            handles: "nw, ne, sw, se",
            containment: "parent",
            alsoResize: childImgContainer
        });
    }
    //An Image Container div (which must have an inner <img>
    else if ($(element).hasClass("bloom-imageContainer")) {
        const img = $(element).find("img");
        $(element).resizable({
            handles: "nw, ne, sw, se",
            containment: "parent"
        });
    }
    // some other kind of resizable
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
            }
        });
    }
}

//jquery resizable normally uses pixels. This makes it use percentages, which are mor robust across page size/orientation changes
function ResizeUsingPercentages(e, ui) {
    const parent = ui.element.parent();
    ui.element.css({
        width: (ui.element.width() / parent.width()) * 100 + "%",
        height: (ui.element.height() / parent.height()) * 100 + "%"
    });

    //after any resize jquery adds an absolute position, which we don't want unless the user has resized
    //so this removes it, unless we previously noted that the user had moved it
    if ($(ui.element).data("doRestoreRelativePosition")) {
        ui.element.css({
            position: "",
            top: "",
            left: ""
        });
    }
    $(ui.element).removeData("hadPreviouslyBeenRelocated");
}
