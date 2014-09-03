$(function() {
    $('div.split-pane').splitPane();
});
//$(window).load(function() {
//    $('.add').click(addClickHandler);
//});

function setupOrigami() {
    $('.customPage').append(getOnOffSwitch());

    $('.origami-toggle .onoffswitch').change(layoutToggleClickHandler);

    if($('marginBox.mode-layout').length) {
        setupLayoutMode();
        $('#myonoffswitch').prop('checked', true);
    }
}

function cleanupOrigami() {
    $('.marginBox.mode-layout').removeClass('mode-layout');
    // Otherwise, we get a new one each time the page is loaded
    $('.split-pane-resize-shim').remove();
}

function setupLayoutMode() {
    $('.split-pane-component-inner').each(function() {
        if (!$(this).find('.bloom-imageContainer, .bloom-translationGroup:not(.box-header-off)').length)
            $(this).append(getTypeSelectors());
        $(this).append(getButtons());
    });
    // Text should not be editable in layout mode
    $('.bloom-editable:visible[contentEditable=true]').removeAttr('contentEditable');
    // Images cannot be changed (other than growing/shrinking with container) in layout mode
    $('.bloom-imageContainer').off('mouseenter').off('mouseleave');
}

function layoutToggleClickHandler() {
    var marginBox = $('.marginBox');
    if (!marginBox.hasClass('mode-layout')) {
        marginBox.removeClass('mode-edit');
        marginBox.addClass('mode-layout');

        setupLayoutMode();
    } else {
        marginBox.removeClass('mode-layout');
        marginBox.addClass('mode-edit');

        fireCSharpEditEvent('preparePageForEditingAfterOrigamiChangesEvent', '');
    }
}

// Event handler to split the current box in half (vertically or horizontally)
function splitClickHandler() {
    var myInner = $(this).closest('.split-pane-component-inner');
    if ($(this).hasClass('splitter-top'))
        performSplit2(myInner, 'horizontal', 'bottom', 'top');
    else if ($(this).hasClass('splitter-right'))
        performSplit(myInner, 'vertical', 'left', 'right');
    else if ($(this).hasClass('splitter-bottom'))
        performSplit(myInner, 'horizontal', 'top', 'bottom');
    else if ($(this).hasClass('splitter-left'))
        performSplit2(myInner, 'vertical', 'right', 'left');
}

function performSplit(innerElement, verticalOrHorizontal, existingContentPosition, newContentPosition) {
    innerElement.wrap(getSplitPaneHtml(verticalOrHorizontal));
    innerElement.wrap(getSplitPaneComponentHtml(existingContentPosition));
    var newSplitPane = innerElement.closest('.split-pane');
    newSplitPane.append(getSplitPaneDividerHtml(verticalOrHorizontal));
    newSplitPane.append(getSplitPaneComponentWithNewContent(newContentPosition));
    newSplitPane.splitPane();
}

function performSplit2(innerElement, verticalOrHorizontal, existingContentPosition, newContentPosition) {
    innerElement.wrap(getSplitPaneHtml(verticalOrHorizontal));
    innerElement.wrap(getSplitPaneComponentHtml(existingContentPosition));
    var newSplitPane = innerElement.closest('.split-pane');
    newSplitPane.prepend(getSplitPaneDividerHtml(verticalOrHorizontal));
    newSplitPane.prepend(getSplitPaneComponentWithNewContent(newContentPosition));
    newSplitPane.splitPane();
}

// Event handler to add a new column or row (currently only works for column)
function addClickHandler() {
    var topSplitPane = $('.split-pane-frame').children('div').first();
    if ($(this).hasClass('right')) {
        topSplitPane.wrap(getSplitPaneHtml('vertical'));
        topSplitPane.wrap(getSplitPaneComponentHtml('left'));
        var newSplitPane = $('.split-pane-frame').children('div').first();
        newSplitPane.append(getSplitPaneDividerHtml('vertical'));
        newSplitPane.append(getSplitPaneComponentWithNewContent('right'));
        newSplitPane.splitPane();
    } else if ($(this).hasClass('left')) {
        topSplitPane.wrap(getSplitPaneHtml('vertical'));
        topSplitPane.wrap(getSplitPaneComponentHtml('right'));
        var newSplitPane = $('.split-pane-frame').children('div').first();
        newSplitPane.prepend(getSplitPaneDividerHtml('vertical'));
        newSplitPane.prepend(getSplitPaneComponentWithNewContent('left'));
        newSplitPane.splitPane();
    }
}

