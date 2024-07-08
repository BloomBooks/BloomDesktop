///<reference path="./jquery.hasAttr.d.ts" />
/// <reference path="../../typings/jquery.qtip.d.ts" />
import * as $ from "jquery";
import bloomQtipUtils from "./bloomQtipUtils";
import {
    cleanupImages,
    SetOverlayForImagesWithoutMetadata,
    SetupResizableElement,
    SetupImagesInContainer
} from "./bloomImages";
import { SetupVideoEditing } from "./bloomVideo";
import { SetupWidgetEditing } from "./bloomWidgets";
import { setupOrigami, cleanupOrigami } from "./origami";
import theOneLocalizationManager from "../../lib/localizationManager/localizationManager";
import StyleEditor from "../StyleEditor/StyleEditor";
import OverflowChecker from "../OverflowChecker/OverflowChecker";
import BloomField from "../bloomField/BloomField";
import BloomNotices from "./bloomNotices";
import BloomSourceBubbles from "../sourceBubbles/BloomSourceBubbles";
import BloomHintBubbles from "./BloomHintBubbles";
import { initializeBubbleManager, theOneBubbleManager } from "./bubbleManager";
import { showTopicChooserDialog } from "../TopicChooser/TopicChooserDialog";
import "../../modified_libraries/jquery-ui/jquery-ui-1.10.3.custom.min.js";
import "./jquery.hasAttr.js"; //reviewSlog for CenterVerticallyInParent
import "../../lib/jquery.qtip.js";
import "../../lib/jquery.qtipSecondary.js";
import "../../lib/long-press/jquery.longpress.js";
import "jquery.hotkeys"; //makes the on(keydown work with keynames)
import "../../lib/jquery.resize"; // makes jquery resize work on all elements
import {
    getEditTabBundleExports,
    getToolboxBundleExports
} from "./bloomFrames";
import { showInvisibles, hideInvisibles } from "./showInvisibles";

//promise may be needed to run tests with phantomjs
//import promise = require('es6-promise');
//promise.Promise.polyfill();
import axios from "axios";
import {
    get,
    post,
    postBoolean,
    postData,
    postString,
    postThatMightNavigate
} from "../../utils/bloomApi";
import { showRequestStringDialog } from "../../react_components/RequestStringDialog";
import { fixUpDownArrowEventHandler } from "./arrowKeyWorkaroundManager";

import { hookupLinkHandler } from "../../utils/linkHandler";
import {
    BloomPalette,
    getHexColorsForPalette
} from "../../react_components/color-picking/bloomPalette";
import { ckeditableSelector } from "../../utils/shared";
import { EditableDivUtils } from "./editableDivUtils";
import { removeToolboxMarkup } from "../toolbox/toolbox";
import { IBloomWebSocketEvent } from "../../utils/WebSocketManager";
import { setupDragActivityTabControl } from "../toolbox/dragActivity/dragActivityTool";

// Allows toolbox code to make an element properly in the context of this iframe.
export function makeElement(
    html: string,
    parent: JQuery,
    resizableArgs,
    draggableArgs
): JQuery {
    const result = $(html);
    if (parent) {
        parent.prepend(result);
    }
    if (resizableArgs) {
        result.resizable(resizableArgs);
    }
    if (draggableArgs) {
        result.draggable(draggableArgs);
    }
    return result;
}

function isBrOrWhitespace(node) {
    return (
        node &&
        ((node.nodeType === 1 && node.nodeName.toLowerCase() === "br") ||
            (node.nodeType === 3 && /^\s*$/.test(node.nodeValue)))
    );
}

function removeTrailingWhiteSpace(node) {
    if (node && node.nodeType === 3 && node.nodeValue) {
        // Removes one or more (+) whitespace (\s) at the end ($), across multiple lines (m)
        node.nodeValue = node.nodeValue.replace(/\s+$/m, "");
    }
}

function TrimTrailingLineBreaksInDivs(node) {
    //    while ( isBrOrWhitespace(node.firstChild) ) {
    //        node.removeChild(node.firstChild);
    //    }
    while (isBrOrWhitespace(node.lastChild)) {
        node.removeChild(node.lastChild);
    }
    // Without this, FF can display a space which isn't a space due to a trailing \r\n
    removeTrailingWhiteSpace(node.lastChild);
}

function Cleanup() {
    // for stuff bloom introduces, just use this "bloom-ui" class to have it removed
    $(".bloom-ui").each(function() {
        $(this).remove();
    });

    bloomQtipUtils.cleanupBubbles(); // all 3 kinds!

    $("*.resize-sensor").remove(); // from css-element-queries
    $("*.editTimeOnly").remove();
    $("*.dragHandle").remove();
    $("*").removeAttr("data-easytabs");

    $("div.ui-resizable-handle").remove();
    $("div, figure").each(function() {
        $(this).removeClass("ui-draggable");
        $(this).removeClass("ui-resizable");
        $(this).removeClass("hoverUp");
    });
    $("span").each(function() {
        $(this).removeClass("ui-disableHighlight");
        $(this).removeClass("ui-enableHighlight");
    });

    $("button")
        // Note, previously this was just removing all <button>s.
        // We could have instead decided to enforce the rule that temporary
        // ui elements are supposed to have ".bloom-ui" (see above in this function)
        // Feeling cowardly, I'm introducing page-content so as to change existing behavior as little as possible.
        .not($(".page-content"))
        .each(function() {
            $(this).remove();
        });

    $("div.bloom-editable").each(function() {
        TrimTrailingLineBreaksInDivs(this);
    });

    cleanupImages();
    cleanupOrigami();
}

//add a delete button which shows up when you hover
function SetupDeletable(containerDiv) {
    $(containerDiv)
        .mouseenter(function() {
            const button = $(
                "<button class='deleteButton smallImageButton' title='Delete'></button>"
            );
            $(button).click(() => {
                $(containerDiv).remove();
            });
            $(this).prepend(button);
        })
        .mouseleave(function() {
            $(this)
                .find(".deleteButton")
                .each(function() {
                    $(this).remove();
                });
        });

    return $(containerDiv);
}

