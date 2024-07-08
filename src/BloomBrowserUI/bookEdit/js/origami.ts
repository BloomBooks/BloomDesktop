// not yet: neither bloomEditing nor this is yet a module import {SetupImage} from './bloomEditing';
///<reference path="../../lib/split-pane/split-pane.d.ts" />
import { SetupImage } from "./bloomImages";
import "../../lib/split-pane/split-pane.js";
import TextBoxProperties from "../TextBoxProperties/TextBoxProperties";
import { get, post, postThatMightNavigate } from "../../utils/bloomApi";
import { ElementQueries } from "css-element-queries";
import { theOneBubbleManager } from "./bubbleManager";

$(() => {
    $("div.split-pane").splitPane();
});

export function setupOrigami() {
    get("settings/enterpriseEnabled", result2 => {
        const isEnterpriseEnabled: boolean = result2.data;
        const customPages = document.getElementsByClassName("customPage");
        if (customPages.length > 0) {
            const width = customPages[0].clientWidth;
            const origamiControl = getAbovePageControlContainer()
                .append(createTypeSelectors(isEnterpriseEnabled))
                .append(createTextBoxIdentifier());
            $("#page-scaling-container").append(origamiControl);
            // The container width is set to 100% in the CSS, but we need to
            // limit it to no more than the actual width of the page.
            const toggleContainer = $(".above-page-control-container").get(0);
            toggleContainer.style.maxWidth = width + "px";
        }
        // I'm not clear why the rest of this needs to wait until we have
        // the two results, but none of the controls shows up if we leave it all
        // outside the bloomApi functions.
        $(".origami-toggle .onoffswitch").change(layoutToggleClickHandler);

        if ($(".customPage .marginBox.origami-layout-mode").length) {
            setupLayoutMode();
            $("#myonoffswitch").prop("checked", true);
        }

        $(".customPage, .above-page-control-container")
            .find("*[data-i18n]")
            .localize();
    });
}

export function cleanupOrigami() {
    // Otherwise, we get a new one each time the page is loaded
    $(".split-pane-resize-shim").remove();
}
function isEmpty(el) {
    const temp = $.trim(el[0].textContent);
    //alert("-" + temp + "- equals empty string: " + (temp == "").toString());
    return temp === "";
}
function setupLayoutMode() {
    theOneBubbleManager.suspendComicEditing("forTool");
    $(".split-pane-component-inner").each(function(): boolean {
        const $this = $(this);
        if ($this.find(".split-pane").length) {
            // This is an unexpected situation, probably caused by using a broken version of the
            // origami-based picture-in-middle. split-pane-component-inner's are not meant to be
            // wrapped around split-panes; they indicate leaves that can be further split, while
            // split-panes represent an area that has already been split. A reasonable repair is
            // to remove the split-pane-component-inner, leaving its contents.
            $this.find(".split-pane").unwrap();
            return true; // continue .each()
        }
        if (isSplitPaneComponentInnerEmpty($this))
            $this.append(getTypeSelectors());

        $this.append(getButtons());

        if (!doesSplitPaneComponentNeedTextBoxIdentifier($this)) {
            return true; // continue .each()
        }

        $this.append(getTextBoxIdentifier());

        return true; // continue .each(); though this is the end of the loop, it is needed for tsconfig's 'noImplicitReturns'
    });
    // Text should not be editable in layout mode
    $(".bloom-editable[contentEditable=true]").removeAttr("contentEditable");
    // Images cannot be changed (other than growing/shrinking with container) in layout mode
    $(".bloom-imageContainer")
        .off("mouseenter")
        .off("mouseleave");
    // Attaching to html allows it to work even if nothing has focus.
    $("html").on("keydown.origami", e => {
        if (e.keyCode === 89 && e.ctrlKey) {
            origamiRedo();
        }
        if (e.keyCode === 90 && e.ctrlKey) {
            origamiUndo();
        }
    });

    //have  css-element-queries notice the new elements and track them, adding classes that let rules trigger depending on size
    ElementQueries.init();
}

