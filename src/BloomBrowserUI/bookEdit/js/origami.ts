//not yet: neither bloomEditing nor this is yet a module import {SetupImage} from './bloomEditing';
///<reference path="../../lib/split-pane/split-pane.d.ts" /> 
///<reference path="../../typings/css-element-queries.d.ts" /> 
import {fireCSharpEditEvent} from './bloomEditing';
import {SetupImage} from './bloomImages';
import 'split-pane/split-pane.js';
import {ElementQueries} from 'css-element-queries';

$(function () {
    $('div.split-pane').splitPane();
});

export function setupOrigami() {
    $('.customPage').append(getOnOffSwitch().append(createTypeSelectors()).append(createTextBoxIdentifier()));

    $('.origami-toggle .onoffswitch').change(layoutToggleClickHandler);

    if ($('.customPage .marginBox.origami-layout-mode').length) {
        setupLayoutMode();
        $('#myonoffswitch').prop('checked', true);
    }
}

export function cleanupOrigami() {
    // Otherwise, we get a new one each time the page is loaded
    $('.split-pane-resize-shim').remove();
}
function isEmpty(el) {
    var temp = $.trim(el[0].textContent);
    //alert("-" + temp + "- equals empty string: " + (temp == "").toString());
    return temp == "";
}
function setupLayoutMode() {
    $('.split-pane-component-inner').each(function () {
        var $this = $(this);
        if ($this.find('.split-pane').length) {
            // This is an unexpected situation, probably caused by using a broken version of the
            // origami-based picture-in-middle. split-pane-component-inner's are not meant to be
            // wrapped around split-panes; they indicate leaves that can be further split, while
            // split-panes represent an area that has already been split. A reasonable repair is
            // to remove the split-pane-component-inner, leaving its contents.
            $this.find('.split-pane').unwrap();
            return true; // continue
        }
        if (!$this.find('.bloom-imageContainer, .bloom-translationGroup:not(.box-header-off)').length)
            $this.append(getTypeSelectors());

        $this.append(getButtons());
        var contents = $this.find('.bloom-translationGroup:not(.box-header-off) > .bloom-editable');
        if (!contents.length || (contents.length && !isEmpty(contents)))
            return true;
        $this.append(getTextBoxIdentifier());
    });
    // Text should not be editable in layout mode
    $('.bloom-editable:visible[contentEditable=true]').removeAttr('contentEditable');
    // Images cannot be changed (other than growing/shrinking with container) in layout mode
    $('.bloom-imageContainer').off('mouseenter').off('mouseleave');
    // Attaching to html allows it to work even if nothing has focus.
    $('html').on('keydown.origami', e => {
        if (e.keyCode === 89 && e.ctrlKey) {
            origamiRedo();
        }
        if (e.keyCode === 90 && e.ctrlKey) {
            origamiUndo();
        }
    });

    ElementQueries.init();//have  css-element-queries notice the new elements and track them, adding classes that let rules trigger depending on size
}

function layoutToggleClickHandler() {
    var marginBox = $('.marginBox');
    if (!marginBox.hasClass('origami-layout-mode')) {
        marginBox.addClass('origami-layout-mode');
        setupLayoutMode();
    } else {
        marginBox.removeClass('origami-layout-mode');
        marginBox.find('.bloom-translationGroup .textBox-identifier').remove();
        fireCSharpEditEvent('preparePageForEditingAfterOrigamiChangesEvent', '');
        origamiUndoStack.length = origamiUndoIndex = 0;
        $('html').off('keydown.origami');
    }
}

// Event handler to split the current box in half (vertically or horizontally)
function splitClickHandler() {
    var myInner = $(this).closest('.split-pane-component-inner');
    if ($(this).hasClass('add-top'))
        performSplit(myInner, 'horizontal', 'bottom', 'top', true);
    else if ($(this).hasClass('add-right'))
        performSplit(myInner, 'vertical', 'left', 'right', false);
    else if ($(this).hasClass('add-bottom'))
        performSplit(myInner, 'horizontal', 'top', 'bottom', false);
    else if ($(this).hasClass('add-left'))
        performSplit(myInner, 'vertical', 'right', 'left', true);

    ElementQueries.init();//notice the new elements and track them, adding classes that let rules trigger depending on size
}

function performSplit(innerElement, verticalOrHorizontal, existingContentPosition, newContentPosition, prependNew) {
    addUndoPoint();
    innerElement.wrap(getSplitPaneHtml(verticalOrHorizontal));
    innerElement.wrap(getSplitPaneComponentHtml(existingContentPosition));
    var newSplitPane = innerElement.closest('.split-pane');
    if (prependNew) {
        newSplitPane.prepend(getSplitPaneDividerHtml(verticalOrHorizontal));
        newSplitPane.prepend(getSplitPaneComponentWithNewContent(newContentPosition));
    } else {
        newSplitPane.append(getSplitPaneDividerHtml(verticalOrHorizontal));
        newSplitPane.append(getSplitPaneComponentWithNewContent(newContentPosition));
    }
    newSplitPane.splitPane();
}