// Add various editing key handlers
function AddEditKeyHandlers(container) {
    //Make F6 apply a superscript style (later we'll change to ctrl+shift+plus, as word does. But capturing those in js by hand is a pain.
    //nb: we're avoiding ctrl+plus and ctrl+shift+plus (as used by MS Word), because they means zoom in browser. also three keys is too much
    $(container)
        .find("div.bloom-editable")
        .on("keydown", null, "F6", e => {
            const selection = document.getSelection();
            if (selection) {
                //NB: by using exeCommand, we get undo-ability
                document.execCommand(
                    "insertHTML",
                    false,
                    "<sup>" + document.getSelection() + "</sup>"
                );
            }
        });

    //ctrl alt 0 is from google drive for "normal text"
    $(container)
        .find("div.bloom-editable")
        .on("keydown", null, "ALT+CTRL+0", e => {
            e.preventDefault();
            document.execCommand("formatBlock", false, "P");
        });

    // Make F7 apply top-level header style (H1)
    $(container)
        .find("div.bloom-editable")
        .on("keydown", null, "F7", e => {
            e.preventDefault();
            document.execCommand("formatBlock", false, "H1");
        });
    $(container)
        .find("div.bloom-editable")
        .on("keydown", null, "ALT+CTRL+1", e => {
            //ctrl alt 1 is from google drive
            e.preventDefault();
            document.execCommand("formatBlock", false, "H1");
        });

    // Make F8 apply header style (H2)
    $(container)
        .find("div.bloom-editable")
        .on("keydown", null, "F8", e => {
            e.preventDefault();
            document.execCommand("formatBlock", false, "H2");
        });
    $(container)
        .find("div.bloom-editable")
        .on("keydown", null, "ALT+CTRL+2", e => {
            //ctrl alt 2 is from google drive
            e.preventDefault();
            document.execCommand("formatBlock", false, "H2");
        });

    // for testing only: show invisibles
    $(container)
        .find("div.bloom-editable")
        .on("keydown", null, "CTRL+SHIFT+SPACE", e => {
            showInvisibles(e);
        })
        // when user releases any key or clicks away from the editable
        // (if we only listen for keyup of the CTL+SHIFT+SPACE, it doesn't trigger if user lifts keys in wrong order)
        .on("keyup blur", null, e => {
            hideInvisibles(e);
        });

    $(document).keydown(e => {
        if (e.keyCode === 32 && e.ctrlKey && !e.shiftKey && !e.altKey) {
            document.execCommand("removeFormat"); //will remove bold, italics, etc. but not things that use elements, like h1
        }
    });

    //note: these have the effect of introducing a <div> inside of the div.bloom-editable we're in.
    //note: they aren't currently working. Debugging indicates the events never fire.
    //I (JohnT) have not been able to find any doc indicating that this syntax...passing a
    // keycode to match...is even supposed to work. If we want to reinstate them,
    // adding to the handler for ctrl-space above might work.
    $(document).bind("keydown", "ctrl+r", e => {
        e.preventDefault();
        document.execCommand("justifyright", false);
    });
    $(document).bind("keydown", "ctrl+l", e => {
        e.preventDefault();
        document.execCommand("justifyleft", false);
    });
    $(document).bind("keydown", "ctrl+shift+e", e => {
        //ctrl+shiift+e is what google drive uses
        e.preventDefault();
        document.execCommand("justifycenter", false);
    });

    // Note, CTRL+N is also caught, but up on the Shell where it is turned into an event,
    // so that it can be caught even when the focus isn't on the browser
}

// Add little language tags. (At one point we limited this to visible .bloom-editable divs,
// probably as an optimzation since there can be other-language divs present but hidden.
// But there may be yet others that are not visible when we run this but which soon will be,
// such as image descriptions. We don't seem to need the optimization, so let's just do
// them all.)
function AddLanguageTags(container) {
    $(container)
        .find(".bloom-editable[contentEditable=true]")
        .each(function() {
            const $this = $(this);

            // If this DIV already had a language tag, remove the content in case we decide the situation has changed.
            if ($this.hasAttr("data-languageTipContent")) {
                $this.removeAttr("data-languageTipContent");
            }

            // With a really small box that also had a hint qtip, there wasn't enough room and the two fought
            // with each other, leading to flashing back and forth
            // Of course that was from when Language Tags were qtips too, but I think I'll leave the restriction for now.
            if ($this.width() < 100) {
                return;
            }

            const key = $this.attr("lang");
            if (key !== undefined && (key === "*" || key.length < 1)) {
                return; //seeing a "*" was confusing even to me
            }
            // z is not a real language, it is used for prototype blocks, which are NEVER visible.
            // Searching for it causes missing-localization toasts if attempted.
            if (key === "z") {
                return;
            }

            // if this or any parent element has the class bloom-hideLanguageNameDisplay, we don't want to show any of these tags
            // first usage (for instance) was turning off language tags for a whole page
            if (
                $this.hasClass("bloom-hideLanguageNameDisplay") ||
                $this.parents(".bloom-hideLanguageNameDisplay").length !== 0
            ) {
                return;
            }

            let whatToSay = "";
            if (key !== undefined) {
                whatToSay = theOneLocalizationManager.getText(key);
                if (!whatToSay) {
                    whatToSay = key; //just show the code
                }
            }

            // Put whatToSay into data attribute for pickup by the css
            $this.attr("data-languageTipContent", whatToSay);
        });
}

function SetBookCopyrightAndLicenseButtonVisibility(container) {
    const shouldShowButton = !$(container)
        .find("DIV.copyright")
        .text();
    $(container)
        .find("button#editCopyrightAndLicense")
        .css("display", shouldShowButton ? "inline" : "none");
}

function GetEditor() {
    return new StyleEditor("/bloom/bookEdit");
}

function GetOverflowChecker() {
    return new OverflowChecker();
}

// this is also called by the StyleEditor
export function SetupThingsSensitiveToStyleChanges(container: HTMLElement) {
    $(container)
        .find(".bloom-translationGroup")
        .each(function() {
            // set our font size so that we can use em units when setting padding of the translation group
            // If visibility is under the control of the appearance system for this field the child we
            // want has bloom-contentFirst, otherwise, bloom-content1.
            // At the time of this writing (6.0) this is only used for cover page titles.
            let mainChild = $(this)
                .find(".bloom-contentFirst")
                .first();
            if (mainChild.length === 0) {
                mainChild = $(this)
                    .find(".bloom-content1")
                    .first();
            }
            const fontSizeOfL1 = mainChild.css("font-size");
            $(this).css("font-size", fontSizeOfL1);
        });
}

// called by C# to remove the id attribute from the image element
export function removeImageId(imageId: string) {
    const imgOrImageContainer = document.getElementById(imageId);
    imgOrImageContainer?.removeAttribute("id");
}

// called by c# so be careful about changing the signature, including names of parameters
export function changeImage(imageInfo: {
    imageId: string;
    src: string; // must already appropriately URL-encoded.
    copyright: string;
    creator: string;
    license: string;
}) {
    const imgOrImageContainer = document.getElementById(imageInfo.imageId);
    if (!imgOrImageContainer) {
        throw new Error(
            `changeImage: imageOrImageContainerId: "${imageInfo.imageId}" not found`
        );
    }
    // I can't remember why, but what this is doing is saying that if the imageContainer
    // has an <img> element, we're setting the src on that. But if it does not, we're
    // setting the background-image on the container itself.
    if (imgOrImageContainer.tagName === "IMG") {
        (imgOrImageContainer as HTMLImageElement).src = imageInfo.src;
    }
    // else if it has class bloom-imageContainer, we need to set the background-image on the container
    else if (imgOrImageContainer.classList.contains("bloom-imageContainer")) {
        imgOrImageContainer.setAttribute(
            "style",
            "background-image:url('" + imageInfo.src + "')"
        );
    }
    imgOrImageContainer.setAttribute("data-copyright", imageInfo.copyright);
    imgOrImageContainer.setAttribute("data-creator", imageInfo.creator);
    imgOrImageContainer.setAttribute("data-license", imageInfo.license);
    const ancestor = imgOrImageContainer.parentElement?.parentElement;
    if (ancestor) {
        SetOverlayForImagesWithoutMetadata(ancestor);
    }
    // id is just a temporary expedient to find the right image easily in this method.
    imgOrImageContainer.removeAttribute("id");
}

// This origami checking business is related BL-13120
let hadOrigamiWhenWeLoadedThePage: boolean;
function recordWhatThisPageLooksLikeForSanityCheck(container: HTMLElement) {
    hadOrigamiWhenWeLoadedThePage = hasOrigami(container);
}
function hasOrigami(container: HTMLElement) {
    return container.getElementsByClassName("split-pane-component").length > 0;
}