// N.B. If you add/remove a container class, you'll likely need to modify 'createTypeSelectors()' too.
const bloomContainerClasses =
    ".bloom-imageContainer, .bloom-widgetContainer, .bloom-videoContainer,";

function isSplitPaneComponentInnerEmpty(spci: JQuery) {
    return !spci.find(
        `${bloomContainerClasses} .bloom-translationGroup:not(.box-header-off)`
    ).length;
}

function doesSplitPaneComponentNeedTextBoxIdentifier(spci: JQuery) {
    // don't put the text box identifier in:
    //   image container
    //   video container
    //   widget container,
    // or where we just put the "Picture, Video, Widget or Text" selector links
    return !spci.find(`${bloomContainerClasses} .selector-links`).length;
}

function layoutToggleClickHandler() {
    const marginBox = $(".marginBox");
    if (!marginBox.hasClass("origami-layout-mode")) {
        marginBox.addClass("origami-layout-mode");
        setupLayoutMode();
        // Remove any left over formatButton from normal edit mode
        marginBox.find("#formatButton").remove();
        // Hook up TextBoxProperties dialog to each text box (via its origami overlay)
        const dialog = GetTextBoxPropertiesDialog();
        const overlays = marginBox.find(".textBox-identifier");
        overlays.each(function() {
            dialog.AttachToBox(this); // put the gear button in each text box identifier div
        });
    } else {
        // This line is currently redundant since we will reload the page, but in case we
        // stop doing that, it will be important.
        theOneBubbleManager.resumeComicEditing();

        marginBox.removeClass("origami-layout-mode");
        marginBox.find(".textBox-identifier").remove();
        origamiUndoStack.length = origamiUndoIndex = 0;
        // delay further processing to avoid messing up origami toggle transition
        // 400ms CSS toggle transition + 50ms extra to give it time to finish up.
        const toggleTransitionLength = 450;
        setTimeout(() => {
            $("html").off("keydown.origami");
            postThatMightNavigate("common/saveChangesAndRethinkPageEvent");
        }, toggleTransitionLength);
    }
}

function GetTextBoxPropertiesDialog() {
    return new TextBoxProperties("/bloom/bookEdit");
}

// Event handler to split the current box in half (vertically or horizontally)
function splitClickHandler() {
    const myInner = $(this).closest(".split-pane-component-inner");
    if ($(this).hasClass("add-top"))
        performSplit(myInner, "horizontal", "bottom", "top", true);
    else if ($(this).hasClass("add-right"))
        performSplit(myInner, "vertical", "left", "right", false);
    else if ($(this).hasClass("add-bottom"))
        performSplit(myInner, "horizontal", "top", "bottom", false);
    else if ($(this).hasClass("add-left"))
        performSplit(myInner, "vertical", "right", "left", true);

    ElementQueries.init(); //notice the new elements and track them, adding classes that let rules trigger depending on size
}

function performSplit(
    innerElement,
    verticalOrHorizontal,
    existingContentPosition,
    newContentPosition,
    prependNew
) {
    addUndoPoint();
    innerElement.wrap(getSplitPaneHtml(verticalOrHorizontal));
    innerElement.wrap(getSplitPaneComponentHtml(existingContentPosition));
    const newSplitPane = innerElement.closest(".split-pane");
    if (prependNew) {
        newSplitPane.prepend(getSplitPaneDividerHtml(verticalOrHorizontal));
        newSplitPane.prepend(
            getSplitPaneComponentWithNewContent(newContentPosition)
        );
    } else {
        newSplitPane.append(getSplitPaneDividerHtml(verticalOrHorizontal));
        newSplitPane.append(
            getSplitPaneComponentWithNewContent(newContentPosition)
        );
    }
    newSplitPane.splitPane();
}

var origamiUndoStack: any[] = [];
var origamiUndoIndex = 0; // of item that should be redone next, if any

