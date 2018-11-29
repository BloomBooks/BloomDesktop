///<reference path="./jquery.hasAttr.d.ts" />
import * as $ from "jquery";
import bloomQtipUtils from "./bloomQtipUtils";
import {
    cleanupImages,
    SetOverlayForImagesWithoutMetadata,
    SetupResizableElement,
    SetupImagesInContainer
} from "./bloomImages";
import { SetupVideoEditing } from "./bloomVideo";
import { setupOrigami, cleanupOrigami } from "./origami";
import theOneLocalizationManager from "../../lib/localizationManager/localizationManager";
import StyleEditor from "../StyleEditor/StyleEditor";
import OverflowChecker from "../OverflowChecker/OverflowChecker";
import BloomField from "../bloomField/BloomField";
import BloomNotices from "./bloomNotices";
import BloomSourceBubbles from "../sourceBubbles/BloomSourceBubbles";
import BloomHintBubbles from "./BloomHintBubbles";
import {
    initializeTextOverPictureManager,
    theOneTextOverPictureManager
} from "./textOverPicture";
export { theOneTextOverPictureManager } from "./textOverPicture";
import TopicChooser from "../TopicChooser/TopicChooser";
import "jquery-ui/jquery-ui-1.10.3.custom.min.js";
import "jquery.hasAttr.js"; //reviewSlog for CenterVerticallyInParent
import "jquery.qtip.js";
import "jquery.qtipSecondary.js";
import "long-press/jquery.longpress.js";
import "jquery.hotkeys"; //makes the on(keydown work with keynames)
import "../../lib/jquery.resize"; // makes jquery resize work on all elements
import { getEditViewFrameExports } from "./bloomFrames";

//promise may be needed to run tests with phantomjs
//import promise = require('es6-promise');
//promise.Promise.polyfill();
import axios from "axios";
import { BloomApi } from "../../utils/bloomApi";

/**
 * Fires an event for C# to handle
 * @param {String} eventName
 * @param {String} eventData
 */
export function fireCSharpEditEvent(eventName, eventData) {
    const event = new MessageEvent(eventName, {
        /*'view' : window,*/ bubbles: true,
        cancelable: true,
        data: eventData
    });
    top.document.dispatchEvent(event);
}