// Originally, all this code was in document.load and the selectors were acting
// on all elements (not bound by the container).  I added the container bound so we
// can add new elements (such as during layout mode) and call this on only newly added elements.
// Now document.load calls this with $('body') as the container.
// REVIEW: Some of these would be better off in OneTimeSetup, but too much risk to try to decide right now.
export function SetupElements(container: HTMLElement) {
    recordWhatThisPageLooksLikeForSanityCheck(container);

    SetupImagesInContainer(container);

    SetupVideoEditing(container);
    SetupWidgetEditing(container);
    initializeBubbleManager();

    $(container)
        .find(".bloom-editable")
        .each(function() {
            BloomField.ManageField(this);
        });

    // set up a click action on originalTitle if present
    const citations = container.getElementsByTagName("cite");
    const originalTitleCitations = Array.from(citations).filter(
        (c: HTMLElement) =>
            c.parentElement!.getAttribute("data-derived") ===
            "originalCopyrightAndLicense"
    );
    originalTitleCitations.forEach((titleElement: HTMLElement) => {
        titleElement.onclick = () => {
            showRequestStringDialog(
                titleElement.innerText,
                "EditTab.FrontMatter.EditOriginalTitleCaption",
                "Edit Original Title",
                "EditTab.FrontMatter.EditOriginalTitleLabel",
                "Original Title",
                newTitle => {
                    titleElement.innerText = newTitle;
                    if (newTitle) {
                        titleElement.classList.remove("missingOriginalTitle");
                    } else {
                        titleElement.classList.add("missingOriginalTitle");
                    }
                }
            );
        };
        SetupCustomMissingTitleStylesheet();
    });
    // set up a dynamic stylesheet that will show the "click to set original title"

    //make textarea edits go back into the dom (they were designed to be POST'ed via forms)
    $(container)
        .find("textarea")
        .blur(function() {
            this.innerHTML = this.value;
        });

    const rootFrameExports = getEditTabBundleExports();
    const toolboxVisible = rootFrameExports.toolboxIsShowing();
    rootFrameExports.doWhenToolboxLoaded(toolboxFrameExports => {
        const toolbox = toolboxFrameExports.getTheOneToolbox();
        // toolbox might be undefined in unit testing?

        if (toolbox) {
            toolbox.configureElementsForTools(container);
            const page = container.getElementsByClassName(
                "bloom-page"
            )[0] as HTMLElement;
            // When this is called initially on page load, container is the body,
            // and we will find a bloom-page and adjust the tool list.
            // If it is called later to adjust something like an image container,
            // the toolbox should already be set for this page.
            // In that case we won't find a page inside it, which is fine since
            // we don't need to adjust the tool list again.
            if (page) {
                toolbox.adjustToolListForPage(page);
            }
        }
    });

    SetBookCopyrightAndLicenseButtonVisibility(container);

    //CSS normally can't get at the text in order to, for example, show something different if it is empty.
    //This allows you to add .bloom-needs-data-text to a bloom-translationGroup in order to get
    //its child bloom-editable's to have data-texts's on them
    $(container)
        .find(".bloom-translationGroup.bloom-text-for-css .bloom-editable")
        .each(function() {
            // initially fill it
            $(this).attr("data-text", this.textContent);
            // keep it up to date
            $(this).on("blur paste input", function() {
                $(this).attr("data-text", this.textContent);
            });
        });
    SetupThingsSensitiveToStyleChanges(container);

    $(container)
        .find(".bloom-translationGroup")
        .each(function() {
            // If we get focus on a translation group, move it to the first editable. See BL-11922.
            $(this).focus(e => {
                $(e.target)
                    ?.find("div.bloom-editable:visible")
                    ?.first()
                    ?.focus();
            });

            //in bilingual/trilingual situation, re-order the boxes to match the content languages, so that stylesheets don't have to
            const contentElements = $(this).find(
                "textarea, div.bloom-editable"
            );
            contentElements.sort((a, b) => {
                //using negatives so that something with none of these labels ends up with a > score and at the end
                //reviewSlog
                const scoreA =
                    ($(a).hasClass("bloom-content1") ? 1 : 0) * -3 +
                    ($(a).hasClass("bloom-content2") ? 1 : 0 * -2) +
                    ($(a).hasClass("bloom-content3") ? 1 : 0 * -1);
                const scoreB =
                    ($(b).hasClass("bloom-content1") ? 1 : 0) * -3 +
                    ($(b).hasClass("bloom-content2") ? 1 : 0 * -2) +
                    ($(b).hasClass("bloom-content3") ? 1 : 0 * -1);
                if (scoreA < scoreB) {
                    return -1;
                }
                if (scoreA > scoreB) {
                    return 1;
                }
                return 0;
            });
            //do the actual rearrangement
            $(this).append(contentElements);
        });

    //Convert Standard Format Markers in the pasted text to html spans
    // Also (see BL-5575), if the parent div has .bloom-userCannotModifyStyles,
    // only allow plain text paste.
    $(container)
        .find("div.bloom-editable")
        .on("paste", function(e) {
            const theEvent = e.originalEvent as ClipboardEvent;
            if (!theEvent.clipboardData) return;

            const s = theEvent.clipboardData.getData("text/plain");
            if (s == null || s === "") return;

            if (
                $(this)
                    .parent()
                    .hasClass("bloom-userCannotModifyStyles")
            ) {
                e.preventDefault();
                document.execCommand("insertText", false, s);
                //NB: odd that this doesn't work?! document.execCommand("paste", false, s);
                return;
            }
        });

    AddEditKeyHandlers(container);

    //--------------------------------
    //keep divs vertically centered (yes, I first tried *all* the css approaches, they don't work for our situation)

    //do it initially
    $(container)
        .find(".bloom-centerVertically")
        .CenterVerticallyInParent();
    //reposition as needed
    $(container)
        .find(".bloom-centerVertically")
        .resize(function() {
            //nb: this uses a 3rd party resize extension from Ben Alman; the built in jquery resize only fires on the window
            $(this).CenterVerticallyInParent();
        });

    //html5 provides for a placeholder attribute, but not for contenteditable divs like we use.
    //So one of our foundational stylesheets looks for @data-placeholder and simulates the
    //@placeholder behavior.
    //Now, what's going on here is that we also support
    //<label class='placeholder'> inside a div.bloom-translationGroup to get this placeholder
    //behavior on each of the fields inside the group .
    //Using <label> instead of the attribute makes the html much easier to read, write, and add additional
    //behaviors through classes.
    //So the job of this bit here is to take the label.placeholder and create the data-placeholders.
    $(container)
        .find("*.bloom-translationGroup > label.placeholder")
        .each(function() {
            const labelText = $(this).text();

            //put the attributes on the individual child divs
            $(this)
                .parent()
                .find(".bloom-editable")
                .each(function() {
                    //enhance: it would make sense to allow each of these to be customized for their div
                    //so that you could have a placeholder that said "Name in {lang}", for example.
                    $(this).attr("data-placeholder", labelText);
                    //next, it's up to CSS to draw the placeholder when the field is empty.
                });
        });

    $(container)
        .find("div.bloom-editable")
        .each(function() {
            $(this).attr("contentEditable", "true");
        });

    // Bloom needs to make some fields readonly. E.g., the original license when the user is translating a shellbook
    // Normally, we'd control this is a style in editTranslationMode.css/editOriginalMode.css. However, "readonly" isn't a style, just
    // an attribute, so it can't be included in css.
    // The solution here is to add the readonly attribute when we detect that the css has set the cursor to "not-allowed".
    $(container)
        .find("textarea, div")
        .focus(function() {
            //        if ($(this).css('border-bottom-color') == 'transparent') {
            if ($(this).css("cursor") === "not-allowed") {
                $(this).attr("readonly", "true");
                $(this).removeAttr("contentEditable");
            } else {
                $(this).removeAttr("readonly");
                //review: do we need to add contentEditable... that could lead to making things editable that shouldn't be
            }
        });

    AddLanguageTags(container);

    //Same thing for divs which are potentially editable, but via the contentEditable attribute instead of TextArea's ReadOnly attribute
    // editTranslationMode.css/editOriginalMode.css can't get at the contentEditable (css can't do that), so
    // so they set the cursor to "not-allowed", and we detect that and set the contentEditable appropriately
    $(container)
        .find("div.bloom-readOnlyInTranslationMode")
        .focus(function() {
            if ($(this).css("cursor") === "not-allowed") {
                $(this).removeAttr("contentEditable");
            } else {
                $(this).attr("contentEditable", "true");
            }
        });

    //first used in the Uganda SHRP Primer 1 template, on the image on day 1
    $(container)
        .find(".bloom-draggableLabel")
        .each(function() {
            // previous to June 2014, containment was not working, so some items may be
            // out of bounds. Or the stylesheet could change the size of things. This gets any such back in bounds.
            if ($(this).position().left < 0) {
                $(this).css("left", 0);
            }
            if ($(this).position().top < 0) {
                $(this).css("top", 0);
            }
            if (
                $(this).position().left + $(this).width() >
                $(this)
                    .parent()
                    .width()
            ) {
                $(this).css(
                    "left",
                    $(this)
                        .parent()
                        .width() - $(this).width()
                );
            }
            if (
                $(this).position().top >
                $(this)
                    .parent()
                    .height()
            ) {
                $(this).css(
                    "top",
                    $(this)
                        .parent()
                        .height() - $(this).height()
                );
            }

            $(this).draggable({
                //NB: this containment is of the translation group, not the editable inside it. So avoid margins on the translation group.
                containment: "parent",
                handle: ".dragHandle"
            });
        });

    $(container)
        .find(".bloom-draggableLabel")
        .mouseenter(function() {
            $(this).prepend(" <div class='dragHandle'></div>");
        });

    $(container)
        .find(".bloom-draggableLabel")
        .mouseleave(function() {
            $(this)
                .find(".dragHandle")
                .each(function() {
                    $(this).remove();
                });
        });

    bloomQtipUtils.repositionPictureDictionaryTooltips(container);

    /* Support in page combo boxes that set a class on the parent, thus making some change in the layout of the pge.
    Example:
             <select name="Story Style" class="bloom-classSwitchingCombobox">
                     <option value="Fictional">Fiction</option>
                     <option value="Informative">Informative</option>
     </select>
     */
    //First we select the initial value based on what class is currently set, or leave to the default if none of them
    $(container)
        .find(".bloom-classSwitchingCombobox")
        .each(function() {
            //look through the classes of the parent for any that match one of our combobox values
            for (let i = 0; i < this.options.length; i++) {
                const c = this.options[i].value;
                if (
                    $(this)
                        .parent()
                        .hasClass(c)
                ) {
                    $(this).val(c);
                    break;
                }
            }
        });
    //And now we react to the user choosing a different value
    $(container)
        .find(".bloom-classSwitchingCombobox")
        .change(function() {
            //remove any of the values that might already be set
            for (let i = 0; i < this.options.length; i++) {
                const c = this.options[i].value;
                $(this)
                    .parent()
                    .removeClass(c);
            }
            //add back in the one they just chose
            $(this)
                .parent()
                .addClass(this.value);
        });

    //only make things deletable if they have the deletable class *and* page customization is enabled
    $(container)
        .find(
            "DIV.bloom-page.bloom-enablePageCustomization DIV.bloom-deletable"
        )
        .each(function() {
            SetupDeletable(this);
        });

    BloomNotices.addExperimentalNotice(container); // adds notice to Picture Dictionary pages

    $(container)
        .find(".bloom-resizable")
        .each(function() {
            SetupResizableElement(this);
        });

    SetOverlayForImagesWithoutMetadata(container);

    //note, the normal way is for the user to click the link on the bubble.
    //But clicking on the existing topic may be natural too, and this prevents
    //them from editing it by hand.
    $(container)
        .find("div[data-derived='topic']")
        .click(function() {
            if ($(this).css("cursor") === "not-allowed") return;
            showTopicChooserDialog();
        });

    // Copy source texts out to their own div, where we can make a bubble with tabs out of them
    // We do this because if we made a bubble out of the div, that would suck up the vernacular editable area, too,
    const divsThatHaveSourceBubbles: HTMLElement[] = [];
    const bubbleDivs: any[] = [];
    if ($(container).find(".bloom-preventSourceBubbles").length === 0) {
        $(container)
            .find("*.bloom-translationGroup")
            .not(".bloom-readOnlyInTranslationMode")
            .each(function() {
                if ($(this).find("textarea, div").length > 1) {
                    const bubble = BloomSourceBubbles.ProduceSourceBubbles(
                        this
                    );
                    if (bubble.length !== 0) {
                        divsThatHaveSourceBubbles.push(this);
                        bubbleDivs.push(bubble);
                    }
                }
            });
    }

    //NB: this should be after the ProduceSourceBubbles(), because hint-bubbles are lower
    // priority, and should not show if we already have a source bubble.
    // (Eventually we may make the hint part of the source bubble when there is one...Bl-4295.)
    // This would happen with the Book Title, which would have both
    // when there are source languages to show
    BloomHintBubbles.addHintBubbles(
        container,
        divsThatHaveSourceBubbles,
        bubbleDivs
    );

    // We seem to need a delay to get a reliable result in BloomSourceBubbles.MakeSourceBubblesIntoQtips()
    // as it calls BloomSourceBubbles.CreateAndShowQtipBubbleFromDiv(), which ends by calling
    // bloomQtipUtils.mightCauseHorizontallyOverlappingBubbles(); see the comment on this last method.
    // For getting focus set reliably, it seems best to do this whole loop inside one delay, rather than
    // have separate delays invoked each time through the loop.
    setTimeout(() => {
        for (let i = 0; i < bubbleDivs.length; i++) {
            BloomSourceBubbles.MakeSourceBubblesIntoQtips(
                divsThatHaveSourceBubbles[i],
                bubbleDivs[i]
            );
        }
        BloomSourceBubbles.setupSizeChangedHandling(divsThatHaveSourceBubbles);
        // Ensure focus exists as best we can (BL-7994)
        if (
            document.hasFocus() &&
            document.activeElement &&
            $(document.activeElement).find(":focusable").length > 0
        ) {
            // There seem to be cases where the active element does not actually have focus.
            // We like it to, so the user can actually type there.
            (document.activeElement as HTMLElement).focus();
            // It may already be focused, in which case, focusing it again may not trigger the side effect.
            // So do it explicitly.
            BloomSourceBubbles.ShowSourceBubbleForElement(
                document.activeElement
            );
            // bloomApi postDebugMessage(
            //     "DEBUG bloomEditing/SetupElements()/after delayed loop to make source bubbles - trying to show source bubble on " +
            //         document.activeElement.outerHTML
            // );
        } else {
            // bloomApi postDebugMessage(
            //     "DEBUG bloomEditing/SetupElements()/after delayed loop to make source bubbles - no active element: try to set focus"
            // );
            // nothing is focused. If there are TOP boxes on the page, it's possible we've just reloaded
            // the page after adding a TOP box. New TOP boxes are added last, so focusing the last one
            // is helpful to make sure the user can immediately type into the new TOP box. (BL-8502).
            // It's not obvious this is the most desirable focus when we're NOT doing comics, but it's still
            // probably as good a guess as anything.
            // The one case where it's definitely wrong is if we just added a TOP box to an image that isn't
            // the last image on the page. Then we will pick the wrong one. Getting that right is going to take
            // a pretty tricky solution, which I don't think we should attempt while stabilizing a beta, and
            // may not be worth it at all.
            // Note that there is code in bubbleManager.turnOnBubbleEditing() which tries to focus the last
            // TOP bubble. We have not figured out why it doesn't work. Until we do, the two should probably
            // be kept matching.
            if (!focusLastEditableTopBox()) {
                const firstEditable = $("body")
                    .find("textarea:visible, div.bloom-editable:visible")
                    .first();
                if (firstEditable.length) {
                    // bloomApi postDebugMessage(
                    //     "DEBUG bloomEditing/SetupElements()/after delayed loop to make source bubbles - setting focus on " +
                    //         firstEditable.get(0).outerHTML
                    // );
                    firstEditable.focus();
                } else {
                    // bloomApi postDebugMessage(
                    //     "DEBUG bloomEditing/SetupElements()/after delayed loop to make source bubbles - nothing to focus??"
                    // );
                }
            }
        }
    }, bloomQtipUtils.horizontalOverlappingBubblesDelay);

    // Add overflow event handlers so that when a div is overfull,
    // we add the overflow class and it gets a red background or something
    // Moved AddOverflowHandlers() after SetupImage() because some pages with lots of placeholders
    // were prematurely overflowing before the images were set to the right size.
    GetOverflowChecker().AddOverflowHandlers(container);

    const editor = GetEditor();

    // Applying this to the body element allows it to work for any bloom-editable that can get
    // focus, even ones that might not be visible (or might not exist yet) at the time we run this.
    $(document.body).on("focusin", e => {
        const editBox = $(e.target).closest(".bloom-editable");
        if (
            editBox.length &&
            editBox.closest(".bloom-userCannotModifyStyles").length === 0
        ) {
            editor.AttachToBox(editBox.get(0));
        }
    });

    const editableJQuery = $(container).find(".bloom-editable");

    loadLongpressInstructions(editableJQuery);

    //When we do a CTRL+A DEL, FF leaves us with a <br></br> at the start. When the first key is then pressed,
    //a blank line is shown and the letter pressed shows up after that.
    //This detects that situation when we type the first key after the deletion, and first deletes the <br></br>.
    editableJQuery.keypress(event => {
        // this is causing a worse problem, (preventing us from typing empty lines to move the start of the
        // text down), so we're going to live with the empty space for now.
        // TODO: perhaps we can act when the DEL or Backspace occurs and then detect this situation and clean it up.
        //         if ($(event.target).text() == "") { //NB: the browser inspector shows <br></br>, but innerHTML just says "<br>"
        //            event.target.innerHTML = "";
        //        }
    });
    //This detects that situation when we do CTRL+A and then type a letter, instead of DEL
    editableJQuery.keyup(function(event) {
        //console.log(event.target.innerHTML);
        // If they pressed a letter instead of DEL, we get this case:
        //NB: the browser inspector shows <br></br>, but innerHTML just says "<br>"
        if ($(event.target).find("#formatButton").length === 0) {
            // they have also deleted the formatButton, so put it back in

            // REVIEW: this shows that we're doing the attaching on the first character entered,
            // even though it appears the editor was already attached.
            // So we actually attach twice. That's ok, the editor handles that, but I don't know why
            // we're passing the if, and it could be improved.
            // console.log('attaching');
            if ($(this).closest(".bloom-userCannotModifyStyles").length === 0)
                editor.AttachToBox(this);
        } else {
            // already have a format cog, better make sure it's in the right place
            editor.AdjustFormatButton(this);
        }
    });

    // Up and down arrow navigation don't work in editables with paragraphs that are flexboxes.
    //   (See bug report: https://bugzilla.mozilla.org/show_bug.cgi?id=1414884)
    // So add our own custom handler to intercept the event and if applicable, fix it ourselves.
    editableJQuery.each((dummy, element) => {
        element.addEventListener("keydown", fixUpDownArrowEventHandler);
    });

    // make any added over-picture elements draggable and clickable
    if (theOneBubbleManager) {
        theOneBubbleManager.initializeOverPictureEditing();
    }

    // focus on the first editable field
    // HACK for BL-1139: except for some reason when the Reader tools are active this causes
    // quick typing on a newly loaded page to get the cursor messed up. So for the Reader tools, the
    // user will need to actually click in the div to start typing.
    if (!toolboxVisible) {
        // The focus set here may not actually be what is focused in the end.  Other setup
        // code (such as for Comical) may have other--or identical--ideas and set focus.
        //review: this might choose a textarea which appears after the div. Could we sort on the tab order?
        const editableElement = $(container)
            .find("textarea:visible, div.bloom-editable:visible")
            .first();
        if (editableElement.length) {
            // bloomApi postDebugMessage(
            //     "DEBUG bloomEditing/SetupElements() - setting focus on " +
            //         editableElement.get(0).outerHTML
            // );
            editableElement.focus();
            // bloomApi postDebugMessage(
            //     "DEBUG bloomEditing/SetupElements() - activeElement=" +
            //         (document.activeElement
            //             ? document.activeElement.outerHTML
            //             : "<NULL>")
            // );
        } else {
            // bloomApi postDebugMessage(
            //     "DEBUG bloomEditing/SetupElements() - found nothing to focus"
            // );
        }
    }

    AddXMatterLabelAfterPageLabel(container);
    ConstrainContentsOfPageLabel(container);
}

