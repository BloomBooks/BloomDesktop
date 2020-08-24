import "../../lib/jquery.resize"; // makes jquery resize work on all elements
import { BloomApi } from "../../utils/bloomApi";

// Enhance: this could be turned into a Typescript Module with only two public methods

import theOneLocalizationManager from "../../lib/localizationManager/localizationManager";
import { ImageDescriptionAdapter } from "../toolbox/imageDescription/imageDescription";
import { getToolboxFrameExports } from "../editViewFrame";

declare function ResetRememberedSize(element: HTMLElement);

const kPlaybackOrderContainerSelector: string =
    ".bloom-playbackOrderControlsContainer";

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
        .each(function() {
            SetupImageContainer(this);
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

    $(container)
        .find("img")
        .each(function() {
            SetAlternateTextOnImages(this);
        });
}

export function SetupImage(image) {
    // Remove any obsolete explicit image size and position left over from earlier versions of Bloom, before we had object-fit:contain.
    if (image.style) {
        image.style.width = "";
        image.style.height = "";
        image.style.marginLeft = "";
        image.style.marginTop = "";
    }
    if (image.getAttribute("style") === "") {
        image.removeAttribute("style");
    }
    image.removeAttribute("width");
    image.removeAttribute("height");
}

export function GetButtonModifier(container) {
    var buttonModifier = "";
    var imageButtonWidth = 87;
    var imageButtonHeight = 52;
    var $container = $(container);
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

//Bloom "imageContainer"s are <div>'s with wrap an <img>, and automatically proportionally resize
//the img to fit the available space
//Precondition: containerDiv must be just a single HTMLElement
function SetupImageContainer(containerDiv: any) {
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

    $(containerDiv)
        .mouseenter(function() {
            const $this = $(this);
            let img = $this.find("img");
            if (img.length === 0)
                //TODO check for bloom-backgroundImage to make sure this isn't just a case of a missing <img>
                // TODO: Looks like assigning an HTMLElement to a JQuery. I'd rather we not do this, although I think we manage to $() wrap our way out of this mess later.
                img = containerDiv; //using a backgroundImage

            if ($this.find(kPlaybackOrderContainerSelector).length > 0) {
                return; // Playback order controls are active, deactivate image container stuff.
            }
            const buttonModifier = GetButtonModifier($this);

            $this.prepend(
                '<button class="miniButton cutImageButton imageOverlayButton disabled ' +
                    buttonModifier +
                    '" title="' +
                    theOneLocalizationManager.getText(
                        "EditTab.Image.CutImage"
                    ) +
                    '"></button>'
            );
            $this.prepend(
                '<button class="miniButton copyImageButton imageOverlayButton disabled ' +
                    buttonModifier +
                    '" title="' +
                    theOneLocalizationManager.getText(
                        "EditTab.Image.CopyImage"
                    ) +
                    '"></button>'
            );
            $this.prepend(
                '<button class="pasteImageButton imageButton imageOverlayButton ' +
                    buttonModifier +
                    '" title="' +
                    theOneLocalizationManager.getText(
                        "EditTab.Image.PasteImage"
                    ) +
                    '"></button>'
            );
            $this.prepend(
                '<button class="changeImageButton imageButton imageOverlayButton ' +
                    buttonModifier +
                    '" title="' +
                    theOneLocalizationManager.getText(
                        "EditTab.Image.ChangeImage"
                    ) +
                    '"></button>'
            );

            if (
                // Only show this button if the toolbox is also offering it. It might not offer it
                // if it's experimental and that settings isn't on, or for Bloom Enterprise reasons, or whatever.
                getToolboxFrameExports()
                    .getTheOneToolbox()
                    .getToolIfOffered(ImageDescriptionAdapter.kToolID)
            ) {
                $this.prepend(
                    '<button class="imageDescriptionButton imageButton imageOverlayButton ' +
                        buttonModifier +
                        '" title="' +
                        theOneLocalizationManager.getText(
                            "EditTab.Toolbox.ImageDescriptionTool" // not quite the "Show Image Description Tool", but... feeling parsimonious
                        ) +
                        '"></button>'
                );
                $this.find(".imageDescriptionButton").click(() => {
                    getToolboxFrameExports()
                        .getTheOneToolbox()
                        .activateToolFromId(ImageDescriptionAdapter.kToolID);
                });
            }

            SetImageTooltip(containerDiv, img);

            if (IsImageReal(img)) {
                $this.prepend(
                    '<button class="editMetadataButton imageButton imageOverlayButton ' +
                        buttonModifier +
                        '" title="' +
                        theOneLocalizationManager.getText(
                            "EditTab.Image.EditMetadata"
                        ) +
                        '"></button>'
                );
                $this.find(".miniButton").each(function() {
                    $(this).removeClass("disabled");
                });
            }

            $this.addClass("hoverUp");
        })
        .mouseleave(function() {
            const $this = $(this);
            $this.removeClass("hoverUp");
            $this.find(".imageOverlayButton").each(function() {
                // leave the problem indicator visible
                if (!$(this).hasClass("imgMetadataProblem")) {
                    $(this).remove();
                }
            });
        });
}

function SetImageTooltip(container, img) {
    var url = GetRawImageUrl(img);
    // Don't try to go getting image info for a built in Bloom image (like cogGrey.svg).
    // It'll just throw an exception.
    if (url.startsWith("/bloom/")) {
        container.title = "";
        return;
    }
    BloomApi.getWithConfig(
        "image/info",
        { params: { image: GetRawImageUrl(img) } },
        result => {
            var image: any = result.data;
            // This appears to be constant even on higher dpi screens.
            // (See http://www.w3.org/TR/css3-values/#absolute-lengths)
            const kBrowserDpi = 96;
            var dpi = Math.round(image.width / ($(img).width() / kBrowserDpi));
            var info =
                image.name +
                "\n" +
                getFileLengthString(image.bytes) +
                "\n" +
                image.width +
                " x " +
                image.height +
                "\n" +
                dpi +
                " DPI (should be 300-600)\n" +
                "Bit Depth: " +
                image.bitDepth.toString();
            container.title = info;
        }
    );
}

function getFileLengthString(bytes): String {
    const units = ["Bytes", "KB", "MB"];
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
function GetRawImageUrl(imgOrDivWithBackgroundImage) {
    if ($(imgOrDivWithBackgroundImage).hasAttr("src")) {
        return $(imgOrDivWithBackgroundImage).attr("src");
    }
    //handle divs with background-image in an inline style attribute
    if ($(imgOrDivWithBackgroundImage).hasAttr("style")) {
        var style = $(imgOrDivWithBackgroundImage).attr("style");
        // see http://stackoverflow.com/questions/9723889/regex-to-match-urls-in-inline-styles-div-style-url
        //var result = (/url\(\s*(['"]?)(.*?)\1\s*\)/.exec(style) || [])[2];
        return (/url\s*\(\s*(['"]?)(.*?)\1\s*\)/.exec(style) || [])[2];
    }
    return "";
}
export function SetImageElementUrl(imgOrDivWithBackgroundImage, url) {
    if (imgOrDivWithBackgroundImage.tagName.toLowerCase() === "img") {
        imgOrDivWithBackgroundImage.src = url;
    } else {
        imgOrDivWithBackgroundImage.style =
            "background-image:url('" + url + "')";
    }
}
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
            var img = $(this).find("img");
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
    var copyright = $(img).attr("data-copyright");
    if (!copyright || copyright.length === 0) {
        var buttonClasses = `editMetadataButton imageButton imgMetadataProblem ${GetButtonModifier(
            container
        )}`;
        var englishText =
            "Image is missing information on Credits, Copyright, or License";
        theOneLocalizationManager
            .asyncGetText(
                "EditTab.Image.MissingInfo",
                englishText,
                "tooltip text"
            )
            .done(translation => {
                var title = translation.replace(/'/g, "&apos;");
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
                    theOneLocalizationManager.simpleDotNetFormat(englishText, [
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
    var parent = ui.element.parent();
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