var origamiUndoStack = [];
var origamiUndoIndex = 0; // of item that should be redone next, if any

// Add a point to which the user can return using 'undo'. Call this before making any change that
// would make sense to Undo in origami mode.
function addUndoPoint() {
    origamiUndoStack.length = origamiUndoIndex; // truncate any redo items
    var origamiRoot = $('.marginBox');
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
        var origamiRoot = $('.marginBox');
        // index may be 'out of range' but JS doesn't care
        // If there's already been an undo this is redundant but safe.
        // The main point is that the first Undo should make a new clone
        // indicating the state of things before the Undo.
        origamiUndoStack[origamiUndoIndex] = { original: origamiRoot.clone(true) };
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
        $('.marginBox').replaceWith(origamiUndoStack[origamiUndoIndex].original);
    }
}

// Event handler to add a new column or row (was working in demo but never wired up in Bloom)
//function addClickHandler() {
//    var topSplitPane = $('.split-pane-frame').children('div').first();
//    if ($(this).hasClass('right')) {
//        topSplitPane.wrap(getSplitPaneHtml('vertical'));
//        topSplitPane.wrap(getSplitPaneComponentHtml('left'));
//        var newSplitPane = $('.split-pane-frame').children('div').first();
//        newSplitPane.append(getSplitPaneDividerHtml('vertical'));
//        newSplitPane.append(getSplitPaneComponentWithNewContent('right'));
//        newSplitPane.splitPane();
//    } else if ($(this).hasClass('left')) {
//        topSplitPane.wrap(getSplitPaneHtml('vertical'));
//        topSplitPane.wrap(getSplitPaneComponentHtml('right'));
//        var newSplitPane = $('.split-pane-frame').children('div').first();
//        newSplitPane.prepend(getSplitPaneDividerHtml('vertical'));
//        newSplitPane.prepend(getSplitPaneComponentWithNewContent('left'));
//        newSplitPane.splitPane();
//    }
//}

function closeClickHandler() {
    if (!$('.split-pane').length) {
        // We're at the topmost element
        var marginBox = $(this).closest('.marginBox');
        addUndoPoint();
        marginBox.empty();
        marginBox.append(getSplitPaneComponentInner());
        return;
    }

    var myComponent = $(this).closest('.split-pane-component');                // the div/cell being removed
    //reviewSlog was doing a first('div') which is actually bogus, now parameters allowed.
    var sibling = myComponent.siblings('.split-pane-component').filter('div').first();  // the other div/cell in the same pane
    var toReplace = myComponent.parent().parent();                             // the div/cell containing the pane that contains the siblings above
    var positionClass = toReplace.attr('class');
    var positionStyle = toReplace.attr('style');
    addUndoPoint();

    toReplace.replaceWith(sibling);

    // The idea here is we need the position-* class from the parent to replace the sibling's position-* class.
    // This is working for now, but should be cleaned up since it could also add other classes.
    sibling.removeClass(function (index, css) {
        return (css.match(/(^|\s)position-\S+/g) || []).join(' ');
    });
    sibling.addClass(positionClass);
    sibling.attr('style', positionStyle);
}

function getSplitPaneHtml(verticalOrHorizontal) {
    return $('<div class="split-pane ' + verticalOrHorizontal + '-percent"></div>');
}
function getSplitPaneComponentHtml(position) {
    return $('<div class="split-pane-component position-' + position + '"></div>');
}
function getSplitPaneDividerHtml(verticalOrHorizontal) {
    var divider = $('<div class="split-pane-divider ' + verticalOrHorizontal + '-divider"></div>');
    return divider;
}
function getSplitPaneComponentWithNewContent(position) {
    var spc = $('<div class="split-pane-component position-' + position + '">');
    spc.append(getSplitPaneComponentInner());
    return spc;
}
function getSplitPaneComponentInner() {
    /* the stylesheet will hide this initially; we will have UI later than switches it to box-header-on */
    var spci = $('<div class="split-pane-component-inner adding"><div class="box-header-off bloom-translationGroup"></div></div>');
    spci.append(getTypeSelectors());
    spci.append(getButtons());
    return spci;
}