function focusLastEditableTopBox(): boolean {
    const topBoxes = document.getElementsByClassName("bloom-textOverPicture");
    if (topBoxes.length == 0) return false;
    const lastTop = topBoxes[topBoxes.length - 1];
    if (focusOnChildIfFound(lastTop, "bloom-editable")) return true;
    // image and video boxes are also possibilities (BL-11620)
    if (focusOnChildIfFound(lastTop, "bloom-imageContainer")) return true;
    if (focusOnChildIfFound(lastTop, "bloom-videoContainer")) return true;
    return false; // unexpected
}

function focusOnChildIfFound(lastTop: Element, className: string): boolean {
    const visibleChildBoxesInLastTop = Array.from(
        lastTop.getElementsByClassName(className)
    ).filter(
        // this is a crude check for visibility, but according to stack overflow
        // equivalent to the :visible check we were previously doing in jquery
        s => window.getComputedStyle(s).getPropertyValue("display") != "none"
    );
    if (visibleChildBoxesInLastTop.length !== 0) {
        // This doesn't work reliably if we just use the Element.focus(), so we use the JQuery version that
        // we set the eventhandler with in BloomSourceBubbles.SetupTooltips(). We've tried adding a "focusin"
        // eventhandler to the div, instead of the JQuery one, but it still didn't work when we used
        // the raw HTML focus() here, even if we changed the eventhandler to use currentTarget instead of target.
        // It may have something to do with the fact that JQuery calls trigger() on the event, rather than
        // calling the focus method. (BL-8726)
        $(visibleChildBoxesInLastTop[0] as HTMLElement).focus();
        return true;
    }
    return false;
}