// Add a point to which the user can return using 'undo'. Call this before making any change that
// would make sense to Undo in origami mode.
function addUndoPoint() {
    origamiUndoStack.length = origamiUndoIndex; // truncate any redo items
    const origamiRoot = $(".marginBox");
    // Currently the only thing in each undo entry is a clone of the marginBox at the
    // moment just before the original change. I decided to leave it an object in case
    // at some point we want to attach some more data (e.g., a name of what can be undone).
    origamiUndoStack.push({ original: origamiRoot.clone(true) });
    origamiUndoIndex = origamiUndoStack.length;
}

export function origamiCanUndo() {
    return origamiUndoIndex > 0;
}

export function origamiUndo() {
    if (origamiCanUndo()) {
        const origamiRoot = $(".marginBox");
        // index may be 'out of range' but JS doesn't care
        // If there's already been an undo this is redundant but safe.
        // The main point is that the first Undo should make a new clone
        // indicating the state of things before the Undo.
        origamiUndoStack[origamiUndoIndex] = {
            original: origamiRoot.clone(true)
        };
        origamiUndoIndex--;
        origamiRoot.replaceWith(origamiUndoStack[origamiUndoIndex].original);
    }
}

function origamiCanRedo() {
    return origamiUndoStack.length > origamiUndoIndex;
}

function origamiRedo() {
    if (origamiCanRedo()) {
        origamiUndoIndex++;
        $(".marginBox").replaceWith(
            origamiUndoStack[origamiUndoIndex].original
        );
    }
}

function closeClickHandler() {
    if (!$(".split-pane").length) {
        // We're at the topmost element
        const marginBox = $(this).closest(".marginBox");
        addUndoPoint();
        marginBox.empty();
        marginBox.append(getSplitPaneComponentInner());
        return;
    }
    // the div/cell being removed
    const myComponent = $(this).closest(".split-pane-component");
    //reviewSlog was doing a first('div') which is actually bogus, now parameters allowed.
    // the other div/cell in the same pane
    const sibling = myComponent
        .siblings(".split-pane-component")
        .filter("div")
        .first();
    // the div/cell containing the pane that contains the siblings above
    const toReplace = myComponent.parent().parent();
    const positionClass = toReplace.attr("class");
    let positionStyle = toReplace.attr("style");
    if (!positionStyle) {
        // If positionStyle is undefined, the assignment below doesn't actually change the style attribute.
        // See http://issues.bloomlibrary.org/youtrack/issue/BL-4168 for what could happen.
        positionStyle = "";
    }
    addUndoPoint();

    toReplace.replaceWith(sibling);

    // The idea here is we need the position-* class from the parent to replace the sibling's position-* class.
    // This is working for now, but should be cleaned up since it could also add other classes.
    sibling.removeClass((index, css) => {
        return (css.match(/(^|\s)position-\S+/g) || []).join(" ");
    });
    sibling.addClass(positionClass);
    sibling.attr("style", positionStyle);
}

function getSplitPaneHtml(verticalOrHorizontal) {
    return $(
        "<div class='split-pane " + verticalOrHorizontal + "-percent'></div>"
    );
}
function getSplitPaneComponentHtml(position) {
    return $(
        "<div class='split-pane-component position-" + position + "'></div>"
    );
}
function getSplitPaneDividerHtml(verticalOrHorizontal) {
    const divider = $(
        "<div class='split-pane-divider " +
            verticalOrHorizontal +
            "-divider'></div>"
    );
    return divider;
}
function getSplitPaneComponentWithNewContent(position) {
    const spc = $(
        "<div class='split-pane-component position-" + position + "'>"
    );
    spc.append(getSplitPaneComponentInner());
    return spc;
}
function getSplitPaneComponentInner() {
    const spci = $("<div class='split-pane-component-inner'></div>");
    spci.append(getTypeSelectors());
    spci.append(getButtons());
    return spci;
}