function closeClickHandler() {
    if (!$('.split-pane').length) {
        // We're at the topmost element
        var marginBox = $(this).closest('.marginBox');
        marginBox.empty();
        marginBox.append(getSplitPaneComponentInner());
        return;
    }

    var myComponent = $(this).closest('.split-pane-component');
    var sibling = myComponent.siblings('.split-pane-component').first('div');
    var toReplace = myComponent.parent().parent();
    var positionClass = toReplace.attr('class');
    toReplace.replaceWith(sibling);

    // The idea here is we need the position-* class from the parent to replace the sibling's position-* class.
    // This is working for now, but should be cleaned up since it could also add other classes.
    sibling.removeClass(function (index, css) {
        return (css.match (/(^|\s)position-\S+/g) || []).join(' ');
    });
    sibling.addClass(positionClass);
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
    var spci = $('<div class="split-pane-component-inner"><div class="box-header-off bloom-translationGroup"></div></div>');
    spci.append(getTypeSelectors());
    spci.append(getButtons());
    return spci;
}

function getOnOffSwitch() {
    var switchLabel = localizationManager.getText('EditTab.LayoutMode.ChangeLayout', 'Change Layout');
    return $('\
<div class="origami-toggle bloom-ui">' + switchLabel + ' \
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
    var buttons = $('<div class="origami-buttons bloom-ui origami-ui"></div>');
    buttons.append(getHSplitButton(true))
        .append('&nbsp;')
        .append(getHSplitButton())
        .append('&nbsp;')
        .append(getVSplitButton(true))
        .append('&nbsp;')
        .append(getVSplitButton())
        .append('&nbsp;')
        .append(getCloseButton());
    return buttons;
}
function getVSplitButton(left) {
    var vSplitButton;
    if (left)
        vSplitButton = $('<a class="button bloom-purple splitter-left">&#65513;</a>');
    else
        vSplitButton = $('<a class="button bloom-purple splitter-right">&#65515;</a>');
    vSplitButton.click(splitClickHandler);
    return vSplitButton;
}
function getHSplitButton(top) {
    var hSplitButton;
    if (top)
        hSplitButton = $('<a class="button bloom-purple splitter-top">&#65514;</a>');
    else
        hSplitButton = $('<a class="button bloom-purple splitter-bottom">&#65516;</a>');
    hSplitButton.click(splitClickHandler);
    return hSplitButton;
}
function getCloseButton() {
    var closeButton = $('<a class="button bloom-purple close">&#10005;</a>');
    closeButton.click(closeClickHandler);
    return closeButton;
}
function getTypeSelectors() {
    var links = $('<div class="selector-links bloom-ui origami-ui"></div>');
    var pictureLink = $('<a href="">Picture</a>');
    pictureLink.click(makePictureFieldClickHandler);
    var textLink = $('<a href="">Text</a>');
    textLink.click(makeTextFieldClickHandler);
    links.append(pictureLink).append(' or ').append(textLink);
    return links;
}
function makeTextFieldClickHandler(e) {
    e.preventDefault();
    //note, we're leaving it to some other part of the system, later, to add the needed .bloom-editable 
    //   divs (and set the right classes on them) inside of this new .bloom-translationGroup.
    var translationGroup = $('<div class="bloom-translationGroup bloom-trailingElement normal-style"></div>');
    $(this).closest('.split-pane-component-inner').append(translationGroup);
    $(this).closest('.selector-links').remove();
    //TODO: figure out if anything needs to get hooked up immediately
}
function makePictureFieldClickHandler(e) {
    e.preventDefault();
    var imageContainer = $('<div class="bloom-imageContainer bloom-leadingElement"></div>');
    var image = $('<img src="placeHolder.png" alt="Could not load the picture"/>');
    imageContainer.append(image);
    SetupImage(image); // Must attach it first so event handler gets added to parent
    $(this).closest('.split-pane-component-inner').append(imageContainer);
    $(this).closest('.selector-links').remove();
}