// This function sets up a rule to display a prompt following the placeholder we insert for a missing
// "originalTitle" element. It is displayed using CSS :after so we don't have to modify the DOM to
// make it appear, which would risk having it show up in published books. We insert the CSS dynamically
// so we can localize the message. We don't have to worry about this stylesheet getting saved because
// only the page element from editable pages gets saved. Note that we want this stylesheet to exist
// whether or not the title is missing, since that can change with editing; but we only need it on
// (typically the one) page that has the cite element for the originalTitle.
function SetupCustomMissingTitleStylesheet() {
    const missingTitleStyleSheet = document.getElementById(
        "customMissingTitleStylesheet"
    );
    if (!missingTitleStyleSheet) {
        theOneLocalizationManager
            .asyncGetTextInLang(
                "EditTab.FrontMatter.EditOriginalTitlePlaceholder",
                "click to edit original title",
                "UI",
                ""
            )
            .done(result => {
                if (result) {
                    const newSheet = document.createElement("style");
                    document.head.appendChild(newSheet);
                    newSheet.setAttribute("id", "customMissingTitleStylesheet");
                    newSheet.type = "text/css";
                    newSheet.innerText =
                        '[data-derived="originalCopyrightAndLicense"] cite.missingOriginalTitle::after {content: "' +
                        result +
                        '";}';
                }
            });
    }
}