export function GetDifferenceBetweenHeightAndParentHeight(jqueryNode) {
    // function also declared and used in StyleEditor
    if (!jqueryNode) {
        return 0;
    }
    return jqueryNode.parent().height() - jqueryNode.height();
}

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

    $("button").each(function() {
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
                    "<span class='superscript'>" +
                        document.getSelection() +
                        "</span>"
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

            // Make sure language tags appear or disappear depending on what edit mode we are in
            const isTranslationMode = IsInTranslationMode();
            if (
                isTranslationMode &&
                $this.hasClass("bloom-readOnlyInTranslationMode")
            ) {
                return;
            }
            if (
                !isTranslationMode &&
                $this.hasClass("bloom-readOnlyInAuthorMode")
            ) {
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

function DecodeHtml(encodedString) {
    return encodedString
        .replace(/&amp;/g, "&")
        .replace(/&lt;/g, "<")
        .replace(/&gt;/g, ">")
        .replace(/&#39;/g, "'")
        .replace(/&#169;/g, "©");
}

function GetEditor() {
    return new StyleEditor("/bloom/bookEdit");
}

function GetOverflowChecker() {
    return new OverflowChecker();
}

function IsInTranslationMode() {
    const body = $("body");
    // This used to check for "editMode", but that has been replaced by "bookcreationtype"
    // by the time we get here.
    if (!body.hasAttr("bookcreationtype")) {
        return false;
    } else {
        return body.attr("bookcreationtype") === "translation";
    }
}

let onLoadHappenedNormally = false;

const windowLoadedHandler = () => {
    onLoadHappenedNormally = true;
    fireCSharpEditEvent("jsNotification", "editPagePainted");
};

// When we don't already have a video (either a new page, or it has been deleted),
// and record a new one, we switchContentPage to make the new video show up.
// And for no known reason, the window load event never fires. There may possibly be
// other cases since I have no explanation. Rather than never sending the editPagePainted
// notification which would prevent saving subsequent changes to the page, if the event
// is delayed much longer than expected we just call the handler.
// For a similar event not firing problem, see editViewFrame.switchContentPage().
window.setTimeout(() => {
    if (onLoadHappenedNormally) {
        return;
    }
    windowLoadedHandler();
}, 1000);

window.onload = () => {
    // onload means we have all the parts, and waiting for one more animation frame
    // seems to mean it has actually been painted.
    window.requestAnimationFrame(windowLoadedHandler);
};

// Originally, all this code was in document.load and the selectors were acting
// on all elements (not bound by the container).  I added the container bound so we
// can add new elements (such as during layout mode) and call this on only newly added elements.
// Now document.load calls this with $('body') as the container.
// REVIEW: Some of these would be better off in OneTimeSetup, but too much risk to try to decide right now.
function SetupElements(container) {
    SetupImagesInContainer(container);
    SetupVideoEditing(container);
    initializeTextOverPictureManager();

    //add a marginBox if it's missing. We introduced it early in the first beta
    $(container)
        .find(".bloom-page")
        .each(function() {
            if ($(this).find(".marginBox").length === 0) {
                $(this).wrapInner("<div class='marginBox'></div>");
            }
        });
    $(container)
        .find(".bloom-editable")
        .each(function() {
            BloomField.ManageField(this);
        });

    //make textarea edits go back into the dom (they were designed to be POST'ed via forms)
    $(container)
        .find("textarea")
        .blur(function() {
            this.innerHTML = this.value;
        });

    const rootFrameExports = getEditViewFrameExports();
    const toolboxVisible = rootFrameExports.toolboxIsShowing();
    rootFrameExports.doWhenToolboxLoaded(toolboxFrameExports => {
        const toolbox = toolboxFrameExports.getTheOneToolbox();
        // toolbox might be undefined in unit testing?

        if (toolbox) {
            toolbox.configureElementsForTools(container);
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

    //in bilingual/trilingual situation, re-order the boxes to match the content languages, so that stylesheets don't have to
    $(container)
        .find(".bloom-translationGroup")
        .each(function() {
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
            const re = new RegExp("\\\\v\\s(\\d+)", "g");
            const matches = re.exec(s);
            if (matches == null) {
                //just let it paste
            } else {
                e.preventDefault();
                const x = s.replace(re, "<span class='superscript'>$1</span>");
                document.execCommand("insertHtml", false, x);
                //NB: this would undo, but it doesn't work document.execCommand("paste", false, x);
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

    // If the user moves over something they can't edit, show a tooltip explaining why not
    BloomNotices.addEditingNotAllowedMessages(container);

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
        .find("div[data-book='topic']")
        .click(function() {
            if ($(this).css("cursor") === "not-allowed") return;
            TopicChooser.showTopicChooser();
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

    for (let i = 0; i < bubbleDivs.length; i++) {
        BloomSourceBubbles.MakeSourceBubblesIntoQtips(
            divsThatHaveSourceBubbles[i],
            bubbleDivs[i]
        );
    }

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

    loadLongpressInstructions($(container).find(".bloom-editable"));

    //When we do a CTRL+A DEL, FF leaves us with a <br></br> at the start. When the first key is then pressed,
    //a blank line is shown and the letter pressed shows up after that.
    //This detects that situation when we type the first key after the deletion, and first deletes the <br></br>.
    $(container)
        .find(".bloom-editable")
        .keypress(event => {
            // this is causing a worse problem, (preventing us from typing empty lines to move the start of the
            // text down), so we're going to live with the empty space for now.
            // TODO: perhaps we can act when the DEL or Backspace occurs and then detect this situation and clean it up.
            //         if ($(event.target).text() == "") { //NB: the browser inspector shows <br></br>, but innerHTML just says "<br>"
            //            event.target.innerHTML = "";
            //        }
        });
    //This detects that situation when we do CTRL+A and then type a letter, instead of DEL
    $(container)
        .find(".bloom-editable")
        .keyup(function(event) {
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
                if (
                    $(this).closest(".bloom-userCannotModifyStyles").length ===
                    0
                )
                    editor.AttachToBox(this);
            } else {
                // already have a format cog, better make sure it's in the right place
                editor.AdjustFormatButton(this);
            }
        });

    // make any added text-over-picture bubbles draggable and clickable
    if (theOneTextOverPictureManager) {
        theOneTextOverPictureManager.makeTextOverPictureBoxDraggableClickableAndResizable();
    }

    // focus on the first editable field
    // HACK for BL-1139: except for some reason when the Reader tools are active this causes
    // quick typing on a newly loaded page to get the cursor messed up. So for the Reader tools, the
    // user will need to actually click in the div to start typing.
    if (!toolboxVisible) {
        //review: this might choose a textarea which appears after the div. Could we sort on the tab order?
        $(container)
            .find("textarea, div.bloom-editable")
            .first()
            .focus();
    }

    AddXMatterLabelAfterPageLabel(container);
    ConstrainContentsOfPageLabel(container);
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
    setupOrigami(IsInTranslationMode()); // 'true' means book is locked.
}

interface String {
    endsWith(string): boolean;
    startsWith(string): boolean;
}

// ---------------------------------------------------------------------------------
// called inside document ready function
// ---------------------------------------------------------------------------------
export function bootstrap() {
    bloomQtipUtils.setQtipZindex();

    $.fn.reverse = function() {
        return this.pushStack(this.get().reverse(), arguments);
    };

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

    SetupElements($("body"));
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
    // Map from ckeditor id strings to the div the ckeditor is wrapping.
    const mapCkeditDiv = new Object();

    // attach ckeditor to the contenteditable="true" class="bloom-content1"
    // also to contenteditable="true" and class="bloom-content2" or class="bloom-content3"
    // but skip any element with class="bloom-userCannotModifyStyles" (which might be on the translationGroup)
    let complicatedFind =
        ".bloom-content1[contenteditable='true'],.bloom-content2[contenteditable='true'],";
    complicatedFind +=
        ".bloom-content3[contenteditable='true'],.bloom-contentNational1[contenteditable='true']";
    $("div.bloom-page")
        .find(complicatedFind)
        .each(function() {
            if (
                $(this).hasClass("bloom-userCannotModifyStyles") ||
                $(this)
                    .parentsUntil(".marginBox")
                    .hasClass("bloom-userCannotModifyStyles")
            )
                return; // equivalent to 'continue'

            if ($(this).css("cursor") === "not-allowed") return;

            const ckedit = CKEDITOR.inline(this);

            // Record the div of the edit box for use later in positioning the format bar.
            mapCkeditDiv[ckedit.id] = this;

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
                }
            });

            // hide the toolbar when ckeditor starts
            ckedit.on("instanceReady", evt => {
                const editor = evt["editor"];
                const bar = $("body").find("." + editor.id);
                bar.hide();
            });

            BloomField.WireToCKEditor(this, ckedit);
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
            BloomApi.postThatMightNavigate(command);
        }
        // Setting the zoom is all we want to do in this context.
        e.preventDefault();
        e.cancelBubble = true;
    });
}

function localizeCkeditorTooltips(bar: JQuery) {
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

// This is invoked from C# when we are about to change pages. It is mainly for origami,
// but saveChangesAndRethinkPageEvent currently has the (very important)
// side effect of saving the changes to the current page.
// [GJM: But this doesn't call saveChangesAndRethinkPageEvent! I'm confused.]
export const pageSelectionChanging = () => {
    const marginBox = $(".marginBox");
    marginBox.removeClass("origami-layout-mode");
    marginBox.find(".bloom-translationGroup .textBox-identifier").remove();
};

// For usage, see editViewFrame.switchContentPage()
export const pageUnloading = () => {
    theOneTextOverPictureManager.cleanUp();
};

// This is invoked from C# when we are about to leave a page (often right after the previous
// method for changing pages).  It is mainly to clean things up so that garbage collection
// won't lose multiple megabytes of data that both the DOM (C++) and Javascript subsystems
// think the other is still using.
export const disconnectForGarbageCollection = () => {
    // disconnect all event handlers
    //review: was this, but TS didn't like it    $.find().off();
    $("body")
        .find("*")
        .off();

    const page = $(".bloom-page");
    // blow away any img elements to ensure their data disappears.
    // (the whole document is being replaced, and this happens after it's been saved to a file)
    page.find("img").each(function() {
        $(this).attr("src", "");
    });
    page.find("img").each(function() {
        $(this).remove();
    });
    $("[style*='.background-image']").each(function() {
        $(this).remove();
    });
};

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