function getAbovePageControlContainer(): JQuery {
    // for dragActivities we don't want the origami control, but we still make the
    // wrapper so that the dragActivity can put a different control in it.
    if (
        document
            .getElementsByClassName("bloom-page")[0]
            ?.getAttribute("data-tool-id") === "dragActivity"
    ) {
        return $("<div class='above-page-control-container bloom-ui'></div>");
    }
    return $(
        "\
<div class='above-page-control-container bloom-ui'> \
<div class='origami-toggle bloom-ui'> \
    <div data-i18n='EditTab.CustomPage.ChangeLayout'>Change Layout</div> \
    <div class='onoffswitch'> \
        <input type='checkbox' name='onoffswitch' class='onoffswitch-checkbox' id='myonoffswitch'> \
        <label class='onoffswitch-label' for='myonoffswitch'> \
            <span class='onoffswitch-inner'></span> \
            <span class='onoffswitch-switch'></span> \
        </label> \
    </div> \
</div> \
</div>"
    );
}

function getButtons() {
    const buttons = $(
        "<div class='origami-controls bloom-ui origami-ui'></div>"
    );
    buttons
        .append(getHorizontalButtons())
        .append(getCloseButtonWrapper())
        .append(getVerticalButtons());
    return buttons;
}
function getVerticalButtons() {
    const buttons = $("<div class='adders horizontal-adders'></div>");
    buttons
        .append(getVSplitButton(true))
        .append("<div class='separator'></div>")
        .append(getVSplitButton(null));
    return buttons;
}
function getVSplitButton(left) {
    let vSplitButton;
    if (left) vSplitButton = $("<a class='button  add-left'>&#10010;</a>");
    else vSplitButton = $("<a class='button  add-right'>&#10010;</a>");
    vSplitButton.click(splitClickHandler);
    return vSplitButton.wrap("<div></div>");
}
function getHorizontalButtons() {
    const buttons = $("<div class='adders vertical-adders'></div>");
    buttons
        .append(getHSplitButton(true))
        .append("<div class='separator'></div>")
        .append(getHSplitButton(null));
    return buttons;
}
function getHSplitButton(top) {
    let hSplitButton;
    if (top) hSplitButton = $("<a class='button  add-top'>&#10010;</a>");
    else hSplitButton = $("<a class='button  add-bottom'>&#10010;</a>");
    hSplitButton.click(splitClickHandler);
    return hSplitButton.wrap("<div></div>");
}
function getCloseButtonWrapper() {
    const wrapper = $("<div class='close-button-wrapper'></div>");
    wrapper.append(getCloseButton);
    return wrapper;
}
function getCloseButton() {
    const closeButton = $("<a class='button  close'>&#10005;</a>");
    closeButton.click(closeClickHandler);
    return closeButton;
}