const pageLabelL18nPrefix = "TemplateBooks.PageLabel.";

function ConstrainContentsOfPageLabel(container) {
    const pageLabel = <HTMLDivElement>(
        document.getElementsByClassName("pageLabel")[0]
    );
    if (!pageLabel) return;
    $(pageLabel).blur(event => {
        // characters that cause problem in windows file names (linux is less picky, according to mono source)
        pageLabel.innerText = pageLabel.innerText
            .split(/[\/\\*:?"<>|]/)
            .join("");
        // characters that mess something else up, found through experimentation
        pageLabel.innerText = pageLabel.innerText.split(/[#%\r\n]/).join("");
        // update data-i18n attribute to prevent this change being forgotton on reload; BL-5855
        let localizationAttr = pageLabel.getAttribute("data-i18n");
        if (
            localizationAttr != null &&
            localizationAttr.startsWith(pageLabelL18nPrefix)
        ) {
            localizationAttr = pageLabelL18nPrefix + pageLabel.innerText;
            pageLabel.setAttribute("data-i18n", localizationAttr);
        }
    });
}

function AddXMatterLabelAfterPageLabel(container) {
    // All this rigamarole so we can localize...
    const pageLabel = <HTMLDivElement>(
        document.getElementsByClassName("pageLabel")[0]
    );
    if (!pageLabel) return;
    let xMatterLabel = window.getComputedStyle(pageLabel, ":before").content;
    if (xMatterLabel == null) return;
    xMatterLabel = xMatterLabel.replace(new RegExp('"', "g"), ""); //No idea why the quotes are still in there at this point.
    if (xMatterLabel === "" || xMatterLabel === "none") return;
    theOneLocalizationManager
        .asyncGetText(pageLabelL18nPrefix + xMatterLabel, xMatterLabel, "")
        .done(xMatterLabelTranslation => {
            theOneLocalizationManager
                .asyncGetText(
                    pageLabelL18nPrefix + "FrontBackMatter",
                    "Front/Back Matter",
                    ""
                )
                .done(frontBackTranslation => {
                    $(pageLabel).attr(
                        "data-after-content",
                        xMatterLabelTranslation + " " + frontBackTranslation
                    );
                });
        });
}

// Only put setup code here which is guaranteed to only be run once per page load.
// e.g. Don't put setup for elements such as image containers or editable boxes which may get added after page load.
function OneTimeSetup() {
    setupOrigami();
    hookupLinkHandler();
    setupDragActivityTabControl();
}

interface String {
    endsWith(string): boolean;
    startsWith(string): boolean;
}

function isTextSelected(): boolean {
    const selection = document.getSelection();
    return !!selection && !selection.isCollapsed;
}

let reportedTextSelected = isTextSelected();

// ---------------------------------------------------------------------------------
// called inside document ready function
// ---------------------------------------------------------------------------------
export function bootstrap() {
    bloomQtipUtils.setQtipZindex();

    $.fn.reverse = function() {
        return this.pushStack(this.get().reverse(), arguments);
    };

    document.addEventListener("selectionchange", () => {
        const textSelected = isTextSelected();
        if (textSelected != reportedTextSelected) {
            postBoolean("editView/isTextSelected", textSelected);
            reportedTextSelected = textSelected;
        }
    });
    // We could force this in C#, but it's easier to just send a message to convey the state
    // that the page is in to begin with. I think this is always false at bootstrap.
    reportedTextSelected = isTextSelected();
    postBoolean("editView/isTextSelected", reportedTextSelected);

    /* reviewSlog typescript just couldn't cope with this. Our browser has this built in , so it's ok
            //if this browser doesn't have endsWith built in, add it
            if (typeof String.prototype.endsWith !== 'function') {if (typeof String.prototype.endsWith !== 'function') {
                    String.prototype.endsWith = function (suffix) {
                            return this.indexOf(suffix, this.length - suffix.length) !== -1;
                    };
            }

            // Defines a starts-with function
            if (typeof String.prototype.startsWith != 'function') {
                    String.prototype.startsWith = function (str) {
                            return this.indexOf(str) == 0;
                    };
            }
    */
    //eventually we want to run this *after* we've used the page, but for now, it is useful to clean up stuff from last time
    Cleanup();

    SetupElements(document.body);
    OneTimeSetup();

    // configure ckeditor
    if (typeof CKEDITOR === "undefined") return; // this happens during unit testing
    CKEDITOR.disableAutoInline = true;

    if ($(this).find(".bloom-imageContainer").length) {
        // We would *like* to wire up ckeditor, but would need to get it to stop interfering
        // with the embedded image. See https://silbloom.myjetbrains.com/youtrack/issue/BL-3125.
        // Currently this is only possible in the grade 4 Uganda books by SIL-LEAD.
        // So for now, we just going to say that you don't get ckeditor inside fields that have an embedded image.
        return;
    }

    // Attach ckeditor to the fields that can have styled editable text.
    // (See comment above on ckeditableSelector for what fields those are.)
    $("div.bloom-page")
        .find(ckeditableSelector)
        .each((index: number, element: Element) => {
            attachToCkEditor(element);
        });

    // We want to do this as late in the page setup process as possible because a
    // mouse zoom event will regenerate the page, and various things we do in the process
    // of starting up a page don't like it if the page we are loading is already unloading.
    // We currently suppress errors for pages which are in the process of going away, but better
    // not to generate them than suppress them if we can help it.
    setupWheelZooming();
}
// Attach a function to implement zooming on mouse wheel with ctrl.
// Setting this up should be one of the last things we do when loading the page...
// see the comment above where it is called.
// (Unfortunately, this tends to make zooming feel rather sluggish...we could
// try to optimize that, possibly by trying to keep track of how many wheel events
// we got and using bigger increments...it should be safe to set up a handler
// that just counts them, as long as we don't initiate a new page load until we
// get done loading this one. Or maybe there are some events in page load that
// we could abort if we already got another zoom event. For now, just trying
// to stop it crashing.)
function setupWheelZooming() {
    $("body").on("wheel", e => {
        const theEvent = e.originalEvent as WheelEvent;
        if (!theEvent.ctrlKey) return;
        let command: string = "";
        // Note the direction of the zoom is opposite the direction of the scroll.
        if (theEvent.deltaY < 0) {
            command = "edit/pageControls/zoomPlus";
        } else if (theEvent.deltaY > 0) {
            command = "edit/pageControls/zoomMinus";
        }
        if (command != "") {
            // Zooming re-loads the page (because of a text-over-picture issue)
            postThatMightNavigate(command);
        }
        // Setting the zoom is all we want to do in this context.
        e.preventDefault();
        e.cancelBubble = true;
    });
}

export function localizeCkeditorTooltips(bar: JQuery) {
    // The tooltips for the CKEditor Bold, Italic and Underline buttons need localization.
    const toolGroup = bar.find(".cke_toolgroup");
    theOneLocalizationManager
        .asyncGetText("EditTab.DirectFormatting.Bold", "Bold", "")
        .done(result => {
            $(toolGroup)
                .find(".cke_button__bold")
                .attr("title", result);
        });
    theOneLocalizationManager
        .asyncGetText("EditTab.DirectFormatting.Italic", "Italic", "")
        .done(result => {
            $(toolGroup)
                .find(".cke_button__italic")
                .attr("title", result);
        });
    theOneLocalizationManager
        .asyncGetText("EditTab.DirectFormatting.Underline", "Underline", "")
        .done(result => {
            $(toolGroup)
                .find(".cke_button__underline")
                .attr("title", result);
        });
    theOneLocalizationManager
        .asyncGetText("EditTab.DirectFormatting.Superscript", "Superscript", "")
        .done(result => {
            $(toolGroup)
                .find(".cke_button__superscript")
                .attr("title", result);
        });
}

// This is invoked when we are about to change pages.
function removeOrigami() {
    // We are mirroring the origami layoutToggleClickHandler() here, in case the user changes
    // pages while the origami toggle in on.
    // The DOM here is for just one page, so there's only ever one marginBox.
    const marginBox = document.getElementsByClassName("marginBox")[0];
    marginBox.classList.remove("origami-layout-mode");
    const textLabels = marginBox.getElementsByClassName("textBox-identifier");
    for (let i = 0; i < textLabels.length; i++) {
        textLabels[i].remove();
    }
}

// This is invoked from C# when we are about to change pages. It removes markup we don't want to save.
// Then it calls an API with the information we need to save. This works around the lack of a
// non-async runJavascript API in WebView2.
export function requestPageContent() {
    try {
        // The toolbox is in a separate iframe, hence the call to getToolboxBundleExports().
        getToolboxBundleExports()?.removeToolboxMarkup();
        removeOrigami(); // Enhance this makes a change when better it would only changed the
        const content = getBodyContentForSavePage();
        const userStylesheet = userStylesheetContent();
        postString(
            "editView/pageContent",
            // We tossed up whether to use a JSON object here, but decided that it was simpler to just
            // combine the two strings with a delimiter that we can split on in C#.
            // For one thing, HTML requires some escaping to put in a JSON object, which would have
            // to be done in Javascript, and then undone in C#.
            content + "<SPLIT-DATA>" + userStylesheet
        );
    } catch (e) {
        postString(
            "editView/pageContent",
            "ERROR: " +
                e.message +
                "\n" +
                e.stack +
                "\n\n" +
                `document ${document ? "exists" : "does not exist"}` +
                "\n" +
                "body.innerHTML: " +
                document?.body?.innerHTML
        );
    }
}

// Caution: We don't want this to become an async method because we don't want
// any other event handlers running between cleaning up the page and
// getting the content to save. (Or think hard before changing that.)
export function getBodyContentForSavePage() {
    if (hadOrigamiWhenWeLoadedThePage && !hasOrigami(document.body)) {
        throw new Error(
            "getBodyContentForSavePage(): The page had origami when it loaded, but it doesn't now (check before cleanup). BL-13120"
        );
    }

    const bubbleEditingOn = theOneBubbleManager.isComicEditingOn;
    if (bubbleEditingOn) {
        theOneBubbleManager.turnOffBubbleEditing();
    }
    // Active element should be forced to blur
    if (document.activeElement instanceof HTMLElement) {
        document.activeElement.blur();
    }

    const editableDivs = <HTMLDivElement[]>(
        Array.from(document.querySelectorAll("div.bloom-editable"))
    );

    // We don't think we need to create ckEditor bookmarks and restore the selection
    // in this case because we are just saving the page.
    // In fact, it was causing problems when we were using them at one point.
    // (unfortunately, I don't remember what those problems were...).
    const createCkEditorBookMarks = false;
    EditableDivUtils.doCkEditorCleanup(editableDivs, createCkEditorBookMarks);

    if (hadOrigamiWhenWeLoadedThePage && !hasOrigami(document.body)) {
        throw new Error(
            "getBodyContentForSavePage(): The page had origami when it loaded, but it doesn't now (check after cleanup). BL-13120"
        );
    }

    const result = document.body.innerHTML;

    if (bubbleEditingOn) {
        theOneBubbleManager.turnOnBubbleEditing();
    }

    return result;
}

// Called from C# by a RunJavaScript() in EditingView.CleanHtmlAndCopyToPageDom via
// editTabBundle.getEditablePageBundleExports().
export const userStylesheetContent = () => {
    const ss = Array.from(document.styleSheets).find(
        s => s.title === "userModifiedStyles"
    ) as CSSStyleSheet | undefined;
    if (!ss) return "";
    return Array.from(ss.cssRules)
        .map(rule => rule.cssText)
        .join("\n");
};

export const pageUnloading = () => {
    // It's just possible that 'theOneBubbleManager' hasn't been initialized.
    // If not, just ignore this, since it's a no-op at this point anyway.
    if (theOneBubbleManager) {
        theOneBubbleManager.cleanUp();
    }
};

// These clipboard functions are implemented in Javascript because WebView2 doesn't seem to have
// a C# api for doing them. I've made the exported functions synchronous because I'm not sure
// what complications might come from calling an async function from C#. The implementations
// mostly use clipboard API functions that are async, so those functions must be async.
// We don't need to await them because nothing is using the result.
// The buttons that implement clipboard operations are currently only in Edit mode, so
// this is a reasonable place for this code. If we support them elsewhere, we'll have to
// find a way to share the code (and call it when not part of the editTabBundle).
export const copySelection = () => {
    copyImpl();
};

async function copyImpl() {
    const sel = document.getSelection();
    if (!sel) return;
    navigator.clipboard.writeText(sel.toString());
}

// See comment on copySelection
export const cutSelection = () => {
    cutSelectionImpl();
};

async function cutSelectionImpl() {
    const sel = document.getSelection();
    if (!sel) return;
    await navigator.clipboard.writeText(sel.toString());
    // Using ckeditor here because it's the only way I've found to integrate clipboard
    // ops into an Undo stack that we can operate from an external button.
    // We do a Save before and after to make sure that the cut is distinct from
    // any other editing and that ckEditor actually has an item in its undo stack
    // so that the Undo gets activated.
    (<any>CKEDITOR.currentInstance).undoManager.save(true);
    const range = CKEDITOR.currentInstance.getSelection().getRanges()[0];
    range.deleteContents();
    range.select(); // Select emptied range to place the caret in its place.
    (<any>CKEDITOR.currentInstance).undoManager.save(true);
    // This is a non-ckeditor way to perform the deletion, but doesn't integrate with Undo.
    //sel.deleteFromDocument();
}

// See comment on copySelection
export const pasteClipboardText = () => {
    pasteImpl();
};

async function pasteImpl() {
    // Using ckeditor here because it's the only way I've found to integrate clipboard
    // ops into an Undo stack that we can operate from an external button.
    // We do a Save before and after to make sure that the cut is distinct from
    // any other editing and that ckEditor actually has an item in its undo stack
    // so that the Undo gets activated.
    const textToPaste = await navigator.clipboard.readText();
    (<any>CKEDITOR.currentInstance).undoManager.save(true);
    CKEDITOR.currentInstance.insertText(textToPaste);
    (<any>CKEDITOR.currentInstance).undoManager.save(true);
    // Works but isn't undoable.
    //const sel = document.getSelection();
    // if (sel) {
    //     const range = sel.getRangeAt(0);
    //     range.deleteContents();
    //     range.insertNode(document.createTextNode(textToPaste));
    // }
}

export function loadLongpressInstructions(jQuerySetOfMatchedElements) {
    // using axios directly because we already have a catch...though not obviously better than the Bloom Api one?
    axios
        .get("/bloom/api/keyboarding/useLongpress")
        .then(response => {
            if (response.data) {
                theOneLocalizationManager
                    .asyncGetText(
                        "BookEditor.CharacterMap.Instructions",
                        "To select, use your mouse wheel or point at what you want, or press the key shown in purple. Finally, release the key that you pressed to show this list.",
                        ""
                    )
                    .done(translation => {
                        jQuerySetOfMatchedElements.longPress({
                            instructions:
                                "<div class='instructions'>" +
                                translation +
                                "</div>"
                        });
                    });
            } else {
                console.log("Longpress disabled");
            }
        })
        .catch(e => console.log("useLongpress query failed:" + e));
}

export function IsPageXMatter($target: JQuery): boolean {
    return (
        typeof $target.closest(".bloom-frontMatter")[0] !== "undefined" ||
        typeof $target.closest(".bloom-backMatter")[0] !== "undefined"
    );
}

function updateCkEditorButtonStatus(editor: CKEDITOR.editor) {
    get("editView/isClipboardBookHyperlink", result => {
        const pasteHyperlinkCommand = editor.getCommand("pasteHyperlink");
        if (result.data) {
            pasteHyperlinkCommand.enable();
        } else {
            pasteHyperlinkCommand.disable();
        }
    });
}

export function attachToCkEditor(element) {
    // Map from ckeditor id strings to the div the ckeditor is wrapping.
    const mapCkeditDiv = new Object();

    if (!element) {
        return;
    }

    // Skip any element with class="bloom-userCannotModifyStyles" (which might be on the translationGroup)
    if (
        $(element).hasClass("bloom-userCannotModifyStyles") ||
        $(element)
            .parentsUntil(".marginBox")
            .hasClass("bloom-userCannotModifyStyles")
    )
        return;

    if ($(element).css("cursor") === "not-allowed") return;

    // see bl-12448. Here we add a rule blocking visibility of the toolbar
    $("body").addClass("hideAllCKEditors");
    const ckedit = CKEDITOR.inline(element);

    // Record the div of the edit box for use later in positioning the format bar.
    mapCkeditDiv[ckedit.id] = element;

    // show or hide the toolbar when the text selection changes
    ckedit.on("selectionCheck", evt => {
        const editor = evt["editor"];
        // Length of selected text is more reliable than comparing
        // endpoints of the first range.  Mozilla can return multiple
        // ranges with the first one being empty.
        const textSelected = editor.getSelection().getSelectedText();
        const show = textSelected && textSelected.length > 0;
        const bar = $("body").find("." + editor.id);
        localizeCkeditorTooltips(bar);
        show ? bar.show() : bar.hide();

        // Move the format bar on the screen if needed.
        // (Note that offsets are not defined if it's not visible.)
        if (show) {
            updateCkEditorButtonStatus(editor);
            const barTop = bar.offset().top;
            const div = mapCkeditDiv[editor.id];
            const rect = div.getBoundingClientRect();
            const parent = bar.scrollParent();
            const scrollTop = parent ? parent.scrollTop() : 0;
            const boxTop = rect.top + scrollTop;
            if (boxTop - barTop < 5) {
                const barLeft = bar.offset().left;
                const barHeight = bar.height();
                bar.offset({ top: boxTop - barHeight, left: barLeft });
            }
            // for some reason when the color-choices panel has been shown once, it keeps coming
            // up immediately each time the toolbar is shown. Any change of selection is good reason
            // to hide it again. I'm using this specific way of hiding it because that seems to be
            // what CkEditor uses and therefore what it will change when the button is clicked to
            // show the popup panel.
            const colorPanels = Array.from(
                document.getElementsByClassName("cke_panel")
            );
            colorPanels.forEach(
                p => ((p as HTMLElement).style.display = "none")
            );

            // see bl-12448. Here we remove the rule blocking visibility of the toolbar so that
            // the `display` rule (managed by ckeditor) can take effect.
            $("body").removeClass("hideAllCKEditors");
        } else {
            // see bl-12448. Here we add a rule blocking visibility of the toolbar
            $("body").addClass("hideAllCKEditors");
        }
    });

    ckedit.on("focus", evt => {
        // see bl-12448. This one prevents a flash when switching from a field that has selected
        // text (and thus a visible toolbar) to another field.
        $("body").addClass("hideAllCKEditors");
        const editor = evt["editor"];
        updateCkEditorButtonStatus(editor);
    });

    // hide the toolbar when ckeditor starts
    ckedit.on("instanceReady", evt => {
        const editor = evt["editor"];
        const bar = $("body").find("." + editor.id);
        bar.hide();
    });

    if (CKEDITOR.config.colorButton_colors) {
        ckedit.config.colorButton_colors = CKEDITOR.config.colorButton_colors;
    } else {
        try {
            ckedit.config.colorButton_colors = "FFFFFF,FF0000"; // if something goes wrong, you get white and red
            getHexColorsForPalette(BloomPalette.Text).then(r => {
                // We cache the colors here so that we don't have to have the http round-trip
                // for each field. Currently it does pick up new colors on the next page
                // load. At the moment it doesn't seem worth the complexity of messing
                // with the ancient javascript in the ckeditor plugin to have it be
                // able to respond to the current palette without a page reload, but that would
                // be doable.
                // We're reusing this global because it exists, but this would probably work just
                // fine with out our own static if we wanted.
                CKEDITOR.config.colorButton_colors = r.join(",");
                ckedit.config.colorButton_colors =
                    CKEDITOR.config.colorButton_colors;
            });
            theOneLocalizationManager
                .asyncGetTextInLang(
                    "EditTab.DirectFormatting.labelForDefaultColor",
                    "Default for style",
                    "UI",
                    "A label that is shown next to the default color swatch, which is based on the current text default color."
                )
                .done(translation => {
                    CKEDITOR.config.labelForDefaultColor = translation;
                });
        } catch (error) {
            // swallow... it's not worth crashing over if something went bad in there.
        }
    }

    BloomField.WireToCKEditor(element, ckedit);
}