function getOnOffSwitch() {
    return $('\
<div class="origami-toggle bloom-ui"> \
    <div data-i18n="EditTab.CustomPage.ChangeLayout">Change Layout</div> \
    <div class="onoffswitch"> \
        <input type="checkbox" name="onoffswitch" class="onoffswitch-checkbox" id="myonoffswitch"> \
        <label class="onoffswitch-label" for="myonoffswitch"> \
            <span class="onoffswitch-inner"></span> \
            <span class="onoffswitch-switch"></span> \
        </label> \
    </div> \
</div>');
}
function getButtons() {
    var buttons = $('<div class="origami-controls bloom-ui origami-ui"></div>');
    buttons
        .append(getHorizontalButtons())
        .append(getCloseButtonWrapper())
        .append(getVerticalButtons());
    return buttons;
}
function getVerticalButtons() {
    var buttons = $('<div class="adders horizontal-adders"></div>');
    buttons
        .append(getVSplitButton(true))
        .append('<div class="separator"></div>')
        .append(getVSplitButton(null));
    return buttons;
}
function getVSplitButton(left) {
    var vSplitButton;
    if (left)
        vSplitButton = $('<a class="button  add-left">&#10010;</a>');
    else
        vSplitButton = $('<a class="button  add-right">&#10010;</a>');
    vSplitButton.click(splitClickHandler);
    return vSplitButton.wrap('<div></div>');
}
function getHorizontalButtons() {
    var buttons = $('<div class="adders vertical-adders"></div>');
    buttons
        .append(getHSplitButton(true))
        .append('<div class="separator"></div>')
        .append(getHSplitButton(null));
    return buttons;
}
function getHSplitButton(top) {
    var hSplitButton;
    if (top)
        hSplitButton = $('<a class="button  add-top">&#10010;</a>');
    else
        hSplitButton = $('<a class="button  add-bottom">&#10010;</a>');
    hSplitButton.click(splitClickHandler);
    return hSplitButton.wrap('<div></div>');
}
function getCloseButtonWrapper() {
    var wrapper = $('<div class="close-button-wrapper"></div>');
    wrapper.append(getCloseButton);
    return wrapper;
}
function getCloseButton() {
    var closeButton = $('<a class="button  close">&#10005;</a>');
    closeButton.click(closeClickHandler);
    return closeButton;
}
function createTypeSelectors() {
    var space = " ";
    var links = $('<div class="selector-links bloom-ui origami-ui"></div>');
    var pictureLink = $('<a href="" data-i18n="EditTab.CustomPage.Picture">Picture</a>');
    pictureLink.click(makePictureFieldClickHandler);
    var textLink = $('<a href="" data-i18n="EditTab.CustomPage.Text">Text</a>');
    textLink.click(makeTextFieldClickHandler);
    var orDiv = $('<div data-i18n="EditTab.CustomPage.Or">or</div>');
    links.append(pictureLink).append(space).append(orDiv).append(space).append(textLink);
    return $('<div class="container-selector-links bloom-ui origami-ui"></div>').append(links);
}
function createTextBoxIdentifier() {
    var textBoxId = $('<div class="textBox-identifier bloom-ui origami-ui" data-i18n="EditTab.CustomPage.TextBox">Text Box</div>');
    return $('<div class="container-textBox-id bloom-ui origami.ui"></div>').append(textBoxId);
}
function getTypeSelectors() {
    return $('.container-selector-links > .selector-links').clone(true);
}
function getTextBoxIdentifier() {
    return $('.container-textBox-id > .textBox-identifier').clone();
}
function makeTextFieldClickHandler(e) {
    e.preventDefault();
    var container = $(this).closest('.split-pane-component-inner');
    addUndoPoint();
    //note, we're leaving it to some other part of the system, later, to add the needed .bloom-editable
    //   divs (and set the right classes on them) inside of this new .bloom-translationGroup.
    var translationGroup = $('<div class="bloom-translationGroup bloom-trailingElement"></div>');
    $(translationGroup).addClass('normal-style'); // replaces above to make new text boxes normal
    container.append(translationGroup).append(getTextBoxIdentifier());
    $(this).closest('.selector-links').remove();
    //TODO: figure out if anything needs to get hooked up immediately
}
function makePictureFieldClickHandler(e) {
    e.preventDefault();
    var container = $(this).closest('.split-pane-component-inner');
    addUndoPoint();
    var imageContainer = $('<div class="bloom-imageContainer bloom-leadingElement"></div>');
    var image = $('<img src="placeHolder.png" alt="Could not load the picture"/>');
    imageContainer.append(image);
    SetupImage(image); // Must attach it first so event handler gets added to parent
    container.append(imageContainer);
    $(this).closest('.selector-links').remove();
}

function setStyle(data, translationGroup) {
    $(translationGroup).addClass('style' + data + '-style');
}