// N.B. If we ever add a new type, make sure you also modify 'bloomContainerClasses'.
function createTypeSelectors(includeWidget: boolean) {
    const space = " ";
    const links = $("<div class='selector-links bloom-ui origami-ui'></div>");
    const pictureLink = $(
        "<a href='' data-i18n='EditTab.CustomPage.Picture'>Picture</a>"
    );
    pictureLink.click(makePictureFieldClickHandler);
    const textLink = $(
        "<a href='' data-i18n='EditTab.CustomPage.Text'>Text</a>"
    );
    textLink.click(makeTextFieldClickHandler);
    const videoLink = $(
        "<a href='' data-i18n='EditTab.CustomPage.Video'>Video</a>"
    );
    videoLink.click(makeVideoFieldClickHandler);
    const orDiv = $("<div data-i18n='EditTab.CustomPage.Or'>or</div>");
    const htmlWidgetLink = $(
        "<a href='' data-i18n='EditTab.CustomPage.HtmlWidget'>HTML Widget</a>"
    );
    htmlWidgetLink.click(makeHtmlWidgetFieldClickHandler);
    links
        .append(pictureLink)
        .append(",")
        .append(space)
        .append(videoLink)
        .append(",")
        .append(space);
    if (includeWidget) {
        links
            .append(textLink)
            .append(",")
            .append(space)
            .append(orDiv)
            .append(space)
            .append(htmlWidgetLink);
    } else {
        links
            .append(orDiv)
            .append(space)
            .append(textLink);
    }
    return $(
        "<div class='container-selector-links bloom-ui origami-ui'></div>"
    ).append(links);
}
function createTextBoxIdentifier() {
    const textBoxId = $(
        "<div class='textBox-identifier bloom-ui origami-ui' data-i18n='EditTab.CustomPage.TextBox'>Text Box</div>"
    );
    return $(
        "<div class='container-textBox-id bloom-ui origami.ui'></div>"
    ).append(textBoxId);
}
function getTypeSelectors() {
    return $(".container-selector-links > .selector-links").clone(true);
}
function getTextBoxIdentifier() {
    return $(".container-textBox-id > .textBox-identifier").clone();
}
function makeTextFieldClickHandler(e) {
    e.preventDefault();
    const container = $(this).closest(".split-pane-component-inner");
    addUndoPoint();
    //note, we're leaving it to some other part of the system, later, to add the needed .bloom-editable
    //   divs (and set the right classes on them) inside of this new .bloom-translationGroup.
    const translationGroup = $(
        "<div class='bloom-translationGroup bloom-trailingElement'></div>"
    );
    $(translationGroup).addClass("normal-style"); // replaces above to make new text boxes normal
    container.append(translationGroup).append(getTextBoxIdentifier());
    $(this)
        .closest(".selector-links")
        .remove();
    // hook up TextBoxProperties dialog to this new Text Box (via its origami overlay)
    const dialog = GetTextBoxPropertiesDialog();
    const overlays = container.find(".textBox-identifier");
    overlays.each(function() {
        dialog.AttachToBox(this);
    });
}
function makePictureFieldClickHandler(e) {
    e.preventDefault();
    const container = $(this).closest(".split-pane-component-inner");
    addUndoPoint();
    const imageContainer = $(
        "<div class='bloom-imageContainer bloom-leadingElement'></div>"
    );
    const image = $(
        "<img src='placeHolder.png' alt='Could not load the picture'/>"
    );
    imageContainer.append(image);
    SetupImage(image); // Must attach it first so event handler gets added to parent
    container.append(imageContainer);
    $(this)
        .closest(".selector-links")
        .remove();
}

function makeVideoFieldClickHandler(e) {
    e.preventDefault();
    const container = $(this).closest(".split-pane-component-inner");
    addUndoPoint();
    const videoContainer = $(
        "<div class='bloom-videoContainer bloom-leadingElement bloom-noVideoSelected'></div>"
    );
    container.append(videoContainer);
    // For the book to look right when simply opened in an editor without the help of our local server,
    // the image needs to be in the book folder. Unlike the regular placeholder, which we copy
    // everywhere, this one is only meant to be around when needed. This call asks the server to make
    // sure it is present in the book folder.
    post("edit/pageControls/requestVideoPlaceHolder");
    $(this)
        .closest(".selector-links")
        .remove();
}

function makeHtmlWidgetFieldClickHandler(e) {
    e.preventDefault();
    const container = $(this).closest(".split-pane-component-inner");
    addUndoPoint();
    const widgetContainer = $(
        "<div class='bloom-widgetContainer bloom-leadingElement bloom-noWidgetSelected'></div>"
    );
    container.append(widgetContainer);
    // For the book to look right when simply opened in an editor without the help of our local server,
    // the image needs to be in the book folder. Unlike the regular placeholder, which we copy
    // everywhere, this one is only meant to be around when needed. This call asks the server to make
    // sure it is present in the book folder.
    post("edit/pageControls/requestWidgetPlaceHolder");
    $(this)
        .closest(".selector-links")
        .remove();
}
