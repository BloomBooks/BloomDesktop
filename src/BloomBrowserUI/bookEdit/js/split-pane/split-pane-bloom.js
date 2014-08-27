$(function() {
    $('div.split-pane').splitPane();
});
$(window).load(function() {
    setupSplitPaneComponentInners();
    $('.add').click(addClickHandler);
});

function setupSplitPaneComponentInners() {
    $('.split-pane-component-inner').each(function() {
        if (!$.trim( $(this).html() ).length)
            $(this).append(getTypeSelectors());
        $(this).append(getButtons());
    });
}

function splitClickHandler() {
    var myInner = $(this).closest('.split-pane-component-inner');
    var newSplitPane;
    if ($(this).hasClass('vertical')) {
        myInner.wrap(getSplitPaneHtml('vertical'));
        myInner.wrap(getSplitPaneComponentHtml('left'));
        newSplitPane = myInner.closest('.split-pane');
        newSplitPane.append(getSplitPaneDividerHtml('vertical'));
        newSplitPane.append(getSplitPaneComponentWithNewContent('right'));
    } else {
        myInner.wrap(getSplitPaneHtml('horizontal'));
        myInner.wrap(getSplitPaneComponentHtml('top'));
        newSplitPane = myInner.closest('.split-pane');
        newSplitPane.append(getSplitPaneDividerHtml('horizontal'));
        newSplitPane.append(getSplitPaneComponentWithNewContent('bottom'));
    }
    newSplitPane.splitPane();
}

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
    if (!$('.split-pane'))
        return;

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
function getSplitPaneComponentHtml(leftOrRight) {
    return $('<div class="split-pane-component position-' + leftOrRight + '"></div>');
}
function getSplitPaneDividerHtml(verticalOrHorizontal) {
    return $('<div class="split-pane-divider ' + verticalOrHorizontal + '-divider"></div>');
}
function getSplitPaneComponentWithNewContent(rightOrBottom) {
    var spc = $('<div class="split-pane-component position-' + rightOrBottom + '">');
    spc.append(getSplitPaneComponentInner());
    return spc;
}
function getSplitPaneComponentInner() {
    var spci = $('<div class="split-pane-component-inner">');
    spci.append(getTypeSelectors());
    spci.append(getButtons());
    return spci;
}

function getButtons() {
    var buttons = $('<div class="buttons bloom-ui"></div>');
    buttons.append(getVSplitButton())
        .append('&nbsp;')
        .append(getHSplitButton())
        .append('&nbsp;')
        .append(getCloseButton());
    return buttons;
}
function getVSplitButton() {
    var vSplitButton = $('<a class="button bloom-purple splitter vertical">&#124;</a>');
    vSplitButton.click(splitClickHandler);
    return vSplitButton;
}
function getHSplitButton() {
    var hSplitButton = $('<a class="button bloom-purple splitter horizontal">&#8211;&#8211;&#8211;</a>');
    hSplitButton.click(splitClickHandler);
    return hSplitButton;
}
function getCloseButton() {
    var closeButton = $('<a class="button bloom-purple close">&#215;</a>');
    closeButton.click(closeClickHandler);
    return closeButton;
}
function getTypeSelectors() {
    var links = $('<div class="selector-links bloom-ui"></div>');
    var pictureLink = $('<a href="">Picture</a>');
    pictureLink.click(makePictureFieldClickHandler);
    var textLink = $('<a href="">Text</a>');
    textLink.click(makeTextFieldClickHandler);
    links.append(pictureLink).append(' or ').append(textLink);
    return links;
}
function makeTextFieldClickHandler(e) {
    e.preventDefault();
    $(this).closest('.split-pane-component-inner').append('<div class="bloom-translationGroup bloom-trailingElement normal-style"><div lang="z" contenteditable="true" class="bloom-content1 bloom-editable"></div></div>');
    $(this).closest('.selector-links').remove();
}
function makePictureFieldClickHandler(e) {
    e.preventDefault();
    var imageContainer = $('<div class="bloom-imageContainer bloom-leadingElement"></div>');
    var image = $('<img src="placeHolder.png" alt="Could not load the picture"/>');
    SetupImage(image);
    imageContainer.append(image);
    SetupImageContainer(imageContainer);
    $(this).closest('.split-pane-component-inner').append(imageContainer);
    $(this).closest('.selector-links').remove();
}